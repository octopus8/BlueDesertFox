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
    public float pipeSpeedRetain;
    public float pipeContactSkin;
    public float pipeLeaveLockoutSeconds;
    public byte pipeDebugLogging;
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

    /// <summary>
    /// Seconds left in which PipeFace hits are dropped outright, so the lip cannot re-grab the
    /// rider on the frames right after a launch. Once it expires the separating-hit test in
    /// <see cref="PlayerFollowObjectGroundContactSystem"/> allows a landing back on the pipe.
    /// </summary>
    public float pipeLeaveLockout;
}

/// <summary>
/// Sphere sliding on any mesh: snap the center onto the hit, kill inbound normal velocity,
/// and accelerate along the tangent. Misses and separating gaps become ballistic flight.
/// A displacement SphereCast prevents tunneling; every hit uses the true surface normal.
/// Integrates in terrain-relative velocity space so scroll motion and sliding do not compete.
/// Burst-compiled to avoid managed GC.
///
/// PipeFace surfaces ride differently on three counts. They win the contact probe over nearer
/// terrain, so a transition is entered in contact instead of as a head-on collision. Their
/// contacts redirect velocity along the surface at full magnitude instead of deleting the
/// inbound component, so speed becomes climb rather than a stop. And they snap without the
/// penetration rate limit, which otherwise lets a fast rising face outrun the correction.
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

    /// <summary>
    /// Graze threshold for PipeFace hits. Climbing parallel to a wall gives a near-zero dot with
    /// it, which the default threshold discards, letting the sphere pass through the face.
    /// </summary>
    private const float PipeOpposingDotThreshold = 0f;

    /// <summary>Tangent speed below this fraction of total speed counts as a dead-on hit.</summary>
    private const float DeadOnTangentFraction = 0.01f;

    /// <summary>Normal Y at or above which a pipe face is a ramp and a leave cannot be a launch.</summary>
    private const float PipeLipRampNormalUp = 0.5f;

    /// <summary>Normal Y at or below which a pipe face counts as a full vertical wall.</summary>
    private const float PipeLipWallNormalUp = 0.17f;

    private const float DefaultMaxPenetrationRecoverySpeed = 6f;
    private const float DefaultPipeContactSkin = 0.25f;
    private const float DefaultPipeLeaveLockoutSeconds = 0.15f;
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
        public NativeArray<RigidBody> Bodies;
        public uint PipeLayerMask;
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public ColliderCastHit ClosestHit;
        public bool ClosestIsPipe;

        public OpposingHitCollector(
            float maxFraction,
            Entity ignoreEntity,
            float3 direction,
            bool ignorePipeHits,
            ComponentLookup<PipeFaceTag> pipeLookup,
            NativeArray<RigidBody> bodies,
            uint pipeLayerMask)
        {
            MaxFraction = maxFraction;
            IgnoreEntity = ignoreEntity;
            Direction = direction;
            IgnorePipeHits = ignorePipeHits;
            PipeLookup = pipeLookup;
            Bodies = bodies;
            PipeLayerMask = pipeLayerMask;
            NumHits = 0;
            ClosestHit = default;
            ClosestIsPipe = false;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            bool isPipe = IsPipeBody(hit.Entity, hit.RigidBodyIndex, Bodies, PipeLayerMask, PipeLookup);
            if (IgnorePipeHits && isPipe)
                return false;

            float3 normal = math.normalizesafe(hit.SurfaceNormal, math.up());
            float threshold = isPipe ? PipeOpposingDotThreshold : OpposingDotThreshold;
            if (math.dot(normal, Direction) >= threshold)
                return false;

            MaxFraction = hit.Fraction;
            ClosestHit = hit;
            ClosestIsPipe = isPipe;
            NumHits = 1;
            return true;
        }
    }

    /// <summary>
    /// Supporting surface along the probe, with PipeFace hits given priority.
    ///
    /// Taking the nearest hit glues the rider to the terrain at the foot of a transition, because
    /// flat ground underfoot is closer than the pipe face rising just ahead. The pipe then becomes
    /// an obstacle for the sweep to brake against instead of a surface to ride. So a pipe hit wins
    /// whenever it is no more than <see cref="PipeCaptureMargin"/> further along the cast than the
    /// nearest other surface, which keeps distant pipes below a long downward probe from stealing
    /// contact while still capturing one the rider is arriving on.
    ///
    /// <see cref="IgnoreSeparatingPipe"/> drops pipe faces the sphere is moving away from, so a
    /// wall beside an airborne rider cannot recapture. <see cref="IgnorePipe"/> drops them outright
    /// during the post-launch lockout. Hits are never culled by fraction because the winning
    /// surface is not necessarily the nearest one.
    /// </summary>
    private struct SurfaceProbeCollector : ICollector<ColliderCastHit>
    {
        public Entity IgnoreEntity;
        public bool IgnoreSeparatingPipe;
        public bool IgnorePipe;
        public float3 ApproachVelocity;
        public ComponentLookup<PipeFaceTag> PipeLookup;
        public NativeArray<RigidBody> Bodies;
        public uint PipeLayerMask;
        public float PipeCaptureMargin;
        public float MaxDistance;
        public float3 CastDirection;

        /// <summary>
        /// Cast travel before which a hit sits behind the rider and cannot be supporting them.
        /// The probe starts a long way back along its axis, which is harmless casting down at
        /// terrain but reaches clear across the pipe once the axis tips horizontal on a wall.
        /// </summary>
        public float MinTravel;

        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction => 1f;
        public int NumHits => _resolvedHits;

        public ColliderCastHit ClosestHit;
        public bool ClosestIsPipe;

        private ColliderCastHit _pipeHit;
        private ColliderCastHit _otherHit;
        private byte _hasPipe;
        private byte _hasOther;
        private int _resolvedHits;

        public SurfaceProbeCollector(
            Entity ignoreEntity,
            bool ignoreSeparatingPipe,
            bool ignorePipe,
            float3 approachVelocity,
            ComponentLookup<PipeFaceTag> pipeLookup,
            NativeArray<RigidBody> bodies,
            uint pipeLayerMask,
            float pipeCaptureMargin,
            float maxDistance,
            float3 castDirection,
            float minTravel)
        {
            CastDirection = castDirection;
            MinTravel = minTravel;
            IgnoreEntity = ignoreEntity;
            IgnoreSeparatingPipe = ignoreSeparatingPipe;
            IgnorePipe = ignorePipe;
            ApproachVelocity = approachVelocity;
            PipeLookup = pipeLookup;
            Bodies = bodies;
            PipeLayerMask = pipeLayerMask;
            PipeCaptureMargin = pipeCaptureMargin;
            MaxDistance = maxDistance;
            ClosestHit = default;
            ClosestIsPipe = false;
            _pipeHit = default;
            _otherHit = default;
            _hasPipe = 0;
            _hasOther = 0;
            _resolvedHits = 0;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            if (hit.Entity == IgnoreEntity)
                return false;

            if (hit.Fraction * MaxDistance < MinTravel)
                return false;

            // A support has to face back up the cast. Without this the probe accepts backfaces,
            // and on a vertical wall - where the cast runs horizontally across the pipe - it
            // returns a ceiling metres away as the surface to stand on.
            float3 hitNormal = math.normalizesafe(hit.SurfaceNormal, math.up());
            if (math.dot(hitNormal, CastDirection) >= 0f)
                return false;

            if (IsPipeBody(hit.Entity, hit.RigidBodyIndex, Bodies, PipeLayerMask, PipeLookup))
            {
                if (IgnorePipe)
                    return false;

                if (IgnoreSeparatingPipe && math.dot(ApproachVelocity, hitNormal) >= 0f)
                    return false;

                if (_hasPipe == 0 || hit.Fraction < _pipeHit.Fraction)
                {
                    _pipeHit = hit;
                    _hasPipe = 1;
                }

                return true;
            }

            if (_hasOther == 0 || hit.Fraction < _otherHit.Fraction)
            {
                _otherHit = hit;
                _hasOther = 1;
            }

            return true;
        }

        /// <summary>Picks the winning surface once every hit along the cast has been offered.</summary>
        public void Resolve()
        {
            bool pipeWins = _hasPipe != 0
                && (_hasOther == 0
                    || (_pipeHit.Fraction - _otherHit.Fraction) * MaxDistance <= PipeCaptureMargin);

            if (pipeWins)
            {
                ClosestHit = _pipeHit;
                ClosestIsPipe = true;
                _resolvedHits = 1;
                return;
            }

            if (_hasOther != 0)
            {
                ClosestHit = _otherHit;
                ClosestIsPipe = false;
                _resolvedHits = 1;
                return;
            }

            _resolvedHits = 0;
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
            float pipeLeaveLockout = math.max(0f, motionState.ValueRO.pipeLeaveLockout - dt);
            float3 searchNormal = hadContact ? previousGroundNormal : math.up();

            float3 contactNormal = previousGroundNormal;
            float3 contactPoint = position;
            float signedSeparation = float.MaxValue;
            bool probed = false;
            bool contactIsPipe = false;

            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);

            float traceEntrySpeed = math.length(terrainRelativeVelocity);

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
                    pipeLeaveLockout > 0f,
                    approachVelocity,
                    pipeLookup,
                    out contactPoint,
                    out contactNormal,
                    out signedSeparation,
                    out contactIsPipe);
            }

            // The pipe face dropping out of the probe is the leave, not the probe coming back
            // empty: a downward cast still finds terrain far below from the top of a wall.
            bool pipeLost = wasOnPipe && !contactIsPipe;

            // Climbing at the lip launches; riding along the wall past the open end just keeps
            // its velocity and arcs away, and dropping back to the snow is not a leave at all.
            bool lipLaunch = pipeLost && IsClimbDominant(worldVelocity, previousGroundNormal);
            if (lipLaunch)
            {
                ApplyPipeLeave(ref worldVelocity, previousGroundNormal);
                pipeAirborne = true;
                pipeLeaveLockout = GetPipeLeaveLockout(config.ValueRO);
            }

            if (probed && !lipLaunch)
                previousGroundNormal = contactNormal;

            bool hasContact = !lipLaunch && EvaluateSphereContact(
                probed,
                signedSeparation,
                worldVelocity,
                TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity),
                contactNormal,
                gravity,
                braking,
                contactIsPipe,
                GetPipeContactSkin(config.ValueRO),
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
                    contactIsPipe,
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

            float traceContactSpeed = math.length(
                TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity));

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

            bool sweepHit = false;
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
                        lipLaunch || pipeLeaveLockout > 0f,
                        pipeLookup,
                        out float3 sweepNormal,
                        out bool sweepIsPipe))
                {
                    sweepHit = true;
                    hasContact = true;
                    pipeAirborne = false;

                    // Only let the sweep take over the ride surface when it is at least as
                    // rideable as the probe's. A terrain hit alongside a pipe face would
                    // otherwise hand the contact normal back to the snow mid-climb.
                    if (sweepIsPipe || !contactIsPipe)
                    {
                        previousGroundNormal = sweepNormal;
                        contactIsPipe = sweepIsPipe;
                    }
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
                    pipeLookup,
                    dt);
            }

            if (hasContact)
                pipeAirborne = false;
            else if (lipLaunch)
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
            motionState.ValueRW.pipeLeaveLockout = pipeLeaveLockout;

            if (config.ValueRO.pipeDebugLogging != 0 && (onPipe || wasOnPipe || contactIsPipe || pipeAirborne))
            {
                LogPipeTrace(
                    traceEntrySpeed,
                    traceContactSpeed,
                    math.length(terrainRelativeVelocity),
                    terrainRelativeVelocity,
                    signedSeparation,
                    previousGroundNormal,
                    probed,
                    contactIsPipe,
                    hasContact,
                    sweepHit,
                    pipeLost,
                    lipLaunch,
                    pipeAirborne,
                    pipeLeaveLockout);
            }

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
    /// In contact when the sphere is within the contact skin of a hit, or this step's motion would
    /// close the remaining gap. Otherwise the sphere is airborne. Pipe faces use a wider skin: a
    /// sphere on a concave surface never truly separates, and the narrow terrain skin lets facet
    /// seams and arc curvature flicker contact off mid-climb.
    /// </summary>
    private static bool EvaluateSphereContact(
        bool probed,
        float signedSeparation,
        float3 worldVelocity,
        float3 surfaceRelativeVelocity,
        float3 contactNormal,
        float3 gravity,
        bool braking,
        bool contactIsPipe,
        float pipeContactSkin,
        float dt)
    {
        if (!probed)
            return false;

        float skin = contactIsPipe ? math.max(ContactSkin, pipeContactSkin) : ContactSkin;
        if (signedSeparation <= skin)
        {
            // Being near the face is not the same as riding it. A rider going over the lip stays
            // inside the wide pipe skin for several frames while travelling away at speed, and
            // holding contact there swaps gravity for the surface tangent, so they glide up the
            // wall instead of launching off it.
            //
            // The wide skin is there to stop mesh facets and arc curvature flickering contact off
            // mid-climb, which is a question about geometry, not about motion. So once the rider
            // is actually moving away, fall back to the tight skin to decide they have left.
            float separationRate = math.dot(surfaceRelativeVelocity, contactNormal);
            if (separationRate > 0f && signedSeparation + separationRate * dt > ContactSkin)
                return false;

            return true;
        }

        float3 predictedVelocity = worldVelocity;
        if (!braking)
            predictedVelocity += gravity * dt;
        float approach = -math.dot(predictedVelocity, contactNormal) * dt;
        return approach >= signedSeparation - ContactSkin;
    }

    /// <summary>
    /// Constrains the sphere to the hit surface: snap the center to contact + normal * radius,
    /// resolve inbound relative normal velocity, then slide under tangent gravity and friction.
    ///
    /// Pipe faces differ in three ways. The snap is not rate limited, because a face rising under
    /// a fast rider outruns the recovery speed and the sphere sinks into the mesh. Inbound velocity
    /// is redirected along the surface at full magnitude instead of deleted, so speed becomes
    /// climb. And friction is skipped, because a pipe should return the rider's energy rather than
    /// eat a third of it on the way up.
    /// </summary>
    private static void ApplySphereContactConstraint(
        ref float3 position,
        ref float3 worldVelocity,
        float3 contactPoint,
        float3 contactNormal,
        in PlayerFollowObjectGroundConfig config,
        bool braking,
        bool contactIsPipe,
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
            if (intoSurface > 0f && !contactIsPipe)
            {
                float recoverySpeed = config.maxPenetrationRecoverySpeed;
                if (recoverySpeed <= 0f)
                    recoverySpeed = DefaultMaxPenetrationRecoverySpeed;
                applied = math.min(applied, recoverySpeed * math.max(0f, dt));
            }

            position += corrDir * applied;
        }

        float3 surfaceWorldVelocity = -scrollVelocity;
        float3 relative = worldVelocity - surfaceWorldVelocity;
        float relativeVn = math.dot(relative, contactNormal);
        if (relativeVn < 0f)
        {
            worldVelocity = contactIsPipe
                ? RedirectAlongSurface(relative, contactNormal, GetPipeSpeedRetain(config)) + surfaceWorldVelocity
                : worldVelocity - contactNormal * relativeVn;
        }

        if (!braking)
        {
            worldVelocity += GetTangentComponent(gravity, contactNormal) * dt;
            if (!contactIsPipe)
            {
                ApplyGroundFriction(
                    ref worldVelocity,
                    scrollVelocity,
                    contactNormal,
                    config.groundFriction,
                    dt);
            }
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
        ComponentLookup<PipeFaceTag> pipeLookup,
        float dt)
    {
        float3 center = position + config.sphereCenter;
        float radius = math.max(config.sphereRadius, MinSphereRadius);
        float maxDistance = radius + BlockingDepenetrationSkin;

        float deepestPenetration = 0f;
        float3 bestNormal = float3.zero;
        bool bestIsPipe = false;
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
                bestIsPipe = body.Entity != Entity.Null && pipeLookup.HasComponent(body.Entity);
                found = true;
            }
        }

        if (!found)
            return;

        // Pipe faces are surfaces to ride, not obstacles: push all the way out this frame and
        // redirect rather than brake, matching the contact constraint.
        float push = deepestPenetration + BlockingDepenetrationSkin;
        if (!bestIsPipe)
        {
            float recoverySpeed = config.maxPenetrationRecoverySpeed;
            if (recoverySpeed <= 0f)
                recoverySpeed = DefaultMaxPenetrationRecoverySpeed;
            push = math.min(push, recoverySpeed * math.max(0f, dt));
        }

        position += bestNormal * push;

        float vNormal = math.dot(terrainRelativeVelocity, bestNormal);
        if (vNormal < 0f)
        {
            terrainRelativeVelocity = bestIsPipe
                ? RedirectAlongSurface(terrainRelativeVelocity, bestNormal, GetPipeSpeedRetain(config))
                : terrainRelativeVelocity - bestNormal * vNormal;
        }
    }

    /// <summary>
    /// SphereCasts along <paramref name="searchNormal"/> (last contact normal, or world-up) onto
    /// any collider and returns the supporting surface, preferring a PipeFace over nearer terrain.
    /// Separating PipeFace hits are skipped while airborne after a pipe leave, and all of them are
    /// skipped during the launch lockout, so the wall beside the rider cannot recapture.
    /// </summary>
    private static bool TryProbeSurface(
        CollisionWorld collisionWorld,
        CollisionFilter filter,
        Entity ignoreEntity,
        float3 position,
        float3 searchNormal,
        in PlayerFollowObjectGroundConfig config,
        bool ignoreSeparatingPipe,
        bool ignorePipe,
        float3 approachVelocity,
        ComponentLookup<PipeFaceTag> pipeLookup,
        out float3 contactPoint,
        out float3 contactNormal,
        out float signedSeparation,
        out bool hitIsPipe)
    {
        contactPoint = position;
        contactNormal = math.up();
        signedSeparation = float.MaxValue;
        hitIsPipe = false;

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
            ignoreEntity,
            ignoreSeparatingPipe,
            ignorePipe,
            approachVelocity,
            pipeLookup,
            GetBodies(collisionWorld),
            config.pipeLayerMask,
            radius + GetPipeContactSkin(config),
            maxDistance,
            direction,
            above - (radius + GetPipeContactSkin(config)));
        collisionWorld.SphereCastCustom(
            origin,
            radius,
            direction,
            maxDistance,
            ref collector,
            filter);
        collector.Resolve();

        if (collector.NumHits == 0)
            return false;

        ColliderCastHit hit = collector.ClosestHit;
        hitIsPipe = collector.ClosestIsPipe;
        contactNormal = math.normalizesafe(hit.SurfaceNormal, math.up());
        contactPoint = hit.Position;

        // Measure along the probe axis, not against the hit surface's plane. The plane form
        // reports a surface the sphere centre happens to sit behind as deeply penetrating no
        // matter how far away it is, which on a curved pipe face reads as -20m and snaps the
        // rider across the map. Cast travel past the start offset is the real gap.
        signedSeparation = hit.Fraction * maxDistance - above;
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
                config.pipeLayerMask,
                out ColliderCastHit castHit,
                out hitIsPipe))
        {
            return false;
        }

        hitNormal = math.normalizesafe(castHit.SurfaceNormal, math.up());
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
                    config.pipeLayerMask,
                    out ColliderCastHit slideHit,
                    out _))
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
        {
            // A pipe transition turns travel into climb; it does not brake. Deleting the inbound
            // component here is what bled the rider dry one mesh facet at a time.
            terrainRelativeVelocity = hitIsPipe
                ? RedirectAlongSurface(terrainRelativeVelocity, hitNormal, GetPipeSpeedRetain(config))
                : terrainRelativeVelocity - hitNormal * approachRate;
        }

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
        uint pipeLayerMask,
        out ColliderCastHit hit,
        out bool hitIsPipe)
    {
        hit = default;
        hitIsPipe = false;
        if (maxDistance < MinCastDistance)
            return false;

        var collector = new OpposingHitCollector(
            1f,
            ignoreEntity,
            direction,
            ignorePipeHits,
            pipeLookup,
            GetBodies(collisionWorld),
            pipeLayerMask);
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
        hitIsPipe = collector.ClosestIsPipe;
        return true;
    }

    /// <summary>
    /// True when the hit is a baked PipeFace (tag) or its Unity Physics filter belongs to the pipe
    /// layer. The layer fallback covers colliders whose tag did not bake into the SubScene.
    /// </summary>
    private static bool IsPipeBody(
        Entity entity,
        int rigidBodyIndex,
        in NativeArray<RigidBody> bodies,
        uint pipeLayerMask,
        in ComponentLookup<PipeFaceTag> pipeLookup)
    {
        if (entity != Entity.Null && pipeLookup.HasComponent(entity))
            return true;

        if (pipeLayerMask == 0u || !bodies.IsCreated || rigidBodyIndex < 0 || rigidBodyIndex >= bodies.Length)
            return false;

        BlobAssetReference<Collider> collider = bodies[rigidBodyIndex].Collider;
        if (!collider.IsCreated)
            return false;

        return (collider.Value.GetCollisionFilter().BelongsTo & pipeLayerMask) != 0u;
    }

    private static NativeArray<RigidBody> GetBodies(CollisionWorld collisionWorld)
    {
        return collisionWorld.NumBodies > 0 ? collisionWorld.Bodies : default;
    }

    private static float GetPipeSpeedRetain(in PlayerFollowObjectGroundConfig config)
    {
        return config.pipeSpeedRetain <= 0f ? 1f : math.min(config.pipeSpeedRetain, 1f);
    }

    private static float GetPipeContactSkin(in PlayerFollowObjectGroundConfig config)
    {
        return config.pipeContactSkin <= 0f ? DefaultPipeContactSkin : config.pipeContactSkin;
    }

    private static float GetPipeLeaveLockout(in PlayerFollowObjectGroundConfig config)
    {
        return config.pipeLeaveLockoutSeconds <= 0f
            ? DefaultPipeLeaveLockoutSeconds
            : config.pipeLeaveLockoutSeconds;
    }

    /// <summary>
    /// One line per frame while the rider is on or just off a pipe. The three speeds bracket the
    /// contact constraint and the sweep, so a run shows which stage spends the rider's momentum,
    /// and the surface field shows whether the pipe is winning the probe at all.
    /// </summary>
    private static void LogPipeTrace(
        float entrySpeed,
        float contactSpeed,
        float exitSpeed,
        float3 exitVelocity,
        float separation,
        float3 normal,
        bool probed,
        bool contactIsPipe,
        bool hasContact,
        bool sweepHit,
        bool pipeLost,
        bool lipLaunch,
        bool pipeAirborne,
        float lockout)
    {
        FixedString32Bytes arrow = " -> ";
        var line = new FixedString512Bytes();

        FixedString32Bytes prefix = "[Pipe] v ";
        line.Append(prefix);
        AppendRounded(ref line, entrySpeed);
        line.Append(arrow);
        AppendRounded(ref line, contactSpeed);
        line.Append(arrow);
        AppendRounded(ref line, exitSpeed);

        FixedString32Bytes velLabel = " vel ";
        line.Append(velLabel);
        AppendRounded(ref line, exitVelocity.x);
        FixedString32Bytes velComma = ",";
        line.Append(velComma);
        AppendRounded(ref line, exitVelocity.y);
        line.Append(velComma);
        AppendRounded(ref line, exitVelocity.z);

        FixedString32Bytes surfaceLabel = " | surface ";
        line.Append(surfaceLabel);
        if (!probed)
        {
            FixedString32Bytes none = "none";
            line.Append(none);
        }
        else if (contactIsPipe)
        {
            FixedString32Bytes pipe = "pipe";
            line.Append(pipe);
        }
        else
        {
            FixedString32Bytes terrain = "terrain";
            line.Append(terrain);
        }

        FixedString32Bytes sepLabel = " sep ";
        line.Append(sepLabel);
        AppendRounded(ref line, separation);

        FixedString32Bytes normalLabel = " n ";
        line.Append(normalLabel);
        AppendRounded(ref line, normal.x);
        FixedString32Bytes comma = ",";
        line.Append(comma);
        AppendRounded(ref line, normal.y);
        line.Append(comma);
        AppendRounded(ref line, normal.z);

        FixedString32Bytes flagsLabel = " |";
        line.Append(flagsLabel);
        if (hasContact)
        {
            FixedString32Bytes flag = " contact";
            line.Append(flag);
        }

        if (sweepHit)
        {
            FixedString32Bytes flag = " sweep";
            line.Append(flag);
        }

        if (pipeLost)
        {
            FixedString32Bytes flag = " lost";
            line.Append(flag);
        }

        if (lipLaunch)
        {
            FixedString32Bytes flag = " LIP";
            line.Append(flag);
        }

        if (pipeAirborne)
        {
            FixedString32Bytes flag = " air";
            line.Append(flag);
        }

        if (lockout > 0f)
        {
            FixedString32Bytes flag = " lock ";
            line.Append(flag);
            AppendRounded(ref line, lockout);
        }

        UnityEngine.Debug.Log(line);
    }

    private static void AppendRounded(ref FixedString512Bytes line, float value)
    {
        line.Append(math.round(value * 100f) / 100f);
    }

    /// <summary>
    /// Keeps the speed and changes only the direction. A transition converts travel into climb; it
    /// does not brake. A dead-on hit has no tangent to follow, so on a steep face it is sent up the
    /// wall, which is what turns a high-speed perpendicular entry into a launch instead of a wall
    /// strike. Dead-on against a ramp is a rider dropping onto the floor, not a wall strike, so
    /// that case just sheds the inbound component the way terrain does.
    /// </summary>
    private static float3 RedirectAlongSurface(float3 relativeVelocity, float3 normal, float retain)
    {
        float speed = math.length(relativeVelocity);
        if (speed < MinSlideSpeed)
            return relativeVelocity;

        float3 tangent = RemoveNormalComponent(relativeVelocity, normal);
        if (math.lengthsq(tangent) < speed * speed * DeadOnTangentFraction * DeadOnTangentFraction
            && PipeLipSteepness(normal) > 0f)
        {
            tangent = RemoveNormalComponent(math.up(), normal);
        }

        float3 direction = math.normalizesafe(tangent, float3.zero);
        if (math.lengthsq(direction) < 0.5f)
            return RemoveNormalComponent(relativeVelocity, math.normalizesafe(normal, math.up()));

        return direction * (speed * retain);
    }

    /// <summary>
    /// How lip-like the face is, from 0 on a ramp to 1 on a vertical wall. Losing the face
    /// low on the transition is a rider running out of speed, not a launch, and the two have
    /// to be told apart: the launch rewrite below is only harmless where the wall is already
    /// steep enough that the tangent points nearly straight up on its own.
    /// </summary>
    private static float PipeLipSteepness(float3 pipeNormal)
    {
        float3 n = math.normalizesafe(pipeNormal, math.up());
        return math.saturate(
            (PipeLipRampNormalUp - math.abs(n.y)) / (PipeLipRampNormalUp - PipeLipWallNormalUp));
    }

    /// <summary>
    /// True when the rider is heading up the wall rather than along it. Separates a lip launch,
    /// which verticalizes, from riding out the open end, which keeps its velocity untouched.
    /// </summary>
    private static bool IsClimbDominant(float3 worldVelocity, float3 pipeNormal)
    {
        float3 n = math.normalizesafe(pipeNormal, math.up());
        float3 wallUp = math.normalizesafe(RemoveNormalComponent(math.up(), n), float3.zero);
        if (math.lengthsq(wallUp) < 0.5f)
            return false;

        if (PipeLipSteepness(n) <= 0f)
            return false;

        float3 pipeAxis = math.normalizesafe(math.cross(n, wallUp), float3.zero);
        float climb = math.dot(worldVelocity, wallUp);
        float along = math.dot(worldVelocity, pipeAxis);
        return climb > 0f && climb >= math.abs(along);
    }

    /// <summary>
    /// Rewrites the climb component onto world-up as the rider leaves the lip. On a full quarter
    /// arc the wall is already vertical there, so this is a safety net for pipes whose face stops
    /// short of vertical rather than the mechanism that produces the launch. It is faded out by
    /// <see cref="PipeLipSteepness"/> because rotating a ramp's whole up-slope speed onto world-up
    /// throws the rider straight up and strips all their travel.
    /// </summary>
    private static void ApplyPipeLeave(ref float3 worldVelocity, float3 pipeNormal)
    {
        float3 n = math.normalizesafe(pipeNormal, math.up());
        float3 wallUp = math.normalizesafe(RemoveNormalComponent(math.up(), n), float3.zero);
        if (math.lengthsq(wallUp) < 0.5f)
            return;

        float climb = math.dot(worldVelocity, wallUp);
        if (climb <= 0f)
            return;

        float turned = climb * PipeLipSteepness(n);
        worldVelocity = worldVelocity - wallUp * turned + math.up() * turned;
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
