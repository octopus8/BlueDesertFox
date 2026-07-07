using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using RaycastHit = Unity.Physics.RaycastHit;

/// <summary>
/// Baked ground-contact settings for <see cref="PlayerFollowObjectGroundContactSystem"/>.
/// </summary>
public struct PlayerFollowObjectGroundConfig : IComponentData
{
    public float bottomOffset;
    public float rayHeightAbove;
    public float rayLengthBelow;
    public int fallbackTerrainLayer;
    public int rideablePhysicsLayer;
    public float springStiffness;
    public float springDamping;
    public float groundFriction;
    public float groundedDistance;
    public float capsuleRadius;
    public float capsuleHalfCylinder;
    public float3 capsuleCenter;
}

/// <summary>
/// Runtime velocity for the player follow object when driven by ground contact.
/// </summary>
public struct PlayerFollowObjectMotionState : IComponentData
{
    public float3 velocity;
    public byte wasGrounded;
}

/// <summary>
/// Drives the Player Follow Object entity along terrain and rideable surfaces using Unity Physics raycasts
/// with a normal-axis spring-damper, and blocks obstacles via capsule casts.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
public partial class PlayerFollowObjectGroundContactSystem : SystemBase
{
    private const float ObstacleSkin = 0.001f;
    private const float MinCastDistance = 1e-4f;
    private const float GroundHitDistanceMargin = 0.5f;
    private const float WalkableSlopeThreshold = 0.5f;

    protected override void OnCreate()
    {
        RequireForUpdate<PlayerFollowObjectTag>();
        RequireForUpdate<PlayerFollowObjectGroundConfig>();
        RequireForUpdate<PlayerFollowObjectMotionState>();
        RequireForUpdate<TerrainTileConfig>();
    }

    protected override void OnUpdate()
    {
        float dt = SystemAPI.Time.DeltaTime;
        float3 gravity = (float3)Physics.gravity;

        bool hasPhysicsWorld = SystemAPI.TryGetSingleton<TerrainTileConfig>(out TerrainTileConfig terrainConfig)
            && terrainConfig.enablePhysicsColliders
            && SystemAPI.HasSingleton<PhysicsWorldSingleton>();

        CollisionWorld collisionWorld = default;

        if (hasPhysicsWorld)
        {
            Dependency.Complete();
            collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld.CollisionWorld;
        }

        foreach (var (config, motionState, localTransform) in SystemAPI
                     .Query<RefRO<PlayerFollowObjectGroundConfig>, RefRW<PlayerFollowObjectMotionState>, RefRW<LocalTransform>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            CollisionFilter groundFilter = default;
            CollisionFilter obstacleFilter = default;

            if (hasPhysicsWorld)
            {
                int terrainLayer = terrainConfig.terrainPhysicsLayer;
                int rideableLayer = config.ValueRO.rideablePhysicsLayer;
                if (rideableLayer < 0 || rideableLayer > 30)
                    rideableLayer = 15;

                uint terrainLayerMask = 1u << terrainLayer;
                uint rideableLayerMask = 1u << rideableLayer;
                uint groundLayerMask = terrainLayerMask | rideableLayerMask;
                groundFilter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = groundLayerMask,
                    GroupIndex = 0
                };
                obstacleFilter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = ~groundLayerMask,
                    GroupIndex = 0
                };
            }

            float3 position = localTransform.ValueRO.Position;
            float3 velocity = motionState.ValueRO.velocity;
            bool wasGrounded = motionState.ValueRO.wasGrounded != 0;
            bool grounded = false;
            float3 normal = math.up();

            if (hasPhysicsWorld
                && TryGetGroundHit(collisionWorld, groundFilter, position, config.ValueRO, out RaycastHit groundHit)
                && IsValidGroundHit(groundHit, position, config.ValueRO))
            {
                normal = math.normalizesafe(groundHit.SurfaceNormal, math.up());
                float3 surfaceTarget = groundHit.Position + normal * config.ValueRO.bottomOffset;
                float error = math.dot(surfaceTarget - position, normal);
                grounded = math.abs(error) < config.ValueRO.groundedDistance;
                bool approachingSurface = error > 0f && error < 3f;

                if (grounded || approachingSurface)
                {
                    if (grounded && !wasGrounded)
                    {
                        float vNormal = math.dot(velocity, normal);
                        if (vNormal > 0f)
                            velocity -= normal * vNormal;
                    }

                    velocity += GetTangentComponent(gravity, normal) * dt;
                    ApplySpringDamper(ref velocity, position, surfaceTarget, normal, config.ValueRO, dt);

                    if (grounded)
                        ApplyGroundFriction(ref velocity, normal, config.ValueRO.groundFriction, dt);
                }
                else
                {
                    velocity += gravity * dt;
                }
            }
            else
            {
                velocity += gravity * dt;
            }

