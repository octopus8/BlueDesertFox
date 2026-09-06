using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
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
    public float rideFrequency;
    public float rideDampingRatio;
    public float rideHeight;
    public float maxLegExtension;
    public float maxLegCompression;
    public float contactProbeRadius;
    public float groundFriction;
    public float yawRotationSmoothTime;
    public float minYawSpeed;
    public float capsuleRadius;
    public float capsuleHalfCylinder;
    public float3 capsuleCenter;
    public float3 gravity;
    // Appended fields stay at the end so a closed SubScene bake from before they existed still lines
    // up every earlier field (capsule, gravity, friction). Inserting them mid-struct scrambled those on
    // Quest and left the rider stuck on first contact while Editor live-bake looked fine.
    public float maxGroundLiftSpeed;
    public float maxPenetrationRecoverySpeed;

    /// <summary>Leg length past which the surface is out of reach and contact is lost.</summary>
    public float MaxLegLength => rideHeight + math.max(0f, maxLegExtension);

    /// <summary>Leg length at which the suspension bottoms out against its hard stop.</summary>
    public float MinLegLength => math.max(0f, rideHeight - math.max(0f, maxLegCompression));
}

/// <summary>
/// Runtime terrain-relative velocity and suspension state for the player follow object.
/// World velocity is <c>terrainRelativeVelocity - scrollVelocity</c> (see <see cref="TerrainScrollVelocityMath"/>).
/// </summary>
public struct PlayerFollowObjectMotionState : IComponentData
{
    public float3 terrainRelativeVelocity;
    public float smoothedYaw;
    public byte inContact;
    public float legLength;
    public float previousContactHeight;
    public byte hasPreviousContact;
    public float3 previousGroundNormal;
    public float3 contactPoint;

    /// <summary>Diagnostic: clamped rate the supporting surface was seen rising at, in m/s.</summary>
    public float lastSurfaceVerticalRate;

    /// <summary>Diagnostic: change in world-space upward speed produced by the contact step.</summary>
    public float lastContactLiftSpeed;

    /// <summary>Unused; kept so motion-state layout stays stable.</summary>
    public byte lastLiftWasClamped;

    /// <summary>Set while a steep blocking wall is actively scraping the capsule this frame.</summary>
    public byte wallSlideActive;
}

/// <summary>
/// Snowboard-style grounding: a sprung body above a massless board. Downward probes find walkable
/// snow, legs absorb upward surface motion through limited compression, and contact is lost when
/// momentum carries the rider off faster than remaining extension can follow. Steep faces on any
/// ground layer block like cliffs. Integrates in terrain-relative velocity space so scroll motion
/// and sliding do not compete. Burst-compiled to avoid managed GC.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
// Must run before TerrainAnchorSystem / TileScrollPositionSystem. Those systems already apply this
// frame's scroll delta to colliders; casting against that end-of-frame pose while the rider is still
// at last frame's position starts the capsule inside thin scrolling faces and every cast misses.
[UpdateBefore(typeof(TerrainAnchorSystem))]
[UpdateBefore(typeof(TileScrollPositionSystem))]
public partial struct PlayerFollowObjectGroundContactSystem : ISystem
{
    private const float ObstacleSkin = 0.001f;
    // Nudge out of a wall when the steep cast already reports fraction ~0, so the next frame does not
    // start buried and miss. Kept small so it cannot read as a launch.
    private const float BlockingDepenetrationSkin = 0.01f;
    private const float MinCastDistance = 1e-4f;
    private const float ProbeReachMargin = 0.5f;
    private const float MinProbeRadius = 1e-3f;
    private const float MaxPenetrationRecovery = 2f;
    // tan(60 deg): the steepest slope the board is considered able to ride up. Bounds both how far the
    // supporting surface may rise per step and how fast it may climb, which keeps a wall the probe
    // happens to see over from being mistaken for ground.
    private const float MaxClimbTangent = 1.7320508f;
    private const float MinGradientNormalY = 0.1f;
    private const float WalkableSlopeThreshold = 0.5f;
    private const float MinSlideSpeed = 0.01f;
    // Squared ratio (0.1^2): tangential motion below a tenth of the inbound magnitude counts as head-on,
    // where the surface tangent is too ill-conditioned to slide along and a direction must be chosen.
    private const float MinTangentFractionSq = 0.01f;
    private const float DefaultMaxPenetrationRecoverySpeed = 6f;
    // Fraction of horizontal terrain-relative speed kept the frame a blocking wall scrape ends. Without
    // this, clearing the face releases the full wall-tangent speed as a sideways slingshot.
    private const float WallExitSpeedRetain = 0.25f;
    private static readonly float3 DefaultGravity = new float3(0f, -9.81f, 0f);

