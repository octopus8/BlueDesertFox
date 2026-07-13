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
    public int rideablePhysicsLayer;
    public float springStiffness;
    public float springDamping;
    public float groundFriction;
    public float groundedDistance;
    public float approachingSurfaceMaxDistance;
    public float takeoffSpeed;
    public float airborneGraceTime;
    public float minCrestSpeed;
    public float crestNormalDotThreshold;
    public float yawRotationSmoothTime;
    public float minYawSpeed;
    public float capsuleRadius;
    public float capsuleHalfCylinder;
    public float3 capsuleCenter;
}

/// <summary>
/// Runtime terrain-relative velocity for the player follow object when driven by ground contact.
/// World velocity is <c>terrainRelativeVelocity - scrollVelocity</c> (see <see cref="TerrainScrollVelocityMath"/>).
/// </summary>
public struct PlayerFollowObjectMotionState : IComponentData
{
    public float3 terrainRelativeVelocity;
    public float smoothedYaw;
    public byte wasGrounded;
    public byte wasInSurfaceContact;
    public float airborneTimeRemaining;
    public float3 previousGroundNormal;
}

/// <summary>
/// Drives the Player Follow Object entity along terrain and rideable surfaces using Unity Physics raycasts
/// with a normal-axis spring-damper, and blocks obstacles via capsule casts.
/// Integrates in terrain-relative velocity space so scroll motion and ramp slide do not compete.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(TileScrollPositionSystem))]
public partial class PlayerFollowObjectGroundContactSystem : SystemBase
{
    private const float ObstacleSkin = 0.001f;
    private const float MinCastDistance = 1e-4f;
    private const float GroundHitDistanceMargin = 0.5f;
    private const float WalkableSlopeThreshold = 0.5f;
    private const float FlatGroundNormalThreshold = 0.95f;
    private const float ScrollBaselineLerpSpeed = 12f;
    private const float ScrollActiveSpeedSq = 0.01f;

    protected override void OnCreate()
    {
        RequireForUpdate<PlayerFollowObjectTag>();
        RequireForUpdate<PlayerFollowObjectGroundConfig>();
        RequireForUpdate<PlayerFollowObjectMotionState>();
        RequireForUpdate<TerrainTileConfig>();
        RequireForUpdate<TerrainScrollVelocity>();
    }

    protected override void OnUpdate()
    {
        float dt = SystemAPI.Time.DeltaTime;
        float3 gravity = (float3)Physics.gravity;
        float3 scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>().WorldVelocity;
        bool scrollActive = math.lengthsq(scrollVelocity) > ScrollActiveSpeedSq;

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
            float3 terrainRelativeVelocity = motionState.ValueRO.terrainRelativeVelocity;
            bool wasGrounded = motionState.ValueRO.wasGrounded != 0;
            float airborneTimeRemaining = motionState.ValueRO.airborneTimeRemaining;
            float3 previousGroundNormal = motionState.ValueRO.previousGroundNormal;
            if (math.lengthsq(previousGroundNormal) < 0.01f)
                previousGroundNormal = math.up();

            bool grounded = false;
            bool inSurfaceContact = false;
            float3 normal = math.up();
            bool forceAirborne = airborneTimeRemaining > 0f;

            if (forceAirborne)
            {
                airborneTimeRemaining = math.max(0f, airborneTimeRemaining - dt);
                terrainRelativeVelocity += gravity * dt;

                if (hasPhysicsWorld
                    && TryGetGroundHit(collisionWorld, groundFilter, position, config.ValueRO, out RaycastHit airborneHit)
                    && IsValidGroundHit(airborneHit, position, config.ValueRO))
                {
                    previousGroundNormal = math.normalizesafe(airborneHit.SurfaceNormal, math.up());
                }
            }
            else if (hasPhysicsWorld
                && TryGetGroundHit(collisionWorld, groundFilter, position, config.ValueRO, out RaycastHit groundHit)
                && IsValidGroundHit(groundHit, position, config.ValueRO))
            {
                normal = math.normalizesafe(groundHit.SurfaceNormal, math.up());
                float3 surfaceTarget = groundHit.Position + normal * config.ValueRO.bottomOffset;
                float error = math.dot(surfaceTarget - position, normal);
                grounded = math.abs(error) < config.ValueRO.groundedDistance;
                bool approachingSurface = error > 0f && error < config.ValueRO.approachingSurfaceMaxDistance;
                bool contactCandidate = grounded || approachingSurface;

                float3 contactWorldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                    terrainRelativeVelocity,
                    scrollVelocity);
                float vNormal = math.dot(contactWorldVelocity, normal);
                float3 flatVelocity = new float3(contactWorldVelocity.x, 0f, contactWorldVelocity.z);
                float flatSpeed = math.length(flatVelocity);

                bool takeoffFromSpeed = config.ValueRO.takeoffSpeed > 0f
                    && contactCandidate
                    && vNormal > config.ValueRO.takeoffSpeed;
                bool takeoffFromCrest = wasGrounded
                    && config.ValueRO.minCrestSpeed > 0f
                    && flatSpeed >= config.ValueRO.minCrestSpeed
                    && math.dot(previousGroundNormal, normal) < config.ValueRO.crestNormalDotThreshold;

                if (takeoffFromSpeed || takeoffFromCrest)
                {
                    airborneTimeRemaining = config.ValueRO.airborneGraceTime;
                    terrainRelativeVelocity += gravity * dt;
                    grounded = false;
                }
                else if (contactCandidate)
                {
                    inSurfaceContact = true;

                    if (grounded && !wasGrounded)
                    {
                        if (scrollActive)
                            terrainRelativeVelocity = scrollVelocity;

                        float3 touchdownVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                            terrainRelativeVelocity,
                            scrollVelocity);
                        float touchdownNormal = math.dot(touchdownVelocity, normal);
                        if (touchdownNormal > 0f)
                        {
                            touchdownVelocity -= normal * touchdownNormal;
                            terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                                touchdownVelocity,
                                scrollVelocity);
                        }
                    }