            float3 startPosition = position;
            float3 displacement = velocity * dt;
            position = startPosition + displacement;

            if (hasPhysicsWorld && math.lengthsq(displacement) > MinCastDistance * MinCastDistance)
            {
                ResolveObstacleCollision(
                    ref position,
                    ref velocity,
                    startPosition,
                    displacement,
                    config.ValueRO,
                    collisionWorld,
                    obstacleFilter);
            }

            localTransform.ValueRW.Position = position;
            motionState.ValueRW.velocity = velocity;
            motionState.ValueRW.wasGrounded = grounded ? (byte)1 : (byte)0;
        }
    }

    private static bool IsValidGroundHit(RaycastHit hit, float3 position, in PlayerFollowObjectGroundConfig config)
    {
        float3 rayStart = position + math.up() * config.rayHeightAbove;
        float hitDistance = math.distance(rayStart, hit.Position);
        float maxDistance = config.rayHeightAbove + config.bottomOffset + config.groundedDistance + GroundHitDistanceMargin;
        return hitDistance <= maxDistance;
    }

    private static void ApplySpringDamper(
        ref float3 velocity,
        float3 position,
        float3 surfaceTarget,
        float3 normal,
        in PlayerFollowObjectGroundConfig config,
        float dt)
    {
        float error = math.dot(surfaceTarget - position, normal);
        float vNormal = math.dot(velocity, normal);
        float3 springAccel = normal * (config.springStiffness * error - config.springDamping * vNormal);
        velocity += springAccel * dt;
    }

    private static bool TryGetGroundHit(
        CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        float3 position,
        in PlayerFollowObjectGroundConfig config,
        out RaycastHit hit)
    {
        float3 rayStart = position + math.up() * config.rayHeightAbove;
        float3 rayEnd = position - math.up() * config.rayLengthBelow;

        var rayInput = new RaycastInput
        {
            Start = rayStart,
            End = rayEnd,
            Filter = groundFilter
        };

        return collisionWorld.CastRay(rayInput, out hit);
    }

    private static void ResolveObstacleCollision(
        ref float3 position,
        ref float3 velocity,
        float3 startPosition,
        float3 displacement,
        in PlayerFollowObjectGroundConfig config,
        CollisionWorld collisionWorld,
        CollisionFilter obstacleFilter)
    {
        float distance = math.length(displacement);
        if (distance < MinCastDistance)
            return;

        float3 direction = displacement / distance;
        GetCapsuleEndpoints(startPosition, config, out float3 point1, out float3 point2);

        if (!collisionWorld.CapsuleCast(
                point1,
                point2,
                config.capsuleRadius,
                direction,
                distance,
                out ColliderCastHit castHit,
                obstacleFilter))
        {
            return;
        }

        float3 obstacleNormal = math.normalizesafe(castHit.SurfaceNormal, math.up());
        if (math.dot(obstacleNormal, math.up()) >= WalkableSlopeThreshold)
            return;

        float fraction = math.max(castHit.Fraction - ObstacleSkin, 0f);
        position = startPosition + direction * (distance * fraction);
        velocity = RemoveNormalComponent(velocity, obstacleNormal);
    }

    private static void GetCapsuleEndpoints(
        float3 position,
        in PlayerFollowObjectGroundConfig config,
        out float3 point1,
        out float3 point2)
    {
        float3 center = position + config.capsuleCenter;
        float3 axis = math.up();
        point1 = center + axis * config.capsuleHalfCylinder;
        point2 = center - axis * config.capsuleHalfCylinder;
    }

    private static float3 RemoveNormalComponent(float3 velocity, float3 normal)
    {
        return velocity - normal * math.dot(velocity, normal);
    }

    private static float3 GetTangentComponent(float3 vector, float3 normal)
    {
        return RemoveNormalComponent(vector, normal);
    }

    private static void ApplyGroundFriction(ref float3 velocity, float3 normal, float groundFriction, float dt)
    {
        if (groundFriction <= 0f)
            return;

        float3 tangent = RemoveNormalComponent(velocity, normal);
        float damping = math.max(0f, 1f - groundFriction * dt);
        velocity = normal * math.dot(velocity, normal) + tangent * damping;
    }
}