    /// <summary>Result of a single downward probe within the contact footprint.</summary>
    private struct GroundProbe
    {
        public bool hit;
        public float height;
        public float3 rawNormal;
    }

    /// <summary>
    /// Capsule-cast collector that ignores walkable hits and keeps the closest steep face. A plain
    /// CapsuleCast returns the nearest surface, which near a cliff is often the floor — accepting that
    /// and early-outing left the full step through the wall behind it.
    /// </summary>
    private struct SteepHitCollector : ICollector<ColliderCastHit>
    {
        public bool EarlyOutOnFirstHit => false;
        public float MaxFraction { get; private set; }
        public int NumHits { get; private set; }
        public ColliderCastHit ClosestHit;

        public SteepHitCollector(float maxFraction)
        {
            MaxFraction = maxFraction;
            NumHits = 0;
            ClosestHit = default;
        }

        public bool AddHit(ColliderCastHit hit)
        {
            float3 normal = math.normalizesafe(hit.SurfaceNormal, math.up());
            if (normal.y >= WalkableSlopeThreshold)
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
        int terrainLayer = 0;
        NativeList<RigidBody> anchoredBodies = default;

        if (hasPhysicsWorld)
        {
            state.Dependency.Complete();
            collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld.CollisionWorld;
            terrainLayer = terrainConfig.terrainPhysicsLayer;

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

        foreach (var (config, motionState, localTransform, brakeState) in SystemAPI
                     .Query<RefRO<PlayerFollowObjectGroundConfig>, RefRW<PlayerFollowObjectMotionState>,
                         RefRW<LocalTransform>, RefRW<PlayerFollowObjectBrakeState>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            bool braking = brakeState.ValueRO.active != 0;

            float3 gravity = config.ValueRO.gravity;
            if (math.lengthsq(gravity) < 1e-8f)
                gravity = DefaultGravity;

            BuildCollisionFilters(
                terrainLayer,
                config.ValueRO.rideablePhysicsLayer,
                out CollisionFilter groundFilter,
                out CollisionFilter obstacleFilter);

            float3 position = localTransform.ValueRO.Position;
            float3 terrainRelativeVelocity = motionState.ValueRO.terrainRelativeVelocity;
            float smoothedYaw = motionState.ValueRO.smoothedYaw;
            float3 previousGroundNormal = motionState.ValueRO.previousGroundNormal;
            if (math.lengthsq(previousGroundNormal) < 0.01f)
                previousGroundNormal = math.up();

            bool hadContact = motionState.ValueRO.hasPreviousContact != 0;

            // Support heights are compared in world space between frames, so the budget has to cover
            // every way the world-space surface under the board can move: the board travelling over the
            // terrain, and the terrain slab itself scrolling past and sinking. Measuring only the board's
            // terrain-relative speed reads zero whenever it rides along with the scroll, which collapses
            // the budget to its floor and makes ordinary terrain look like a wall.
            float traverseSpeed = math.max(
                math.length(new float2(terrainRelativeVelocity.x, terrainRelativeVelocity.z)),
                math.length(new float2(scrollVelocity.x, scrollVelocity.z)));
            float surfaceRiseSpeed = traverseSpeed * MaxClimbTangent + math.abs(scrollVelocity.y);

            // How far the supporting surface may rise in one step. Anything higher is a wall for the
            // capsule sweep to block, not ground for the leg to climb — admitting it would let a cliff
            // top seen past its face shove the rider skyward. The leg's own squash range floors it so
            // small steps stay climbable when nothing is moving.
            float maxSurfaceRise = hadContact
                ? config.ValueRO.maxLegCompression + surfaceRiseSpeed * dt
                : MaxPenetrationRecovery;
            float referenceHeight = hadContact
                ? motionState.ValueRO.previousContactHeight
                : position.y + config.ValueRO.bottomOffset;

            // Probes sit out at the footprint radius, where a slope legitimately puts the surface well
            // above the height under the body. Predicting each probe from the plane already being ridden
            // keeps them valid on a ramp while still rejecting a cliff top, which sits above the plane.
            float2 supportGradient = float2.zero;
            if (hadContact && previousGroundNormal.y > MinGradientNormalY)
            {
                supportGradient = new float2(-previousGroundNormal.x, -previousGroundNormal.z)
                    / previousGroundNormal.y;
                float gradientLength = math.length(supportGradient);
                if (gradientLength > MaxClimbTangent)
                    supportGradient *= MaxClimbTangent / gradientLength;
            }

            // How fast the world-space surface under the board can legitimately rise, as a rate rather
            // than a distance: the board's travel over the slope it is already riding, plus the slab's own
            // vertical motion. This clamps the measured surface rate so a probe discontinuity cannot
            // look like the ground rushed upward — measurement hygiene, not a launch cap.
            float surfaceFollowRateLimit = math.length(supportGradient) * traverseSpeed
                + math.abs(scrollVelocity.y);

            float3 contactNormal = previousGroundNormal;
            float legLength = config.ValueRO.rideHeight;
            float supportHeight = position.y;
            bool probed = false;

            if (hasPhysicsWorld)
            {
                probed = TryProbeGround(
                    collisionWorld,
                    groundFilter,
                    position,
                    smoothedYaw,
                    referenceHeight,
                    supportGradient,
                    maxSurfaceRise,
                    config.ValueRO,
                    out supportHeight,
                    out contactNormal);
            }

            if (probed)
            {
                previousGroundNormal = contactNormal;
                legLength = contactNormal.y * (position.y - supportHeight) - config.ValueRO.bottomOffset;
            }

            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);

            float surfaceVerticalRate = 0f;
            if (probed && hadContact)
            {
                surfaceVerticalRate = math.clamp(
                    (supportHeight - motionState.ValueRO.previousContactHeight) / dt,
                    -surfaceFollowRateLimit,
                    surfaceFollowRateLimit);
            }

            float relativeNormalRate = probed
                ? contactNormal.y * (worldVelocity.y - surfaceVerticalRate)
                : 0f;

            bool hasContact = EvaluateContact(
                hadContact,
                probed,
                legLength,
                relativeNormalRate,
                config.ValueRO,
                dt);

            float contactLiftSpeed = 0f;
            float entryUpwardSpeed = worldVelocity.y;

            if (hasContact)
            {
                ApplyGroundContactForces(
                    ref position,
                    ref worldVelocity,
                    ref legLength,
                    contactNormal,
                    surfaceVerticalRate,
                    config.ValueRO,
                    braking,
                    gravity,
                    scrollVelocity,
                    dt);
                contactLiftSpeed = worldVelocity.y - entryUpwardSpeed;
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

            terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);

            // Capture the body-to-surface gap before integrating so the board can be republished from the
            // final position below. Publishing the pre-integration contact point instead left the board
            // trailing the rider by a frame of travel, which read as fore/aft judder as frametime varied.
            float contactOffsetY = supportHeight - position.y;

            // Colliders (tiles + TerrainAnchors) still sit at last frame's scroll pose while we run.
            // World displacement includes -scroll, but those colliders will also move by -scroll this
            // frame — sweeping world motion against them double-counts scroll and tunnels thin faces.
            // Sweep terrain-relative motion first, then apply scroll once.
            float3 startPosition = position;
            float3 scrollDelta = scrollVelocity * dt;
            float3 relativeDisplacement = terrainRelativeVelocity * dt;
            position = startPosition + relativeDisplacement;

            byte wallSlideActive = 0;
            if (hasPhysicsWorld
                && math.lengthsq(relativeDisplacement) > MinCastDistance * MinCastDistance)
            {
                // Ground-layer steep faces (terrain cliffs, pipe walls) and obstacles stay separate
                // sweeps: a capsule cast reports only its first hit, so one merged pass could early out
                // on walkable ground and miss an obstacle standing right behind it. SteepHitCollector
                // already ignores walkable hits within each pass.
                if (ResolveBlockingCollision(
                        ref position,
                        ref terrainRelativeVelocity,
                        startPosition,
                        relativeDisplacement,
                        config.ValueRO,
                        collisionWorld,
                        groundFilter))
                {
                    wallSlideActive = 1;
                }

                float3 traveledDisplacement = position - startPosition;
                if (math.lengthsq(traveledDisplacement) > MinCastDistance * MinCastDistance
                    && ResolveBlockingCollision(
                        ref position,
                        ref terrainRelativeVelocity,
                        startPosition,
                        traveledDisplacement,
                        config.ValueRO,
                        collisionWorld,
                        obstacleFilter))
                {
                    wallSlideActive = 1;
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

            // Leaving a wall scrape releases the full tangent speed that was preserved against the face.
            // Keep a fraction so clearing a cliff does not read as a sideways slingshot.
            if (motionState.ValueRO.wallSlideActive != 0 && wallSlideActive == 0)
            {
                terrainRelativeVelocity.x *= WallExitSpeedRetain;
                terrainRelativeVelocity.z *= WallExitSpeedRetain;
            }

            localTransform.ValueRW.Position = position;
            motionState.ValueRW.terrainRelativeVelocity = terrainRelativeVelocity;
            motionState.ValueRW.inContact = hasContact ? (byte)1 : (byte)0;
            motionState.ValueRW.legLength = legLength;
            motionState.ValueRW.previousContactHeight = supportHeight;
            motionState.ValueRW.hasPreviousContact = hasContact ? (byte)1 : (byte)0;
            motionState.ValueRW.previousGroundNormal = previousGroundNormal;
            motionState.ValueRW.contactPoint = new float3(position.x, position.y + contactOffsetY, position.z);
            motionState.ValueRW.lastSurfaceVerticalRate = surfaceVerticalRate;
            motionState.ValueRW.lastContactLiftSpeed = contactLiftSpeed;
            motionState.ValueRW.lastLiftWasClamped = 0;
            motionState.ValueRW.wallSlideActive = wallSlideActive;

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

    private static void BuildCollisionFilters(
        int terrainLayer,
        int extraGroundLayer,
        out CollisionFilter groundFilter,
        out CollisionFilter obstacleFilter)
    {
        if (terrainLayer < 0 || terrainLayer > 30)
            terrainLayer = 0;
        if (extraGroundLayer < 0 || extraGroundLayer > 30)
            extraGroundLayer = 15;

        uint groundLayerMask = (1u << terrainLayer) | (1u << extraGroundLayer);

        groundFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = groundLayerMask,
            GroupIndex = 0
        };
        // Terrain and obstacles stay separate sweeps even though they share a response: a capsule cast
        // reports only its first hit, so one merged pass could early out on walkable terrain and miss an
        // obstacle standing right behind it.
        obstacleFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = ~groundLayerMask,
            GroupIndex = 0
        };
    }

    /// <summary>
    /// The board stays on snow while the legs can still reach it. Take off when already in contact if
    /// the surface is out of reach, or if momentum will use up remaining extension this step. Land from
    /// air only when the surface is in reach and the body is closing onto it (or at rest on the snow).
    /// </summary>
    private static bool EvaluateContact(
        bool hadContact,
        bool probed,
        float legLength,
        float relativeNormalRate,
        in PlayerFollowObjectGroundConfig config,
        float dt)
    {
        if (!probed)
            return false;

        float maxLegLength = config.MaxLegLength;

        if (hadContact)
        {
            if (legLength > maxLegLength)
                return false;

            if (relativeNormalRate > 0f && legLength + relativeNormalRate * dt > maxLegLength)
                return false;

            return true;
        }

        if (legLength > maxLegLength)
            return false;

        bool closing = relativeNormalRate < 0f;
        // Spawn / standstill: vy is ~0 so this is not "still going up over snow." A small slack
        // covers probe noise around neutral ride height.
        bool restingOnSnow = relativeNormalRate <= 0f
            && legLength <= config.rideHeight + 0.05f;
        return closing || restingOnSnow;
    }

    /// <summary>
    /// Legs absorb upward surface motion through the spring-damper. When fully compressed and still
    /// closing, kill the closing rate along the contact normal so the body follows the slope. Snow
    /// cannot pull: takeoff is decided before this runs.
    /// </summary>
    private static void ApplyGroundContactForces(
        ref float3 position,
        ref float3 worldVelocity,
        ref float legLength,
        float3 contactNormal,
        float surfaceVerticalRate,
        in PlayerFollowObjectGroundConfig config,
        bool braking,
        float3 gravity,
        float3 scrollVelocity,
        float dt)
    {
        float minLegLength = config.MinLegLength;
        bool bottomedOut = legLength < minLegLength;
        if (bottomedOut)
        {
            float recoverySpeed = config.maxPenetrationRecoverySpeed;
            if (recoverySpeed <= 0f)
                recoverySpeed = DefaultMaxPenetrationRecoverySpeed;
            float lift = math.min(minLegLength - legLength, recoverySpeed * dt);
            position += contactNormal * lift;
            legLength += lift;
        }

        float relativeNormalRate = contactNormal.y * (worldVelocity.y - surfaceVerticalRate);

        if (bottomedOut && relativeNormalRate < 0f)
        {
            worldVelocity -= contactNormal * relativeNormalRate;
            relativeNormalRate = 0f;
        }

        float omega = 2f * math.PI * math.max(0f, config.rideFrequency);
        float stiffness = omega * omega;
        float damping = 2f * math.max(0f, config.rideDampingRatio) * omega;
        // Damping a closing rate while the leg is already long fights freefall. Only damp when
        // the leg is at/under neutral, or when it is extending (soft landing from a crest).
        float damperRate = relativeNormalRate;
        if (legLength > config.rideHeight && relativeNormalRate < 0f)
            damperRate = 0f;
        float springAcceleration = stiffness * (config.rideHeight - legLength)
            - damping * damperRate;

        worldVelocity += contactNormal * (springAcceleration * dt);

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
    /// If the capsule overlaps a scrolling TerrainAnchor mesh, push out along the surface normal
    /// and cancel into-wall velocity. Recovers cases CapsuleCast misses from inside a thin face.
    /// Does not re-arm ground contact — steep faces stay walls.
    /// </summary>
    private static void TryDepenetrateAnchoredBodies(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        NativeList<RigidBody> anchoredBodies,
        in PlayerFollowObjectGroundConfig config,
        float dt)
    {
        GetCapsuleEndpoints(position, config, out float3 point1, out float3 point2);
        float3 center = position + config.capsuleCenter;
        float radius = math.max(config.capsuleRadius, MinProbeRadius);
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

            for (int s = 0; s < 3; s++)
            {
                float3 sample = s == 0 ? center : (s == 1 ? point1 : point2);
                var input = new PointDistanceInput
                {
                    Position = sample,
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

                // Walkable overlap is the suspension's job. Only push out of steep / thin faces.
                if (n.y >= WalkableSlopeThreshold)
                    continue;

                if (penetration > deepestPenetration)
                {
                    deepestPenetration = penetration;
                    bestNormal = n;
                    found = true;
                }
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
    /// Probes the supporting surface under the board footprint: a centre column plus fore/aft and
    /// left/right columns at <see cref="PlayerFollowObjectGroundConfig.contactProbeRadius"/>. Opposing
    /// pairs give a fitted plane normal, which is far steadier than a single ray's per-triangle normal.
    /// Each probe is then extrapolated along that plane to the body's XZ and the highest result wins, so
    /// a rigid board bridges narrow crests instead of dropping into every gap between them.
    /// Each column is judged against the plane <paramref name="referenceHeight"/> /
    /// <paramref name="supportGradient"/> describes, and hits more than <paramref name="maxSurfaceRise"/>
    /// above their predicted height are discarded as walls rather than ground. Surfaces steeper than
    /// <see cref="WalkableSlopeThreshold"/> are discarded outright — a cliff face is something to
    /// collide with rather than stand on.
    /// </summary>
    private static bool TryProbeGround(
        CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        float3 position,
        float yaw,
        float referenceHeight,
        float2 supportGradient,
        float maxSurfaceRise,
        in PlayerFollowObjectGroundConfig config,
        out float supportHeight,
        out float3 normal)
    {
        supportHeight = position.y;
        normal = math.up();

        float radius = math.max(0f, config.contactProbeRadius);
        float3 forward = new float3(math.sin(yaw), 0f, math.cos(yaw));
        float3 right = new float3(forward.z, 0f, -forward.x);
        float3 forwardOffset = forward * radius;
        float3 rightOffset = right * radius;

        float ceilingAtBody = referenceHeight + maxSurfaceRise;

        GroundProbe centre = ProbeColumn(
            collisionWorld, groundFilter,
            position, float3.zero, ceilingAtBody, supportGradient, config);

        if (radius < MinProbeRadius)
        {
            if (!centre.hit)
                return false;

            supportHeight = centre.height;
            normal = centre.rawNormal;
            return true;
        }

        GroundProbe fore = ProbeColumn(
            collisionWorld, groundFilter,
            position, forwardOffset, ceilingAtBody, supportGradient, config);
        GroundProbe aft = ProbeColumn(
            collisionWorld, groundFilter,
            position, -forwardOffset, ceilingAtBody, supportGradient, config);
        GroundProbe starboard = ProbeColumn(
            collisionWorld, groundFilter,
            position, rightOffset, ceilingAtBody, supportGradient, config);
        GroundProbe port = ProbeColumn(
            collisionWorld, groundFilter,
            position, -rightOffset, ceilingAtBody, supportGradient, config);

        if (!centre.hit && !fore.hit && !aft.hit && !starboard.hit && !port.hit)
            return false;

        float invSpan = 1f / (2f * radius);
        float2 gradient = float2.zero;
        bool fitted = false;

        if (fore.hit && aft.hit)
        {
            gradient += (fore.height - aft.height) * invSpan * new float2(forward.x, forward.z);
            fitted = true;
        }

        if (starboard.hit && port.hit)
        {
            gradient += (starboard.height - port.height) * invSpan * new float2(right.x, right.z);
            fitted = true;
        }

        if (fitted)
        {
            normal = math.normalizesafe(new float3(-gradient.x, 1f, -gradient.y), math.up());

            // A footprint that spans a cliff base fits a wall-plane from walkable hits at very different
            // heights. Fall back to centre-only support instead of riding that wall.
            if (normal.y < WalkableSlopeThreshold)
            {
                if (!centre.hit)
                    return false;

                normal = centre.rawNormal;
                gradient = float2.zero;
                supportHeight = math.min(centre.height, ceilingAtBody);
                return true;
            }
        }
        else
        {
            // Not enough opposing pairs to fit a plane (e.g. hanging over an edge) — fall back to a
            // single triangle normal and treat the surface as locally flat for the support test.
            GroundProbe fallback = centre.hit ? centre
                : fore.hit ? fore
                : aft.hit ? aft
                : starboard.hit ? starboard
                : port;
            normal = fallback.rawNormal;
            gradient = float2.zero;
        }

        supportHeight = float.MinValue;
        AccumulateSupport(centre, float3.zero, gradient, ref supportHeight);
        AccumulateSupport(fore, forwardOffset, gradient, ref supportHeight);
        AccumulateSupport(aft, -forwardOffset, gradient, ref supportHeight);
        AccumulateSupport(starboard, rightOffset, gradient, ref supportHeight);
        AccumulateSupport(port, -rightOffset, gradient, ref supportHeight);

        // Extrapolating along the fitted plane can overshoot the accepted hits over a crest, so re-apply
        // the ceiling to guarantee the continuity limit holds for the height the suspension actually sees.
        supportHeight = math.min(supportHeight, ceilingAtBody);

        return true;
    }

    /// <summary>
    /// Projects a probe hit along the fitted plane back to the body's XZ and keeps it if it is the
    /// highest support found so far.
    /// </summary>
    private static void AccumulateSupport(
        in GroundProbe probe,
        float3 horizontalOffset,
        float2 gradient,
        ref float supportHeight)
    {
        if (!probe.hit)
            return;

        float heightAtBody = probe.height - (gradient.x * horizontalOffset.x + gradient.y * horizontalOffset.z);
        if (heightAtBody > supportHeight)
            supportHeight = heightAtBody;
    }

    private static GroundProbe ProbeColumn(
        CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        float3 position,
        float3 horizontalOffset,
        float ceilingAtBody,
        float2 supportGradient,
        in PlayerFollowObjectGroundConfig config)
    {
        GroundProbe probe = default;

        float3 origin = position + horizontalOffset;
        var rayInput = new RaycastInput
        {
            Start = origin + math.up() * config.rayHeightAbove,
            End = origin - math.up() * config.rayLengthBelow,
            Filter = groundFilter
        };

        if (!collisionWorld.CastRay(rayInput, out RaycastHit hit))
            return probe;

        // Discard surfaces the leg could never reach so ledges and cliffs read as open air. The margin
        // keeps the leg-reach test in OnUpdate authoritative for the contact decision itself.
        float reach = config.bottomOffset + config.MaxLegLength + ProbeReachMargin;
        if (hit.Position.y < position.y - reach)
            return probe;

        // The rays start well overhead, so anything above the ceiling is a ceiling, an overhang, or the
        // top of a wall seen past its face — none of which the board can be resting on. The ceiling
        // follows the ridden plane out to this column's offset, so a ramp stays ground while a step up
        // out of that plane does not. While in contact it tracks the surface already being ridden; on
        // landing it tracks the body, so penetration the hard stop still needs to undo stays visible.
        float ceiling = ceilingAtBody
            + supportGradient.x * horizontalOffset.x
            + supportGradient.y * horizontalOffset.z;
        if (hit.Position.y > ceiling)
            return probe;

        float3 hitNormal = math.normalizesafe(hit.SurfaceNormal, math.up());
        if (hitNormal.y < WalkableSlopeThreshold)
            return probe;

        probe.hit = true;
        probe.height = hit.Position.y;
        probe.rawNormal = hitNormal;
        return probe;
    }

    /// <summary>
    /// Blocks the body against a steep surface and lets it scrape along the face. Walkable hits are
    /// skipped by <see cref="SteepHitCollector"/> so a floor underfoot cannot authorize a step through
    /// the cliff behind it. The step is truncated at the steep contact, then the remainder is re-swept
    /// along the plumb barrier plane so grinding still progresses without an unswept tunnel into the
    /// face. Head-on hits deflect along the horizontal wall tangent — never up the face.
    /// </summary>
    private static bool ResolveBlockingCollision(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        float3 startPosition,
        float3 displacement,
        in PlayerFollowObjectGroundConfig config,
        CollisionWorld collisionWorld,
        CollisionFilter blockingFilter)
    {
        float distance = math.length(displacement);
        if (distance < MinCastDistance)
            return false;

        float3 direction = displacement / distance;
        GetCapsuleEndpoints(startPosition, config, out float3 point1, out float3 point2);

        if (!TryCastSteepCapsule(
                collisionWorld,
                point1,
                point2,
                config.capsuleRadius,
                direction,
                distance,
                blockingFilter,
                out ColliderCastHit castHit))
        {
            return false;
        }

        float3 blockingNormal = math.normalizesafe(castHit.SurfaceNormal, math.up());

        // Respond as though the face were plumb. A "vertical" cliff never is — its normal leans back a
        // few degrees — and resolving the step against that leaning plane turns speed driven into the wall
        // into speed up the wall, which is the climb-and-launch. Standing the normal up removes the lift
        // term entirely while still cancelling the approach. An overhang or ceiling has no horizontal
        // normal to stand up, so it keeps the true one and blocks the rider's rise as before.
        float3 barrierNormal = math.normalizesafe(new float3(blockingNormal.x, 0f, blockingNormal.z), float3.zero);
        if (math.lengthsq(barrierNormal) < 0.5f)
            barrierNormal = blockingNormal;

        float3 horizontalTangent = GetHorizontalWallTangent(barrierNormal, direction);

        float fraction = math.max(castHit.Fraction - ObstacleSkin, 0f);
        float3 contactPosition = startPosition + direction * (distance * fraction);

        // Already touching or slightly inside: nudge out so the next cast does not start buried and miss.
        if (castHit.Fraction <= ObstacleSkin)
            contactPosition += barrierNormal * BlockingDepenetrationSkin;

        // Carry the unspent travel along the face, but re-sweep it — an unswept remainder was how a
        // leaning or concave cliff ate the capsule in one frame after the first hit.
        float remainder = distance * (1f - fraction);
        float3 slideDirection = RemoveNormalComponent(direction, barrierNormal);
        if (math.lengthsq(slideDirection) < MinTangentFractionSq)
            slideDirection = horizontalTangent;
        slideDirection = math.normalizesafe(slideDirection, float3.zero);

        float3 slideDisplacement = float3.zero;
        if (remainder > MinCastDistance && math.lengthsq(slideDirection) > 0.5f)
        {
            GetCapsuleEndpoints(contactPosition, config, out float3 slidePoint1, out float3 slidePoint2);
            if (TryCastSteepCapsule(
                    collisionWorld,
                    slidePoint1,
                    slidePoint2,
                    config.capsuleRadius,
                    slideDirection,
                    remainder,
                    blockingFilter,
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

        // Cancel only the approach into the face, in terrain-relative space so the world velocity ends up
        // matching the scroll along that normal — a scrolling slab then carries the rider with its face
        // instead of driving through them. Cancelling the outbound half too would undo the separation the
        // suspension and the deflection below provide, which is another way to end up pinned.
        float inboundHorizontalSpeed = math.length(new float2(terrainRelativeVelocity.x, terrainRelativeVelocity.z));
        float approachRate = math.dot(terrainRelativeVelocity, barrierNormal);
        if (approachRate < 0f)
            terrainRelativeVelocity -= barrierNormal * approachRate;

        // A head-on hit projects to almost no tangential speed, which leaves the rider parked against the
        // face while the scroll keeps pressing them into it. Turning the horizontal speed along the wall
        // instead lets them scrape past. Vertical speed is left to the projection and to gravity, so this
        // can never throw the rider up the face.
        float2 slidHorizontal = new float2(terrainRelativeVelocity.x, terrainRelativeVelocity.z);
        if (inboundHorizontalSpeed > MinSlideSpeed
            && math.lengthsq(horizontalTangent) > 0.5f
            && math.lengthsq(slidHorizontal) < inboundHorizontalSpeed * inboundHorizontalSpeed * MinTangentFractionSq)
        {
            terrainRelativeVelocity = new float3(
                horizontalTangent.x * inboundHorizontalSpeed,
                terrainRelativeVelocity.y,
                horizontalTangent.z * inboundHorizontalSpeed);
        }

        return true;
    }

    /// <summary>
    /// Capsule-casts and returns the closest steep hit, ignoring walkable surfaces.
    /// </summary>
    private static bool TryCastSteepCapsule(
        CollisionWorld collisionWorld,
        float3 point1,
        float3 point2,
        float radius,
        float3 direction,
        float maxDistance,
        CollisionFilter filter,
        out ColliderCastHit hit)
    {
        hit = default;
        if (maxDistance < MinCastDistance)
            return false;

        var collector = new SteepHitCollector(1f);
        collisionWorld.CapsuleCastCustom(
            point1,
            point2,
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
    /// The horizontal direction along a wall face that best matches <paramref name="direction"/>. Used to
    /// deflect a head-on hit sideways rather than up the face.
    /// </summary>
    private static float3 GetHorizontalWallTangent(float3 wallNormal, float3 direction)
    {
        float3 tangent = math.normalizesafe(math.cross(math.up(), wallNormal), float3.zero);
        if (math.lengthsq(tangent) < 0.5f)
            return float3.zero;

        return math.dot(tangent, direction) < 0f ? -tangent : tangent;
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
        ref float3 worldVelocity,
        float3 scrollVelocity,
        float3 normal,
        float groundFriction,
        float dt)
    {
        if (groundFriction <= 0f)
            return;

        // Friction resists sliding against the support surface. Terrain moves at -scrollVelocity in world
        // space, so pass the real scroll and damp the terrain-relative tangent.
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
        // deceleration <= 0 means "no brake force" (coast). Gravity/steering are already
        // suppressed while brake is active; do not treat 0 as an instant hard stop.
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
