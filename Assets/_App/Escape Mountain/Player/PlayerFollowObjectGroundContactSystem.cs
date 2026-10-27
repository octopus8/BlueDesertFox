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
    /// <summary>Latched once the rider touches a Pipe-layer collider; cleared on landing on a non-Pipe surface.</summary>
    public byte pipeMode;
}

/// <summary>
/// Hybrid ground contact: sticky probe/snap on snow and other non-Pipe colliders, unilateral
/// gravity-then-collide on the Pipe layer so quarterpipes can launch. Surfaces only push while
/// on a pipe (inbound normal velocity is cancelled; separating motion is left ballistic).
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
    private const float MinCastDistance = 1e-4f;
    private const float MinSphereRadius = 1e-3f;
    private const float StickyContactSkin = 0.05f;
    private const float MinSlideSpeed = 0.01f;
    private const float SeparatingDotThreshold = 0.01f;
    private const float DefaultMaxPenetrationRecoverySpeed = 6f;
    private const int MaxSweepIterations = 4;
    // Sticky snapping only grabs floor-like surfaces; anything steeper is left to the sweep.
    private const float MinStickyGroundUp = 0.5f;
    // TagManager: Pipe is user layer index 15.
    private const uint PipeLayerMask = 1u << 15;
    private static readonly float3 DefaultGravity = new float3(0f, -9.81f, 0f);

    /// <summary>
    /// Collector that can reject TerrainAnchor hits. Set for CollisionWorld queries, whose anchor
    /// poses may be stale; cleared when querying the anchored bodies directly.
    /// </summary>
    private interface IAnchorSkippingCollector
    {
        bool SkipAnchored { get; set; }
    }

    /// <summary>
    /// Closest SphereCast hit that the sphere is moving into. Ignores the follow-object entity
    /// and surfaces the motion is leaving. Grazing and inbound hits are kept so a shallow
    /// quarterpipe floor can redirect speed instead of being skipped until the wall.
    /// </summary>
    private struct OpposingHitCollector : ICollector<ColliderCastHit>, IAnchorSkippingCollector
    {
        public Entity IgnoreEntity;
        public float3 Direction;
        public ComponentLookup<TerrainAnchorTag> AnchorLookup;
        public bool SkipAnchored { get; set; }
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public ColliderCastHit ClosestHit;

        public OpposingHitCollector(
            float maxFraction,
            Entity ignoreEntity,
            float3 direction,
            ComponentLookup<TerrainAnchorTag> anchorLookup,
            bool skipAnchored)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            Direction = direction;
            AnchorLookup = anchorLookup;
            SkipAnchored = skipAnchored;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            if (SkipAnchored && AnchorLookup.HasComponent(hit.Entity))
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
    /// Closest SphereCast hit that is not the follow-object entity and whose normal's world-up
    /// component is at least <see cref="MinUp"/> (pass -1 to accept any orientation).
    /// </summary>
    private struct IgnoreEntityCastCollector : ICollector<ColliderCastHit>, IAnchorSkippingCollector
    {
        public Entity IgnoreEntity;
        public float MinUp;
        public ComponentLookup<TerrainAnchorTag> AnchorLookup;
        public bool SkipAnchored { get; set; }
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public ColliderCastHit ClosestHit;

        public IgnoreEntityCastCollector(
            float maxFraction,
            Entity ignoreEntity,
            ComponentLookup<TerrainAnchorTag> anchorLookup,
            bool skipAnchored,
            float minUp = -1f)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            MinUp = minUp;
            AnchorLookup = anchorLookup;
            SkipAnchored = skipAnchored;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            if (SkipAnchored && AnchorLookup.HasComponent(hit.Entity))
                return false;

            if (math.normalizesafe(hit.SurfaceNormal, math.up()).y < MinUp)
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
        public ComponentLookup<TerrainAnchorTag> AnchorLookup;
        /// <summary>Reject TerrainAnchor hits (set for CollisionWorld queries, whose anchor poses may be stale).</summary>
        public bool SkipAnchored;
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public DistanceHit ClosestHit;

        public IgnoreEntityDistanceCollector(
            float maxFraction,
            Entity ignoreEntity,
            ComponentLookup<TerrainAnchorTag> anchorLookup,
            bool skipAnchored)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            AnchorLookup = anchorLookup;
            SkipAnchored = skipAnchored;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(DistanceHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            if (SkipAnchored && AnchorLookup.HasComponent(hit.Entity))
                return false;

            MaxFraction = hit.Fraction;
            ClosestHit = hit;
            NumHits = 1;
            return true;
        }
    }

    private ComponentLookup<TerrainAnchorTag> _anchorLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _anchorLookup = state.GetComponentLookup<TerrainAnchorTag>(isReadOnly: true);
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

        _anchorLookup.Update(ref state);
        ComponentLookup<TerrainAnchorTag> anchorLookup = _anchorLookup;

        CollisionWorld collisionWorld = default;
        // Built from LocalTransform before TerrainAnchorSystem runs, so it always holds last frame's
        // scroll pose. CollisionWorld only refreshes on fixed steps and can be an extra frame behind,
        // so every query skips TerrainAnchor hits there and tests these bodies directly instead.
        NativeList<RigidBody> anchoredBodies = default;
        CollisionFilter allFilter = CollisionFilter.Default;
        CollisionFilter pipeFilter = CollisionFilter.Default;
        CollisionFilter nonPipeFilter = CollisionFilter.Default;

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
            pipeFilter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = PipeLayerMask,
                GroupIndex = 0
            };
            nonPipeFilter = new CollisionFilter
            {
                BelongsTo = ~0u,
                CollidesWith = ~PipeLayerMask,
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

            float3 startPosition = position;
            float3 scrollDelta = scrollVelocity * dt;

            float3 detectWorldVelocity = worldVelocity;
            if (!braking)
                detectWorldVelocity += gravity * dt;
            float3 detectRelative = TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                detectWorldVelocity,
                scrollVelocity);
            float3 relativeDisplacement = detectRelative * dt;

            // Pipe mode latches through the whole ride and air time: the sticky probe must not run
            // between pipe contacts or it searches along a near-vertical pipe normal and snaps onto
            // unrelated geometry.
            bool onPipe = hasPhysicsWorld
                && (motionState.ValueRO.pipeMode != 0
                    || IsTouchingPipe(
                        collisionWorld,
                        anchoredBodies,
                        anchorLookup,
                        position,
                        relativeDisplacement,
                        config.ValueRO,
                        pipeFilter,
                        entity));
            byte pipeMode = 0;

            bool hasContact = false;

            if (onPipe)
            {
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

                terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                    worldVelocity,
                    scrollVelocity);

                // Colliders (tiles + TerrainAnchors) still sit at last frame's scroll pose while we run.
                // World displacement includes -scroll, but those colliders will also move by -scroll this
                // frame — sweeping world motion against them double-counts scroll and tunnels thin faces.
                // Sweep terrain-relative motion first, then apply scroll once.
                relativeDisplacement = terrainRelativeVelocity * dt;

                if (SweepAndSlide(
                        ref position,
                        ref terrainRelativeVelocity,
                        startPosition,
                        relativeDisplacement,
                        config.ValueRO,
                        collisionWorld,
                        anchoredBodies,
                        anchorLookup,
                        allFilter,
                        entity,
                        out float3 sweepNormal))
                {
                    hasContact = true;
                    previousGroundNormal = sweepNormal;
                }

                // Depenetrate against last-frame colliders before applying this frame's scroll.
                // Doing it after scrollDelta tests the rider at the new pose against the old pipe
                // and shoves along the wall normal every frame.
                if (TryDepenetrateWorld(
                        ref position,
                        ref terrainRelativeVelocity,
                        collisionWorld,
                        anchorLookup,
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
                        allFilter,
                        config.ValueRO,
                        dt,
                        out float3 anchorNormal))
                {
                    hasContact = true;
                    previousGroundNormal = anchorNormal;
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

                // Stay latched while airborne or on a pipe; hand back to sticky once supported by
                // something that is not a pipe. Test the pre-scroll pose against last-frame colliders.
                bool pipeInReach = IsTouchingPipe(
                    collisionWorld,
                    anchoredBodies,
                    anchorLookup,
                    position,
                    float3.zero,
                    config.ValueRO,
                    pipeFilter,
                    entity,
                    StickyContactSkin);
                if (pipeInReach)
                    hasContact = true;
                pipeMode = hasContact && !pipeInReach ? (byte)0 : (byte)1;

                position -= scrollDelta;
            }
            else
            {
                bool hadContact = motionState.ValueRO.hasPreviousContact != 0;
                float3 searchNormal = hadContact && previousGroundNormal.y >= MinStickyGroundUp
                    ? previousGroundNormal
                    : math.up();

                float3 contactNormal = previousGroundNormal;
                float3 contactPoint = position;
                float signedSeparation = float.MaxValue;
                bool probed = false;

                if (hasPhysicsWorld)
                {
                    probed = TryProbeSurface(
                        collisionWorld,
                        anchoredBodies,
                        anchorLookup,
                        nonPipeFilter,
                        entity,
                        position,
                        searchNormal,
                        config.ValueRO,
                        out contactPoint,
                        out contactNormal,
                        out signedSeparation);
                }

                if (probed)
                    previousGroundNormal = contactNormal;

                hasContact = EvaluateSphereContact(
                    probed,
                    signedSeparation,
                    worldVelocity,
                    contactNormal,
                    gravity,
                    braking,
                    dt);

                if (hasContact)
                {
                    ApplySphereContactConstraint(
                        ref position,
                        ref worldVelocity,
                        contactPoint,
                        contactNormal,
                        config.ValueRO,
                        braking,
                        gravity,
                        scrollVelocity,
                        dt);
                }
                else if (!braking)
                {
                    worldVelocity += gravity * dt;
                }

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

                terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                    worldVelocity,
                    scrollVelocity);

                startPosition = position;
                relativeDisplacement = terrainRelativeVelocity * dt;

                if (hasPhysicsWorld)
                {
                    if (SweepAndSlide(
                            ref position,
                            ref terrainRelativeVelocity,
                            startPosition,
                            relativeDisplacement,
                            config.ValueRO,
                            collisionWorld,
                            anchoredBodies,
                            anchorLookup,
                            nonPipeFilter,
                            entity,
                            out float3 sweepNormal))
                    {
                        hasContact = true;
                        previousGroundNormal = sweepNormal;
                    }

                    if (TryDepenetrateWorld(
                            ref position,
                            ref terrainRelativeVelocity,
                            collisionWorld,
                            anchorLookup,
                            nonPipeFilter,
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
                            nonPipeFilter,
                            config.ValueRO,
                            dt,
                            out float3 anchorNormal))
                    {
                        hasContact = true;
                        previousGroundNormal = anchorNormal;
                    }
                }
                else
                {
                    position = startPosition + relativeDisplacement;
                }

                position -= scrollDelta;
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
            motionState.ValueRW.pipeMode = pipeMode;
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
    /// True when the rider sphere overlaps or this step's sweep hits a Pipe-layer collider,
    /// including scrolling TerrainAnchor meshes whose BelongsTo includes the Pipe layer.
    /// </summary>
    private static bool IsTouchingPipe(
        CollisionWorld collisionWorld,
        NativeList<RigidBody> anchoredBodies,
        ComponentLookup<TerrainAnchorTag> anchorLookup,
        float3 position,
        float3 relativeDisplacement,
        in PlayerFollowObjectGroundConfig config,
        CollisionFilter pipeFilter,
        Entity ignoreEntity,
        float extraReach = 0f)
    {
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        float reach = radius + math.max(0f, extraReach);
        float3 center = position + config.sphereCenter;

        var distanceInput = new PointDistanceInput
        {
            Position = center,
            MaxDistance = reach,
            Filter = pipeFilter
        };
        var distanceCollector = new IgnoreEntityDistanceCollector(reach, ignoreEntity, anchorLookup, skipAnchored: true);
        if (collisionWorld.CalculateDistance(distanceInput, ref distanceCollector)
            && distanceCollector.NumHits > 0)
        {
            return true;
        }

        float distance = math.length(relativeDisplacement);
        if (distance >= MinCastDistance)
        {
            float3 direction = relativeDisplacement / distance;
            var castCollector = new IgnoreEntityCastCollector(1f, ignoreEntity, anchorLookup, skipAnchored: true);
            SphereCastWithAnchors(
                collisionWorld,
                anchoredBodies,
                center,
                radius,
                direction,
                distance,
                pipeFilter,
                ref castCollector);
            if (castCollector.NumHits > 0)
                return true;
        }

        if (!anchoredBodies.IsCreated)
            return false;

        for (int bodyIndex = 0; bodyIndex < anchoredBodies.Length; bodyIndex++)
        {
            RigidBody body = anchoredBodies[bodyIndex];
            if (!body.Collider.IsCreated)
                continue;

            CollisionFilter bodyFilter = body.Collider.Value.GetCollisionFilter();
            if ((bodyFilter.BelongsTo & PipeLayerMask) == 0)
                continue;

            var input = new PointDistanceInput
            {
                Position = center,
                MaxDistance = reach,
                Filter = CollisionFilter.Default
            };
            if (body.CalculateDistance(input, out DistanceHit _))
                return true;
        }

        return false;
    }

    /// <summary>
    /// In contact when the sphere is within <see cref="StickyContactSkin"/> of a hit, or this
    /// step's motion would close the remaining gap. Otherwise the sphere is airborne.
    /// </summary>
    private static bool EvaluateSphereContact(
        bool probed,
        float signedSeparation,
        float3 worldVelocity,
        float3 contactNormal,
        float3 gravity,
        bool braking,
        float dt)
    {
        if (!probed)
            return false;

        if (signedSeparation <= StickyContactSkin)
            return true;

        float3 predictedVelocity = worldVelocity;
        if (!braking)
            predictedVelocity += gravity * dt;
        float approach = -math.dot(predictedVelocity, contactNormal) * dt;
        return approach >= signedSeparation - StickyContactSkin;
    }

    /// <summary>
    /// Constrains the sphere to the hit surface: snap the center to contact + normal * radius,
    /// kill inbound relative normal velocity, then slide under tangent gravity and friction.
    /// </summary>
    private static void ApplySphereContactConstraint(
        ref float3 position,
        ref float3 worldVelocity,
        float3 contactPoint,
        float3 contactNormal,
        in PlayerFollowObjectGroundConfig config,
        bool braking,
        float3 gravity,
        float3 scrollVelocity,
        float dt)
    {
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        float3 center = position + config.sphereCenter;
        float3 desiredCenter = contactPoint + contactNormal * radius;
        float3 correction = desiredCenter - center;
        float correctionLen = math.length(correction);
        if (correctionLen > 1e-6f)
        {
            float3 corrDir = correction / correctionLen;
            float intoSurface = math.dot(correction, contactNormal);
            float applied = correctionLen;
            if (intoSurface > 0f)
            {
                float recoverySpeed = config.maxPenetrationRecoverySpeed;
                if (recoverySpeed <= 0f)
                    recoverySpeed = DefaultMaxPenetrationRecoverySpeed;
                applied = math.min(applied, recoverySpeed * math.max(0f, dt));
            }

            position += corrDir * applied;
        }

        float3 surfaceWorldVelocity = -scrollVelocity;
        float relativeVn = math.dot(worldVelocity - surfaceWorldVelocity, contactNormal);
        if (relativeVn < 0f)
            worldVelocity -= contactNormal * relativeVn;

        if (!braking)
        {
            worldVelocity += RemoveNormalComponent(gravity, contactNormal) * dt;
            ApplyGroundFriction(
                ref worldVelocity,
                scrollVelocity,
                contactNormal,
                config.groundFriction,
                dt);
        }
    }

    /// <summary>
    /// SphereCasts along <paramref name="searchNormal"/> (last contact normal, or world-up)
    /// onto non-Pipe colliders. Closest hit is the supporting surface.
    /// </summary>
    private static bool TryProbeSurface(
        CollisionWorld collisionWorld,
        NativeList<RigidBody> anchoredBodies,
        ComponentLookup<TerrainAnchorTag> anchorLookup,
        CollisionFilter filter,
        Entity ignoreEntity,
        float3 position,
        float3 searchNormal,
        in PlayerFollowObjectGroundConfig config,
        out float3 contactPoint,
        out float3 contactNormal,
        out float signedSeparation)
    {
        contactPoint = position;
        contactNormal = math.up();
        signedSeparation = float.MaxValue;

        float radius = math.max(config.sphereRadius, MinSphereRadius);
        float3 center = position + config.sphereCenter;
        float3 n = math.normalizesafe(searchNormal, math.up());
        float above = math.max(config.rayHeightAbove, radius + 0.1f);
        float below = math.max(config.rayLengthBelow, radius);
        float maxDistance = above + below;
        if (maxDistance < MinCastDistance)
            return false;

        float3 origin = center + n * above;
        float3 direction = -n;

        var collector = new IgnoreEntityCastCollector(1f, ignoreEntity, anchorLookup, skipAnchored: true, minUp: MinStickyGroundUp);
        SphereCastWithAnchors(
            collisionWorld,
            anchoredBodies,
            origin,
            radius,
            direction,
            maxDistance,
            filter,
            ref collector);

        if (collector.NumHits == 0)
            return false;

        ColliderCastHit hit = collector.ClosestHit;
        contactNormal = math.normalizesafe(hit.SurfaceNormal, math.up());
        contactPoint = hit.Position;
        signedSeparation = math.dot(center - contactPoint, contactNormal) - radius;
        return true;
    }

    /// <summary>
    /// If the sphere overlaps a collider in the physics world, push out along the true surface
    /// normal and cancel inbound velocity. Does not snap across a gap. TerrainAnchor bodies are
    /// skipped here; <see cref="TryDepenetrateAnchoredBodies"/> handles them at their current pose.
    /// </summary>
    private static bool TryDepenetrateWorld(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        CollisionWorld collisionWorld,
        ComponentLookup<TerrainAnchorTag> anchorLookup,
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
        var collector = new IgnoreEntityDistanceCollector(radius, ignoreEntity, anchorLookup, skipAnchored: true);
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
    /// Bodies that do not collide with <paramref name="filter"/> are skipped (sticky path excludes Pipe).
    /// </summary>
    private static bool TryDepenetrateAnchoredBodies(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        NativeList<RigidBody> anchoredBodies,
        CollisionFilter filter,
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

        for (int bodyIndex = 0; bodyIndex < anchoredBodies.Length; bodyIndex++)
        {
            RigidBody body = anchoredBodies[bodyIndex];
            if (!body.Collider.IsCreated)
                continue;

            CollisionFilter bodyFilter = body.Collider.Value.GetCollisionFilter();
            if (!CollisionFilter.IsCollisionEnabled(filter, bodyFilter))
                continue;

            var input = new PointDistanceInput
            {
                Position = center,
                MaxDistance = maxDistance,
                Filter = CollisionFilter.Default
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
        float push = math.min(penetration + ObstacleSkin, maxPush);
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
        NativeList<RigidBody> anchoredBodies,
        ComponentLookup<TerrainAnchorTag> anchorLookup,
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
                    anchoredBodies,
                    anchorLookup,
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
                currentPosition += hitNormal * ObstacleSkin;

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

    /// <summary>
    /// SphereCasts against the CollisionWorld (TerrainAnchor hits rejected) and then each anchored
    /// body at its current pose, sharing one collector so the closest hit across both wins.
    /// </summary>
    private static void SphereCastWithAnchors<C>(
        CollisionWorld collisionWorld,
        NativeList<RigidBody> anchoredBodies,
        float3 origin,
        float radius,
        float3 direction,
        float maxDistance,
        CollisionFilter filter,
        ref C collector)
        where C : struct, ICollector<ColliderCastHit>, IAnchorSkippingCollector
    {
        collector.SkipAnchored = true;
        collisionWorld.SphereCastCustom(origin, radius, direction, maxDistance, ref collector, filter);
        collector.SkipAnchored = false;

        if (!anchoredBodies.IsCreated)
            return;

        for (int bodyIndex = 0; bodyIndex < anchoredBodies.Length; bodyIndex++)
        {
            RigidBody body = anchoredBodies[bodyIndex];
            if (!body.Collider.IsCreated)
                continue;

            if (!CollisionFilter.IsCollisionEnabled(filter, body.Collider.Value.GetCollisionFilter()))
                continue;

            body.SphereCastCustom(origin, radius, direction, maxDistance, ref collector, filter);
        }
    }

    private static bool TryCastOpposingSphere(
        CollisionWorld collisionWorld,
        NativeList<RigidBody> anchoredBodies,
        ComponentLookup<TerrainAnchorTag> anchorLookup,
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

        var collector = new OpposingHitCollector(1f, ignoreEntity, direction, anchorLookup, skipAnchored: true);
        SphereCastWithAnchors(
            collisionWorld,
            anchoredBodies,
            center,
            radius,
            direction,
            maxDistance,
            filter,
            ref collector);

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
