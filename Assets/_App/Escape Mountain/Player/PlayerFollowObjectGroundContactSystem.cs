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
    public uint pipeLayerMask;
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
    public byte onPipe;
    public byte pipeAirborne;
}

/// <summary>
/// Sphere sliding on any mesh: snap the center onto the hit, kill inbound normal velocity,
/// and accelerate along the tangent. Misses and separating gaps become ballistic flight.
/// A displacement SphereCast prevents tunneling; every hit uses the true surface normal.
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
    private const float ContactSkin = 0.05f;
    private const float MinSlideSpeed = 0.01f;
    private const float OpposingDotThreshold = -0.01f;
    private const float DefaultMaxPenetrationRecoverySpeed = 6f;
    private static readonly float3 DefaultGravity = new float3(0f, -9.81f, 0f);

    /// <summary>
    /// Closest SphereCast hit that the sphere is moving into. Ignores the follow-object entity
    /// and surfaces the motion is leaving or grazing, so the floor already being ridden does not
    /// eat the step. Optionally ignores PipeFace hits so a lip/end leave cannot re-arm on the wall.
    /// </summary>
    private struct OpposingHitCollector : ICollector<ColliderCastHit>
    {
        public Entity IgnoreEntity;
        public float3 Direction;
        public bool IgnorePipeHits;
        public ComponentLookup<PipeFaceTag> PipeLookup;
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public ColliderCastHit ClosestHit;

        public OpposingHitCollector(
            float maxFraction,
            Entity ignoreEntity,
            float3 direction,
            bool ignorePipeHits,
            ComponentLookup<PipeFaceTag> pipeLookup)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            Direction = direction;
            IgnorePipeHits = ignorePipeHits;
            PipeLookup = pipeLookup;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            if (IgnorePipeHits && hit.Entity != Entity.Null && PipeLookup.HasComponent(hit.Entity))
                return false;

            float3 normal = math.normalizesafe(hit.SurfaceNormal, math.up());
            if (math.dot(normal, Direction) >= OpposingDotThreshold)
                return false;

            MaxFraction = hit.Fraction;
            ClosestHit = hit;
            NumHits = 1;
            return true;
        }
    }

    /// <summary>
    /// Closest supporting surface along the probe. When <see cref="IgnoreSeparatingPipe"/> is set,
    /// PipeFace hits the sphere is not moving into are skipped so a downward cast cannot recapture
    /// the wall after a lip launch.
    /// </summary>
    private struct SurfaceProbeCollector : ICollector<ColliderCastHit>
    {
        public Entity IgnoreEntity;
        public bool IgnoreSeparatingPipe;
        public float3 ApproachVelocity;
        public ComponentLookup<PipeFaceTag> PipeLookup;
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public ColliderCastHit ClosestHit;

        public SurfaceProbeCollector(
            float maxFraction,
            Entity ignoreEntity,
            bool ignoreSeparatingPipe,
            float3 approachVelocity,
            ComponentLookup<PipeFaceTag> pipeLookup)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            IgnoreSeparatingPipe = ignoreSeparatingPipe;
            ApproachVelocity = approachVelocity;
            PipeLookup = pipeLookup;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            if (IgnoreSeparatingPipe
                && hit.Entity != Entity.Null
                && PipeLookup.HasComponent(hit.Entity))
            {
                float3 normal = math.normalizesafe(hit.SurfaceNormal, math.up());
                if (math.dot(ApproachVelocity, normal) >= 0f)
                    return false;
            }

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

        ComponentLookup<PipeFaceTag> pipeLookup = SystemAPI.GetComponentLookup<PipeFaceTag>(true);

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

            bool hadContact = motionState.ValueRO.hasPreviousContact != 0;
            bool wasOnPipe = motionState.ValueRO.onPipe != 0;
            bool pipeAirborne = motionState.ValueRO.pipeAirborne != 0;
            float3 searchNormal = hadContact ? previousGroundNormal : math.up();

            float3 contactNormal = previousGroundNormal;
            float3 contactPoint = position;
            float signedSeparation = float.MaxValue;
            bool probed = false;
            Entity probeHitEntity = Entity.Null;
            int probeRigidBodyIndex = -1;

            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);

            if (hasPhysicsWorld)
            {
                float3 approachVelocity = worldVelocity;
                if (!braking)
                    approachVelocity += gravity * dt;

                probed = TryProbeSurface(
                    collisionWorld,
                    allFilter,
                    entity,
                    position,
                    searchNormal,
                    config.ValueRO,
                    pipeAirborne,
                    approachVelocity,
                    pipeLookup,
                    out contactPoint,
                    out contactNormal,
                    out signedSeparation,
                    out probeHitEntity,
                    out probeRigidBodyIndex);
            }

            bool contactIsPipe = probed && IsPipeHit(
                probeHitEntity,
                probeRigidBodyIndex,
                collisionWorld,
                config.ValueRO.pipeLayerMask,
                pipeLookup);

            bool pipeLeave = false;
            if (wasOnPipe && !probed)
            {
                ApplyPipeLeave(ref worldVelocity, previousGroundNormal);
                pipeLeave = true;
                pipeAirborne = true;
            }

            if (probed && !pipeLeave)
                previousGroundNormal = contactNormal;

            bool hasContact = !pipeLeave && EvaluateSphereContact(
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
                pipeAirborne = false;
            }
            else if (!braking)
            {
                worldVelocity += gravity * dt;
                if (wasOnPipe)
                    pipeAirborne = true;
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

            terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);

            // Colliders (tiles + TerrainAnchors) still sit at last frame's scroll pose while we run.
            // World displacement includes -scroll, but those colliders will also move by -scroll this
            // frame — sweeping world motion against them double-counts scroll and tunnels thin faces.
            // Sweep terrain-relative motion first, then apply scroll once.
            float3 startPosition = position;
            float3 scrollDelta = scrollVelocity * dt;
            float3 relativeDisplacement = terrainRelativeVelocity * dt;
            position = startPosition + relativeDisplacement;

            if (hasPhysicsWorld
                && math.lengthsq(relativeDisplacement) > MinCastDistance * MinCastDistance)
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
                        pipeLeave,
                        pipeLookup,
                        out float3 sweepNormal,
                        out bool sweepIsPipe))
                {
                    hasContact = true;
                    previousGroundNormal = sweepNormal;
                    contactIsPipe = sweepIsPipe;
                    pipeLeave = false;
                    pipeAirborne = false;
                }
            }

            position -= scrollDelta;

            if (hasPhysicsWorld && anchoredBodies.IsCreated && anchoredBodies.Length > 0)
            {
                TryDepenetrateAnchoredBodies(
                    ref position,
                    ref terrainRelativeVelocity,
                    anchoredBodies,
                    config.ValueRO,
                    dt);
            }

            if (hasContact)
                pipeAirborne = false;
            else if (pipeLeave)
                contactIsPipe = false;

            bool onPipe = hasContact && contactIsPipe;

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
            motionState.ValueRW.onPipe = onPipe ? (byte)1 : (byte)0;
            motionState.ValueRW.pipeAirborne = pipeAirborne ? (byte)1 : (byte)0;

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
    /// In contact when the sphere is within <see cref="ContactSkin"/> of a hit, or this
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

        if (signedSeparation <= ContactSkin)
            return true;

        float3 predictedVelocity = worldVelocity;
        if (!braking)
            predictedVelocity += gravity * dt;
        float approach = -math.dot(predictedVelocity, contactNormal) * dt;
        return approach >= signedSeparation - ContactSkin;
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
            worldVelocity += GetTangentComponent(gravity, contactNormal) * dt;
            ApplyGroundFriction(
                ref worldVelocity,
                scrollVelocity,
                contactNormal,
                config.groundFriction,
                dt);
        }
    }

    /// <summary>
    /// If the sphere overlaps a scrolling TerrainAnchor mesh, push out along the true surface
    /// normal and cancel inbound velocity. Recovers cases SphereCast misses from inside a thin face.
    /// </summary>
    private static void TryDepenetrateAnchoredBodies(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        NativeList<RigidBody> anchoredBodies,
        in PlayerFollowObjectGroundConfig config,
        float dt)
    {
        float3 center = position + config.sphereCenter;
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        float maxDistance = radius + BlockingDepenetrationSkin;

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
            return;

        float recoverySpeed = config.maxPenetrationRecoverySpeed;
        if (recoverySpeed <= 0f)
            recoverySpeed = DefaultMaxPenetrationRecoverySpeed;
        float maxPush = recoverySpeed * math.max(0f, dt);
        float push = math.min(deepestPenetration + BlockingDepenetrationSkin, maxPush);
        position += bestNormal * push;

        float vNormal = math.dot(terrainRelativeVelocity, bestNormal);
        if (vNormal < 0f)
            terrainRelativeVelocity -= bestNormal * vNormal;
    }

    /// <summary>
    /// SphereCasts along <paramref name="searchNormal"/> (last contact normal, or world-up)
    /// onto any collider. Closest hit is the supporting surface. Separating PipeFace hits are
    /// skipped while airborne after a pipe leave so the wall beside the rider cannot recapture.
    /// </summary>
    private static bool TryProbeSurface(
        CollisionWorld collisionWorld,
        CollisionFilter filter,
        Entity ignoreEntity,
        float3 position,
        float3 searchNormal,
        in PlayerFollowObjectGroundConfig config,
        bool ignoreSeparatingPipe,
        float3 approachVelocity,
        ComponentLookup<PipeFaceTag> pipeLookup,
        out float3 contactPoint,
        out float3 contactNormal,
        out float signedSeparation,
        out Entity hitEntity,
        out int rigidBodyIndex)
    {
        contactPoint = position;
        contactNormal = math.up();
        signedSeparation = float.MaxValue;
        hitEntity = Entity.Null;
        rigidBodyIndex = -1;

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

        var collector = new SurfaceProbeCollector(
            1f,
            ignoreEntity,
            ignoreSeparatingPipe,
            approachVelocity,
            pipeLookup);
        collisionWorld.SphereCastCustom(
            origin,
            radius,
            direction,
            maxDistance,
            ref collector,
            filter);

        if (collector.NumHits == 0)
            return false;

        ColliderCastHit hit = collector.ClosestHit;
        hitEntity = hit.Entity;
        rigidBodyIndex = hit.RigidBodyIndex;
        contactNormal = math.normalizesafe(hit.SurfaceNormal, math.up());
        contactPoint = hit.Position;
        signedSeparation = math.dot(center - contactPoint, contactNormal) - radius;
        return true;
    }

    /// <summary>
    /// SphereCasts along the step and stops at the first surface the sphere is moving into.
    /// Remaining travel slides along that surface's true tangent. Returns true if a hit constrained
    /// the step. PipeFace hits are ignored on a lip/end leave so the wall cannot re-arm contact.
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
        bool ignorePipeHits,
        ComponentLookup<PipeFaceTag> pipeLookup,
        out float3 hitNormal,
        out bool hitIsPipe)
    {
        hitNormal = math.up();
        hitIsPipe = false;
        float distance = math.length(displacement);
        if (distance < MinCastDistance)
            return false;

        float3 direction = displacement / distance;
        if (!TryCastOpposingSphere(
                collisionWorld,
                startPosition + config.sphereCenter,
                math.max(config.sphereRadius, MinSphereRadius),
                direction,
                distance,
                filter,
                ignoreEntity,
                ignorePipeHits,
                pipeLookup,
                out ColliderCastHit castHit))
        {
            return false;
        }

        hitNormal = math.normalizesafe(castHit.SurfaceNormal, math.up());
        hitIsPipe = IsPipeHit(
            castHit.Entity,
            castHit.RigidBodyIndex,
            collisionWorld,
            config.pipeLayerMask,
            pipeLookup);
        float fraction = math.max(castHit.Fraction - ObstacleSkin, 0f);
        float3 contactPosition = startPosition + direction * (distance * fraction);

        if (castHit.Fraction <= ObstacleSkin)
            contactPosition += hitNormal * BlockingDepenetrationSkin;

        float remainder = distance * (1f - fraction);
        float3 slideDirection = RemoveNormalComponent(direction, hitNormal);
        slideDirection = math.normalizesafe(slideDirection, float3.zero);

        float3 slideDisplacement = float3.zero;
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        if (remainder > MinCastDistance && math.lengthsq(slideDirection) > 0.5f)
        {
            float3 slideCenter = contactPosition + config.sphereCenter;
            if (TryCastOpposingSphere(
                    collisionWorld,
                    slideCenter,
                    radius,
                    slideDirection,
                    remainder,
                    filter,
                    ignoreEntity,
                    ignorePipeHits,
                    pipeLookup,
                    out ColliderCastHit slideHit))
            {
                float slideFraction = math.max(slideHit.Fraction - ObstacleSkin, 0f);
                slideDisplacement = slideDirection * (remainder * slideFraction);
            }
            else
            {
                slideDisplacement = slideDirection * remainder;
            }
        }

        position = contactPosition + slideDisplacement;

        float approachRate = math.dot(terrainRelativeVelocity, hitNormal);
        if (approachRate < 0f)
            terrainRelativeVelocity -= hitNormal * approachRate;

        return true;
    }

    private static bool TryCastOpposingSphere(
        CollisionWorld collisionWorld,
        float3 center,
        float radius,
        float3 direction,
        float maxDistance,
        CollisionFilter filter,
        Entity ignoreEntity,
        bool ignorePipeHits,
        ComponentLookup<PipeFaceTag> pipeLookup,
        out ColliderCastHit hit)
    {
        hit = default;
        if (maxDistance < MinCastDistance)
            return false;

        var collector = new OpposingHitCollector(
            1f,
            ignoreEntity,
            direction,
            ignorePipeHits,
            pipeLookup);
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

    /// <summary>
    /// True when the hit is a baked PipeFace (tag) or its Unity Physics filter belongs to the pipe layer.
    /// </summary>
    private static bool IsPipeHit(
        Entity entity,
        int rigidBodyIndex,
        CollisionWorld collisionWorld,
        uint pipeLayerMask,
        ComponentLookup<PipeFaceTag> pipeLookup)
    {
        if (entity != Entity.Null && pipeLookup.HasComponent(entity))
            return true;

        if (pipeLayerMask == 0u || rigidBodyIndex < 0 || rigidBodyIndex >= collisionWorld.NumBodies)
            return false;

        BlobAssetReference<Collider> collider = collisionWorld.Bodies[rigidBodyIndex].Collider;
        if (!collider.IsCreated)
            return false;

        return (collider.Value.GetCollisionFilter().BelongsTo & pipeLayerMask) != 0u;
    }

    /// <summary>
    /// When the PipeFace mesh ends: if climb speed dominates along-pipe speed, rewrite that
    /// climb onto world-up (lip launch). Otherwise keep velocity (ride out the open end).
    /// </summary>
    private static void ApplyPipeLeave(ref float3 worldVelocity, float3 pipeNormal)
    {
        float3 n = math.normalizesafe(pipeNormal, math.up());
        float3 wallUp = math.normalizesafe(RemoveNormalComponent(math.up(), n), float3.zero);
        if (math.lengthsq(wallUp) < 0.5f)
            return;

        float3 pipeAxis = math.normalizesafe(math.cross(n, wallUp), float3.zero);
        float climb = math.dot(worldVelocity, wallUp);
        float along = math.dot(worldVelocity, pipeAxis);
        if (climb > 0f && math.abs(climb) >= math.abs(along))
            worldVelocity = worldVelocity - wallUp * climb + math.up() * climb;
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