                    terrainRelativeVelocity += GetTangentComponent(gravity, normal) * dt;
                    ApplySpringDamper(
                        ref terrainRelativeVelocity,
                        scrollVelocity,
                        position,
                        surfaceTarget,
                        normal,
                        config.ValueRO,
                        dt);

                    if (grounded)
                    {
                        ApplyGroundFriction(ref terrainRelativeVelocity, scrollVelocity, normal, config.ValueRO.groundFriction, dt);

                        if (scrollActive && normal.y >= FlatGroundNormalThreshold)
                            ApplyScrollBaseline(ref terrainRelativeVelocity, scrollVelocity, dt);
                    }
                }
                else
                {
                    terrainRelativeVelocity += gravity * dt;
                }

                previousGroundNormal = normal;
            }
            else
            {
                terrainRelativeVelocity += gravity * dt;
            }

            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);
            float3 startPosition = position;
            float3 displacement = worldVelocity * dt;
            float3 goalPosition = startPosition + displacement;
            position = goalPosition;

            bool resolvedSteepTerrainCollision = false;

            if (hasPhysicsWorld && math.lengthsq(displacement) > MinCastDistance * MinCastDistance)
            {
                resolvedSteepTerrainCollision = ResolveTerrainCollision(
                    ref position,
                    ref terrainRelativeVelocity,
                    scrollVelocity,
                    startPosition,
                    displacement,
                    config.ValueRO,
                    collisionWorld,
                    groundFilter);

                float3 traveledDisplacement = position - startPosition;
                if (math.lengthsq(traveledDisplacement) > MinCastDistance * MinCastDistance)
                {
                    ResolveObstacleCollision(
                        ref position,
                        ref terrainRelativeVelocity,
                        scrollVelocity,
                        startPosition,
                        traveledDisplacement,
                        config.ValueRO,
                        collisionWorld,
                        obstacleFilter);
                }
            }

            if (hasPhysicsWorld && (resolvedSteepTerrainCollision || !grounded))
            {
                SnapToTerrainSurface(
                    ref position,
                    collisionWorld,
                    groundFilter,
                    config.ValueRO);
            }

            localTransform.ValueRW.Position = position;
            motionState.ValueRW.terrainRelativeVelocity = terrainRelativeVelocity;
            motionState.ValueRW.wasGrounded = grounded ? (byte)1 : (byte)0;
            motionState.ValueRW.wasInSurfaceContact = inSurfaceContact ? (byte)1 : (byte)0;
            motionState.ValueRW.airborneTimeRemaining = airborneTimeRemaining;
            motionState.ValueRW.previousGroundNormal = previousGroundNormal;

            float smoothedYaw = motionState.ValueRO.smoothedYaw;
            UpdateSmoothedYaw(
                ref smoothedYaw,
                worldVelocity,
                config.ValueRO.minYawSpeed,
                config.ValueRO.yawRotationSmoothTime,
                dt);
            motionState.ValueRW.smoothedYaw = smoothedYaw;
            localTransform.ValueRW.Rotation = quaternion.RotateY(smoothedYaw);
        }
    }

    private static void ApplyScrollBaseline(ref float3 terrainRelativeVelocity, float3 scrollVelocity, float dt)
    {
        float3 target = new float3(scrollVelocity.x, terrainRelativeVelocity.y, scrollVelocity.z);
        float t = math.saturate(ScrollBaselineLerpSpeed * dt);
        float3 flat = new float3(terrainRelativeVelocity.x, 0f, terrainRelativeVelocity.z);
        float3 targetFlat = new float3(scrollVelocity.x, 0f, scrollVelocity.z);
        float3 lerpedFlat = math.lerp(flat, targetFlat, t);
        terrainRelativeVelocity = new float3(lerpedFlat.x, target.y, lerpedFlat.z);
    }

    private static bool IsValidGroundHit(RaycastHit hit, float3 position, in PlayerFollowObjectGroundConfig config)
    {
        float3 rayStart = position + math.up() * config.rayHeightAbove;
        float hitDistance = math.distance(rayStart, hit.Position);
        float maxDistance = config.rayHeightAbove + config.bottomOffset + config.groundedDistance + GroundHitDistanceMargin;
        return hitDistance <= maxDistance;
    }

    private static void ApplySpringDamper(
        ref float3 terrainRelativeVelocity,
        float3 scrollVelocity,
        float3 position,
        float3 surfaceTarget,
        float3 normal,
        in PlayerFollowObjectGroundConfig config,
        float dt)
    {
        float error = math.dot(surfaceTarget - position, normal);
        float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
            terrainRelativeVelocity,
            scrollVelocity);
        float vNormal = math.dot(worldVelocity, normal);
        float3 springAccel = normal * (config.springStiffness * error - config.springDamping * vNormal);
        worldVelocity += springAccel * dt;
        terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);
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

    private static bool ResolveTerrainCollision(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        float3 scrollVelocity,
        float3 startPosition,
        float3 displacement,
        in PlayerFollowObjectGroundConfig config,
        CollisionWorld collisionWorld,
        CollisionFilter terrainFilter)
    {
        float distance = math.length(displacement);
        if (distance < MinCastDistance)
            return false;

        float3 direction = displacement / distance;
        GetCapsuleEndpoints(startPosition, config, out float3 point1, out float3 point2);

        if (!collisionWorld.CapsuleCast(
                point1,
                point2,
                config.capsuleRadius,
                direction,
                distance,
                out ColliderCastHit castHit,
                terrainFilter))
        {
            return false;
        }

        float3 terrainNormal = math.normalizesafe(castHit.SurfaceNormal, math.up());
        if (terrainNormal.y >= WalkableSlopeThreshold)
            return false;

        if (math.dot(direction, terrainNormal) >= -ObstacleSkin)
            return false;

        float fraction = math.max(castHit.Fraction - ObstacleSkin, 0f);
        position = startPosition + direction * (distance * fraction);

        float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
            terrainRelativeVelocity,
            scrollVelocity);
        worldVelocity = RemoveNormalComponent(worldVelocity, terrainNormal);
        terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);
        return true;
    }

    private static void SnapToTerrainSurface(
        ref float3 position,
        CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        in PlayerFollowObjectGroundConfig config)
    {
        if (!TryGetGroundHit(collisionWorld, groundFilter, position, config, out RaycastHit hit)
            || !IsValidGroundHit(hit, position, config))
        {
            return;
        }

        float3 normal = math.normalizesafe(hit.SurfaceNormal, math.up());
        float3 surfaceTarget = hit.Position + normal * config.bottomOffset;
        float error = math.dot(surfaceTarget - position, normal);

        if (math.abs(error) <= config.groundedDistance)
            position += normal * error;
    }

    private static void ResolveObstacleCollision(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        float3 scrollVelocity,
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

        float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
            terrainRelativeVelocity,
            scrollVelocity);
        worldVelocity = RemoveNormalComponent(worldVelocity, obstacleNormal);
        terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);
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

    private static void ApplyGroundFriction(
        ref float3 terrainRelativeVelocity,
        float3 scrollVelocity,
        float3 normal,
        float groundFriction,
        float dt)
    {
        if (groundFriction <= 0f)
            return;

        float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
            terrainRelativeVelocity,
            scrollVelocity);
        float3 tangent = RemoveNormalComponent(worldVelocity, normal);
        float damping = math.max(0f, 1f - groundFriction * dt);
        worldVelocity = normal * math.dot(worldVelocity, normal) + tangent * damping;
        terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);
    }

    private static void UpdateSmoothedYaw(
        ref float smoothedYaw,
        float3 worldVelocity,
        float minYawSpeed,
        float yawRotationSmoothTime,
        float dt)
    {
        float3 flat = new float3(worldVelocity.x, 0f, worldVelocity.z);
        if (math.lengthsq(flat) < minYawSpeed * minYawSpeed)
            return;

        float targetYaw = math.atan2(flat.x, flat.z);
        float delta = math.atan2(math.sin(targetYaw - smoothedYaw), math.cos(targetYaw - smoothedYaw));
        float t = yawRotationSmoothTime <= 0f ? 1f : math.saturate(dt / yawRotationSmoothTime);
        smoothedYaw += delta * t;
    }
}
