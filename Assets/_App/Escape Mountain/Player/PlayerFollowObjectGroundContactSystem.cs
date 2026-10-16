using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Baked ground-contact settings for <see cref="PlayerFollowObjectGroundContactSystem"/>.
/// </summary>
public struct PlayerFollowObjectGroundConfig : IComponentData
{
    public float rayHeightAbove;
    public float rayLengthBelow;
    public float groundFriction;
    public float yawRotationSmoothTime;
    public float minYawSpeed;
    public float sphereRadius;
    public float3 sphereCenter;
    public float3 gravity;
    public float maxPenetrationRecoverySpeed;
}

/// <summary>
/// Runtime terrain-relative velocity and contact state for the player follow object.
/// World velocity is <c>terrainRelativeVelocity - scrollVelocity</c> (see <see cref="TerrainScrollVelocityMath"/>).
/// </summary>
public struct PlayerFollowObjectMotionState : IComponentData
{
    public float3 terrainRelativeVelocity;
    public float smoothedYaw;
    public byte inContact;
    public byte hasPreviousContact;
    public float3 previousGroundNormal;
    public float3 contactPoint;
}

/// <summary>
/// Unilateral sliding sphere: gravity then collide. Surfaces only push (inbound normal
/// velocity is cancelled; separating motion is left ballistic). A displacement SphereCast
/// iterates along opposing hits so concave meshes can redirect speed. Overlap queries
/// recover embeds without snapping across a gap.
/// Integrates in terrain-relative velocity space so scroll motion and sliding do not compete.
/// Burst-compiled to avoid managed GC.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
// Must run before TerrainAnchorSystem / TileScrollPositionSystem. Those systems already apply this
// frame's scroll delta to colliders; casting against that end-of-frame pose while the rider is still
// at last frame's position starts the sphere inside thin scrolling faces and every cast misses.
[UpdateBefore(typeof(TerrainAnchorSystem))]
[UpdateBefore(typeof(TileScrollPositionSystem))]
public partial struct PlayerFollowObjectGroundContactSystem : ISystem
{
    private const float ObstacleSkin = 0.001f;
    private const float BlockingDepenetrationSkin = 0.01f;
    private const float MinCastDistance = 1e-4f;
    private const float MinSphereRadius = 1e-3f;
    private const float ContactSkin = 0.01f;
    private const float MinSlideSpeed = 0.01f;
    private const float SeparatingDotThreshold = 0.01f;
    private const float DefaultMaxPenetrationRecoverySpeed = 6f;
    private const int MaxSweepIterations = 4;
    private static readonly float3 DefaultGravity = new float3(0f, -9.81f, 0f);

    /// <summary>
    /// Closest SphereCast hit that the sphere is moving into. Ignores the follow-object entity
    /// and surfaces the motion is leaving. Grazing and inbound hits are kept so a shallow
    /// quarterpipe floor can redirect speed instead of being skipped until the wall.
    /// </summary>
    private struct OpposingHitCollector : ICollector<ColliderCastHit>
    {
        public Entity IgnoreEntity;
        public float3 Direction;
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public ColliderCastHit ClosestHit;

        public OpposingHitCollector(float maxFraction, Entity ignoreEntity, float3 direction)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            Direction = direction;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            float3 normal = math.normalizesafe(hit.SurfaceNormal, math.up());
            if (math.dot(normal, Direction) > SeparatingDotThreshold)
                return false;

            MaxFraction = hit.Fraction;
            ClosestHit = hit;
            NumHits = 1;
            return true;
        }
    }

    /// <summary>
    /// Closest distance hit that is not the follow-object entity.
    /// </summary>
    private struct IgnoreEntityDistanceCollector : ICollector<DistanceHit>
    {
        public Entity IgnoreEntity;
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public DistanceHit ClosestHit;

        public IgnoreEntityDistanceCollector(float maxFraction, Entity ignoreEntity)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            MaxFraction = hit.Fraction;
            ClosestHit = hit;
            NumHits = 1;
            return true;
        }
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerFollowObjectTag>();
        state.RequireForUpdate<PlayerFollowObjectGroundConfig>();
        state.RequireForUpdate<PlayerFollowObjectMotionState>();
        state.RequireForUpdate<PlayerFollowObjectBrakeState>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<TerrainScrollVelocity>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var paused) && paused.Value)
            return;

        float dt = SystemAPI.Time.DeltaTime;
        if (dt <= 0f)
            return;

        float3 scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>().WorldVelocity;

        bool hasPhysicsWorld = SystemAPI.TryGetSingleton(out TerrainTileConfig terrainConfig)
            && terrainConfig.enablePhysicsColliders
            && SystemAPI.HasSingleton<PhysicsWorldSingleton>();

        CollisionWorld collisionWorld = default;
        NativeList<RigidBody> anchoredBodies = default;
        CollisionFilter allFilter = CollisionFilter.Default;

        if (hasPhysicsWorld)
        {
            state.Dependency.Complete();
            collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld.CollisionWorld;
            allFilter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = ~0u,
                GroupIndex = 0
            };

            anchoredBodies = new NativeList<RigidBody>(8, Allocator.Temp);
            foreach (var (transform, collider, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<PhysicsCollider>>()
                         .WithAll<TerrainAnchorTag>()
                         .WithEntityAccess())
            {
                if (!collider.ValueRO.Value.IsCreated)
                    continue;

                LocalTransform lt = transform.ValueRO;
                float scale = lt.Scale;
                if (scale < 1e-4f)
                    scale = 1f;

                anchoredBodies.Add(new RigidBody
                {
                    Collider = collider.ValueRO.Value,
                    WorldFromBody = new RigidTransform(lt.Rotation, lt.Position),
                    Entity = entity,
                    Scale = scale
                });
            }
        }

        foreach (var (config, motionState, localTransform, brakeState, entity) in SystemAPI
                     .Query<RefRO<PlayerFollowObjectGroundConfig>, RefRW<PlayerFollowObjectMotionState>,
                         RefRW<LocalTransform>, RefRW<PlayerFollowObjectBrakeState>>()
                     .WithAll<PlayerFollowObjectTag>()
                     .WithEntityAccess())
        {
            bool braking = brakeState.ValueRO.active != 0;

            float3 gravity = config.ValueRO.gravity;
            if (math.lengthsq(gravity) < 1e-8f)
                gravity = DefaultGravity;

            float3 position = localTransform.ValueRO.Position;
            float3 terrainRelativeVelocity = motionState.ValueRO.terrainRelativeVelocity;
            float smoothedYaw = motionState.ValueRO.smoothedYaw;
            float3 previousGroundNormal = motionState.ValueRO.previousGroundNormal;
            if (math.lengthsq(previousGroundNormal) < 0.01f)
                previousGroundNormal = math.up();

            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);

            if (!braking)
                worldVelocity += gravity * dt;

            if (braking)
            {
                ApplyBrakeDeceleration(
                    ref worldVelocity,
                    scrollVelocity,
                    brakeState.ValueRO.deceleration,
                    dt);

                if (brakeState.ValueRO.holdAfterStop == 0
                    && math.lengthsq(TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                        worldVelocity, scrollVelocity)) < MinSlideSpeed * MinSlideSpeed)
                {
                    brakeState.ValueRW.active = 0;
                }
            }

            terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);

            // Colliders (tiles + TerrainAnchors) still sit at last frame's scroll pose while we run.
            // World displacement includes -scroll, but those colliders will also move by -scroll this
            // frame — sweeping world motion against them double-counts scroll and tunnels thin faces.
            // Sweep terrain-relative motion first, then apply scroll once.
            float3 startPosition = position;
            float3 scrollDelta = scrollVelocity * dt;
            float3 relativeDisplacement = terrainRelativeVelocity * dt;

            bool hasContact = false;

            if (hasPhysicsWorld)
            {
                if (SweepAndSlide(
                        ref position,
                        ref terrainRelativeVelocity,
                        startPosition,
                        relativeDisplacement,
                        config.ValueRO,
                        collisionWorld,
                        allFilter,
                        entity,
                        out float3 sweepNormal))
                {
                    hasContact = true;
                    previousGroundNormal = sweepNormal;
                }
            }
            else
            {
                position = startPosition + relativeDisplacement;
            }

            position -= scrollDelta;

            if (hasPhysicsWorld)
            {
                if (TryDepenetrateWorld(
                        ref position,
                        ref terrainRelativeVelocity,
                        collisionWorld,
                        allFilter,
                        entity,
                        config.ValueRO,
                        dt,
                        out float3 worldNormal))
                {
                    hasContact = true;
                    previousGroundNormal = worldNormal;
                }

                if (anchoredBodies.IsCreated && anchoredBodies.Length > 0
                    && TryDepenetrateAnchoredBodies(
                        ref position,
                        ref terrainRelativeVelocity,
                        anchoredBodies,
                        config.ValueRO,
                        dt,
                        out float3 anchorNormal))
                {
                    hasContact = true;
                    previousGroundNormal = anchorNormal;
                }
            }

            if (hasContact && !braking)
            {
                worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                    terrainRelativeVelocity,
                    scrollVelocity);
                ApplyGroundFriction(
                    ref worldVelocity,
                    scrollVelocity,
                    previousGroundNormal,
                    config.ValueRO.groundFriction,
                    dt);
                terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                    worldVelocity,
                    scrollVelocity);
            }

            float radius = math.max(config.ValueRO.sphereRadius, MinSphereRadius);
            float3 publishedContact = hasContact
                ? position + config.ValueRO.sphereCenter - previousGroundNormal * radius
                : position + config.ValueRO.sphereCenter;

            localTransform.ValueRW.Position = position;
            motionState.ValueRW.terrainRelativeVelocity = terrainRelativeVelocity;
            motionState.ValueRW.inContact = hasContact ? (byte)1 : (byte)0;
            motionState.ValueRW.hasPreviousContact = hasContact ? (byte)1 : (byte)0;
            motionState.ValueRW.previousGroundNormal = previousGroundNormal;
            motionState.ValueRW.contactPoint = publishedContact;

            UpdateSmoothedYaw(
                ref smoothedYaw,
                TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(terrainRelativeVelocity, scrollVelocity),
                config.ValueRO.minYawSpeed,
                config.ValueRO.yawRotationSmoothTime,
                dt);
            motionState.ValueRW.smoothedYaw = smoothedYaw;
            localTransform.ValueRW.Rotation = quaternion.RotateY(smoothedYaw);
        }

        if (anchoredBodies.IsCreated)
            anchoredBodies.Dispose();
    }

    /// <summary>
    /// If the sphere overlaps a collider in the physics world, push out along the true surface
    /// normal and cancel inbound velocity. Does not snap across a gap.
    /// </summary>
    private static bool TryDepenetrateWorld(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        CollisionWorld collisionWorld,
        CollisionFilter filter,
        Entity ignoreEntity,
        in PlayerFollowObjectGroundConfig config,
        float dt,
        out float3 contactNormal)
    {
        contactNormal = math.up();
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        float3 center = position + config.sphereCenter;

        var input = new PointDistanceInput
        {
            Position = center,
            MaxDistance = radius,
            Filter = filter
        };

        // Distance collectors use MaxFraction as a distance (not a 0-1 fraction).
        var collector = new IgnoreEntityDistanceCollector(radius, ignoreEntity);
        if (!collisionWorld.CalculateDistance(input, ref collector) || collector.NumHits == 0)
            return false;

        DistanceHit hit = collector.ClosestHit;
        float penetration = radius - hit.Distance;
        if (penetration <= ObstacleSkin)
            return false;

        float3 n = math.normalizesafe(hit.SurfaceNormal, float3.zero);
        if (math.lengthsq(n) < 0.5f)
            return false;

        ApplyDepenetration(ref position, ref terrainRelativeVelocity, n, penetration, config, dt);
        contactNormal = n;
        return true;
    }

    /// <summary>
    /// If the sphere overlaps a scrolling TerrainAnchor mesh, push out along the true surface
    /// normal and cancel inbound velocity. Recovers cases SphereCast misses from inside a thin face.
    /// </summary>
    private static bool TryDepenetrateAnchoredBodies(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        NativeList<RigidBody> anchoredBodies,
        in PlayerFollowObjectGroundConfig config,
        float dt,
        out float3 contactNormal)
    {
        contactNormal = math.up();
        float3 center = position + config.sphereCenter;
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        float maxDistance = radius;

        float deepestPenetration = 0f;
        float3 bestNormal = float3.zero;
        bool found = false;

        CollisionFilter filter = CollisionFilter.Default;

        for (int bodyIndex = 0; bodyIndex < anchoredBodies.Length; bodyIndex++)
        {
            RigidBody body = anchoredBodies[bodyIndex];
            if (!body.Collider.IsCreated)
                continue;

            var input = new PointDistanceInput
            {
                Position = center,
                MaxDistance = maxDistance,
                Filter = filter
            };

            if (!body.CalculateDistance(input, out DistanceHit hit))
                continue;

            float penetration = radius - hit.Distance;
            if (penetration <= ObstacleSkin)
                continue;

            float3 n = math.normalizesafe(hit.SurfaceNormal, float3.zero);
            if (math.lengthsq(n) < 0.5f)
                continue;

            if (penetration > deepestPenetration)
            {
                deepestPenetration = penetration;
                bestNormal = n;
                found = true;
            }
        }

        if (!found)
            return false;

        ApplyDepenetration(ref position, ref terrainRelativeVelocity, bestNormal, deepestPenetration, config, dt);
        contactNormal = bestNormal;
        return true;
    }

    private static void ApplyDepenetration(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        float3 normal,
        float penetration,
        in PlayerFollowObjectGroundConfig config,
        float dt)
    {
        float recoverySpeed = config.maxPenetrationRecoverySpeed;
        if (recoverySpeed <= 0f)
            recoverySpeed = DefaultMaxPenetrationRecoverySpeed;
        float maxPush = recoverySpeed * math.max(0f, dt);
        float push = math.min(penetration + ContactSkin, maxPush);
        position += normal * push;

        float vNormal = math.dot(terrainRelativeVelocity, normal);
        if (vNormal < 0f)
            terrainRelativeVelocity -= normal * vNormal;
    }

    /// <summary>
    /// SphereCasts along the step and stops at each surface the sphere is moving into, sliding
    /// remaining travel along that surface's true tangent. Up to <see cref="MaxSweepIterations"/>
    /// opposing hits. Returns true if a hit constrained the step. Head-on walls (no remaining
    /// tangent) stop the slide.
    /// </summary>
    private static bool SweepAndSlide(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        float3 startPosition,
        float3 displacement,
        in PlayerFollowObjectGroundConfig config,
        CollisionWorld collisionWorld,
        CollisionFilter filter,
        Entity ignoreEntity,
        out float3 hitNormal)
    {
        hitNormal = math.up();
        float3 remaining = displacement;
        float3 currentPosition = startPosition;
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        bool anyHit = false;

        for (int iteration = 0; iteration < MaxSweepIterations; iteration++)
        {
            float distance = math.length(remaining);
            if (distance < MinCastDistance)
                break;

            float3 direction = remaining / distance;
            if (!TryCastOpposingSphere(
                    collisionWorld,
                    currentPosition + config.sphereCenter,
                    radius,
                    direction,
                    distance,
                    filter,
                    ignoreEntity,
                    out ColliderCastHit castHit))
            {
                currentPosition += remaining;
                remaining = float3.zero;
                break;
            }

            anyHit = true;
            hitNormal = math.normalizesafe(castHit.SurfaceNormal, math.up());
            float fraction = math.max(castHit.Fraction - ObstacleSkin, 0f);
            currentPosition += direction * (distance * fraction);

            if (castHit.Fraction <= ObstacleSkin)
                currentPosition += hitNormal * BlockingDepenetrationSkin;

            float approachRate = math.dot(terrainRelativeVelocity, hitNormal);
            if (approachRate < 0f)
                terrainRelativeVelocity -= hitNormal * approachRate;

            float remainder = distance * (1f - fraction);
            if (remainder < MinCastDistance)
            {
                remaining = float3.zero;
                break;
            }

            float3 slideDirection = RemoveNormalComponent(direction, hitNormal);
            slideDirection = math.normalizesafe(slideDirection, float3.zero);
            if (math.lengthsq(slideDirection) < 0.5f)
            {
                remaining = float3.zero;
                break;
            }

            remaining = slideDirection * remainder;
        }

        position = currentPosition;
        return anyHit;
    }

    private static bool TryCastOpposingSphere(
        CollisionWorld collisionWorld,
        float3 center,
        float radius,
        float3 direction,
        float maxDistance,
        CollisionFilter filter,
        Entity ignoreEntity,
        out ColliderCastHit hit)
    {
        hit = default;
        if (maxDistance < MinCastDistance)
            return false;

        var collector = new OpposingHitCollector(1f, ignoreEntity, direction);
        collisionWorld.SphereCastCustom(
            center,
            radius,
            direction,
            maxDistance,
            ref collector,
            filter);

        if (collector.NumHits == 0)
            return false;

        hit = collector.ClosestHit;
        return true;
    }

    private static float3 RemoveNormalComponent(float3 velocity, float3 normal)
    {
        return velocity - normal * math.dot(velocity, normal);
    }

    private static void ApplyGroundFriction(
        ref float3 worldVelocity,
        float3 scrollVelocity,
        float3 normal,
        float groundFriction,
        float dt)
    {
        if (groundFriction <= 0f)
            return;

        float3 surfaceRelative = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);
        float3 tangent = RemoveNormalComponent(surfaceRelative, normal);
        float damping = math.max(0f, 1f - groundFriction * dt);
        surfaceRelative = normal * math.dot(surfaceRelative, normal) + tangent * damping;
        worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(surfaceRelative, scrollVelocity);
    }

    /// <summary>
    /// Linearly reduces terrain-relative speed toward zero for finish-line / stop-volume braking.
    /// </summary>
    private static void ApplyBrakeDeceleration(
        ref float3 worldVelocity,
        float3 scrollVelocity,
        float deceleration,
        float dt)
    {
        if (deceleration <= 0f)
            return;

        float3 terrainRelative = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);
        float speed = math.length(terrainRelative);
        if (speed <= MinSlideSpeed)
        {
            worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(float3.zero, scrollVelocity);
            return;
        }

        float newSpeed = math.max(0f, speed - deceleration * dt);
        if (newSpeed <= MinSlideSpeed)
            terrainRelative = float3.zero;
        else
            terrainRelative *= newSpeed / speed;

        worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(terrainRelative, scrollVelocity);
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
