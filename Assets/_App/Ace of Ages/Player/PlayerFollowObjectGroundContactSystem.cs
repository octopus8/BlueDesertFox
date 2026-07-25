using Unity.Burst;
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

    /// <summary>Diagnostic: upward velocity the contact step tried to add this frame, before clamping.</summary>
    public float lastContactLiftSpeed;

    /// <summary>Diagnostic: set when <see cref="lastContactLiftSpeed"/> hit the configured lift cap.</summary>
    public byte lastLiftWasClamped;

    /// <summary>Set while a steep blocking wall is actively scraping the capsule this frame.</summary>
    public byte wallSlideActive;

    /// <summary>Diagnostic: current frame was on Rideable (written each frame; not used to gate physics).</summary>
    public byte previousOnRideable;
}

/// <summary>
/// Drives the Player Follow Object entity along terrain and rideable surfaces as a sprung body on a
/// travel-limited suspension. A footprint of downward probes finds the supporting surface, a soft
/// spring-damper tuned by ride frequency absorbs bumps, and contact is lost the moment the surface
/// drops beyond the leg's reach — which is what launches the player off ledges. Forward capsule sweeps
/// then resolve walls by layer: a steep Rideable surface carries the body along and up its face, while
/// steep Terrain and other obstacles block it. Integrates in terrain-relative velocity space so scroll
/// motion and ramp slide do not compete. Burst-compiled to avoid managed GC.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
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
    private const float DefaultMaxGroundLiftSpeed = 5f;
    private const float DefaultMaxPenetrationRecoverySpeed = 6f;
    // Closing rate (m/s) past which a meaningfully extended leg is freefall onto distant ground, not a
    // bump. Only the closing sign is used: downhill tessellation makes supportHeight step down so the
    // separating sign false-triggered every few metres and popped the board.
    private const float ExtensionAirborneSpeed = 1f;
    // How far past neutral the leg must be before that freefall check can fire.
    private const float MinAirborneSlack = 0.2f;
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
        public bool isRideable;
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
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<TerrainScrollVelocity>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        if (dt <= 0f)
            return;

        float3 scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>().WorldVelocity;

        bool hasPhysicsWorld = SystemAPI.TryGetSingleton(out TerrainTileConfig terrainConfig)
            && terrainConfig.enablePhysicsColliders
            && SystemAPI.HasSingleton<PhysicsWorldSingleton>();

        CollisionWorld collisionWorld = default;
        int terrainLayer = 0;
        uint terrainLayerMask = 0u;

        if (hasPhysicsWorld)
        {
            state.Dependency.Complete();
            collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld.CollisionWorld;
            terrainLayer = terrainConfig.terrainPhysicsLayer;
            terrainLayerMask = 1u << terrainLayer;
        }

        foreach (var (config, motionState, localTransform) in SystemAPI
                     .Query<RefRO<PlayerFollowObjectGroundConfig>, RefRW<PlayerFollowObjectMotionState>, RefRW<LocalTransform>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            float3 gravity = config.ValueRO.gravity;
            if (math.lengthsq(gravity) < 1e-8f)
                gravity = DefaultGravity;

            int rideableLayer = config.ValueRO.rideablePhysicsLayer;
            if (rideableLayer < 0 || rideableLayer > 30)
                rideableLayer = 15;
            uint rideableLayerMask = 1u << rideableLayer;

            BuildCollisionFilters(
                config.ValueRO,
                terrainLayer,
                out CollisionFilter groundFilter,
                out CollisionFilter rideableSweepFilter,
                out CollisionFilter terrainWallFilter,
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
            // vertical motion. Deriving this from maxSurfaceRise instead divided that budget's positional
            // step-up allowance by dt, which read as tens of metres per second of surface motion even at a
            // standstill and let the damper and the hard stop turn a probe discontinuity into a launch.
            float surfaceFollowRateLimit = math.length(supportGradient) * traverseSpeed
                + math.abs(scrollVelocity.y);

            bool hasContact = false;
            bool onRideable = false;
            float3 contactNormal = previousGroundNormal;
            float legLength = config.ValueRO.rideHeight;
            float supportHeight = position.y;

            if (hasPhysicsWorld
                && TryProbeGround(
                    collisionWorld,
                    groundFilter,
                    terrainLayerMask,
                    rideableLayerMask,
                    position,
                    smoothedYaw,
                    referenceHeight,
                    supportGradient,
                    maxSurfaceRise,
                    config.ValueRO,
                    out supportHeight,
                    out float3 probedNormal,
                    out onRideable))
            {
                contactNormal = probedNormal;

                // Stay attached to a steep rideable wall when the probe still sees rideable support
                // (halfpipe floor under you). Do not force this on terrain after leaving a pipe — that
                // re-armed pipe hard-stop on the mountain and launched the rider uphill.
                if (onRideable
                    && contactNormal.y >= WalkableSlopeThreshold
                    && previousGroundNormal.y < WalkableSlopeThreshold)
                {
                    contactNormal = previousGroundNormal;
                    previousGroundNormal = contactNormal;
                }
                else
                {
                    previousGroundNormal = probedNormal;
                }

                // Probes are vertical columns, so the leg runs from the body straight down to the surface
                // beneath it, measured along the contact normal.
                legLength = contactNormal.y * (position.y - supportHeight) - config.ValueRO.bottomOffset;
                hasContact = legLength <= config.ValueRO.MaxLegLength;
            }

            // Quarterpipes need the full climbable rise rate even when the previous frame was flat
            // mountain — otherwise the rising face is treated as a discontinuity and the climb dies.
            // Current-frame rideable only: previousOnRideable would keep this armed on the first
            // terrain frame after exiting the pipe.
            if (onRideable)
            {
                surfaceFollowRateLimit = MaxClimbTangent * traverseSpeed
                    + math.abs(scrollVelocity.y);
            }

            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);

            float surfaceVerticalRate = 0f;
            if (hasContact && hadContact)
            {
                // Bounded by the rate the ridden surface can actually climb, so anything faster is read
                // as a measurement discontinuity — the footprint swinging onto a new face, say — and is
                // never handed to the damper or the hard stop as a launch impulse.
                surfaceVerticalRate = math.clamp(
                    (supportHeight - motionState.ValueRO.previousContactHeight) / dt,
                    -surfaceFollowRateLimit,
                    surfaceFollowRateLimit);
            }

            // Freefall onto ground still inside MaxLegLength but well past neutral: drop contact so the
            // damper cannot crawl through the slack. Do not treat separating rates as airborne — on a
            // downhill the support height steps with the mesh, surfaceVerticalRate goes largely negative,
            // and abs(rate) flickered contact every few metres (board pop). Ledge drops still lose
            // contact when the leg hits MaxLegLength.
            if (hasContact && legLength > config.ValueRO.rideHeight + MinAirborneSlack)
            {
                float extensionRate = contactNormal.y * (worldVelocity.y - surfaceVerticalRate);
                if (extensionRate < -ExtensionAirborneSpeed)
                    hasContact = false;
            }

            float contactLiftSpeed = 0f;
            byte liftWasClamped = 0;

            if (hasContact)
            {
                // Air time must come from losing contact with retained velocity, never from the ground
                // injecting lift. Capture the upward speed on entry so any increase produced below can be
                // capped; existing upward speed from a ledge launch passes through untouched.
                float entryUpwardSpeed = worldVelocity.y;

                // Correct any penetration left over from the previous step before applying forces, so the
                // hard stop doubles as the ground-interpenetration guard. The correction is rate limited
                // so a deep recovery plays out over several frames instead of teleporting the rider.
                float minLegLength = config.ValueRO.MinLegLength;
                bool bottomedOut = legLength < minLegLength;
                if (bottomedOut)
                {
                    // Non-positive means a stale closed SubScene bake from before the field existed (reads
                    // as zero). Fall back so Quest does not freeze the rider inside the first contact.
                    float recoverySpeed = config.ValueRO.maxPenetrationRecoverySpeed;
                    if (recoverySpeed <= 0f)
                        recoverySpeed = DefaultMaxPenetrationRecoverySpeed;
                    float lift = math.min(minLegLength - legLength, recoverySpeed * dt);
                    position += contactNormal * lift;
                    legLength += lift;
                }

                // Closing rate between body and surface along the contact normal. The horizontal terms
                // of the two velocities cancel because the contact target tracks the body's XZ, leaving
                // the vertical difference projected onto the normal. A body gliding along a constant
                // slope therefore reads zero, so the damper follows the ground instead of fighting it.
                float relativeNormalRate = contactNormal.y * (worldVelocity.y - surfaceVerticalRate);

                if (bottomedOut && relativeNormalRate < 0f)
                {
                    if (onRideable)
                    {
                        // Quarterpipe / halfpipe: convert inbound speed up the face. Terrain keeps the
                        // vertical-only match below so cliffs cannot launch the rider.
                        worldVelocity -= contactNormal * relativeNormalRate;
                        relativeNormalRate = 0f;
                    }
                    else if (worldVelocity.y < surfaceVerticalRate)
                    {
                        worldVelocity.y = surfaceVerticalRate;
                        relativeNormalRate = contactNormal.y * (worldVelocity.y - surfaceVerticalRate);
                    }
                }

                float omega = 2f * math.PI * math.max(0f, config.ValueRO.rideFrequency);
                float stiffness = omega * omega;
                float damping = 2f * math.max(0f, config.ValueRO.rideDampingRatio) * omega;
                // Damping a closing rate while the leg is already long fights freefall. Only damp when
                // the leg is at/under neutral, or when it is extending (soft landing from a crest).
                float damperRate = relativeNormalRate;
                if (legLength > config.ValueRO.rideHeight && relativeNormalRate < 0f)
                    damperRate = 0f;
                float springAcceleration = stiffness * (config.ValueRO.rideHeight - legLength)
                    - damping * damperRate;

                worldVelocity += contactNormal * (springAcceleration * dt);

                // The leg carries the normal component of weight, so only the tangent drives sliding.
                worldVelocity += GetTangentComponent(gravity, contactNormal) * dt;

                ApplyGroundFriction(ref worldVelocity, scrollVelocity, contactNormal, config.ValueRO.groundFriction, dt);

                // Climb budget while in contact. Rideable uses total speed so converting H→V up a pipe
                // cannot starve the clamp mid-transition; terrain keeps horizontal × slope tan so cliffs
                // cannot stack vertical launch. Current-frame rideable only — previousOnRideable would
                // allow full-speed vertical on the first mountain frame after leaving the pipe.
                float maxLift = config.ValueRO.maxGroundLiftSpeed;
                if (maxLift <= 0f)
                    maxLift = DefaultMaxGroundLiftSpeed;
                float maxClimbY;
                if (onRideable)
                {
                    maxClimbY = math.max(maxLift, math.length(worldVelocity));
                }
                else
                {
                    float ny = math.max(contactNormal.y, MinGradientNormalY);
                    float slopeTan = math.sqrt(math.max(0f, 1f - contactNormal.y * contactNormal.y)) / ny;
                    float horizontalSpeed = math.length(new float2(worldVelocity.x, worldVelocity.z));
                    maxClimbY = math.max(maxLift, horizontalSpeed * slopeTan);
                }

                if (worldVelocity.y > maxClimbY)
                {
                    worldVelocity.y = maxClimbY;
                    liftWasClamped = 1;
                }

                contactLiftSpeed = worldVelocity.y - entryUpwardSpeed;
            }
            else
            {
                worldVelocity += gravity * dt;
            }

            terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);

            // Capture the body-to-surface gap before integrating so the board can be republished from the
            // final position below. Publishing the pre-integration contact point instead left the board
            // trailing the rider by a frame of travel, which read as fore/aft judder as frametime varied.
            float contactOffsetY = supportHeight - position.y;

            float3 startPosition = position;
            float3 displacement = worldVelocity * dt;
            position = startPosition + displacement;

            byte wallSlideActive = 0;
            if (hasPhysicsWorld && math.lengthsq(displacement) > MinCastDistance * MinCastDistance)
            {
                if (ResolveRideableCollision(
                        ref position,
                        ref terrainRelativeVelocity,
                        startPosition,
                        displacement,
                        config.ValueRO,
                        collisionWorld,
                        rideableSweepFilter,
                        out float3 steepNormal))
                {
                    previousGroundNormal = steepNormal;
                    onRideable = true;
                }

                // Each blocking sweep runs on the distance actually travelled so far, so whichever surface
                // is nearest ends up truncating the step.
                float3 traveledDisplacement = position - startPosition;
                if (math.lengthsq(traveledDisplacement) > MinCastDistance * MinCastDistance
                    && ResolveBlockingCollision(
                        ref position,
                        ref terrainRelativeVelocity,
                        startPosition,
                        traveledDisplacement,
                        config.ValueRO,
                        collisionWorld,
                        terrainWallFilter))
                {
                    wallSlideActive = 1;
                }

                traveledDisplacement = position - startPosition;
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
            motionState.ValueRW.lastLiftWasClamped = liftWasClamped;
            motionState.ValueRW.wallSlideActive = wallSlideActive;
            motionState.ValueRW.previousOnRideable = onRideable ? (byte)1 : (byte)0;

            UpdateSmoothedYaw(
                ref smoothedYaw,
                TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(terrainRelativeVelocity, scrollVelocity),
                config.ValueRO.minYawSpeed,
                config.ValueRO.yawRotationSmoothTime,
                dt);
            motionState.ValueRW.smoothedYaw = smoothedYaw;
            localTransform.ValueRW.Rotation = quaternion.RotateY(smoothedYaw);
        }
    }

    private static void BuildCollisionFilters(
        in PlayerFollowObjectGroundConfig config,
        int terrainLayer,
        out CollisionFilter groundFilter,
        out CollisionFilter rideableSweepFilter,
        out CollisionFilter terrainWallFilter,
        out CollisionFilter obstacleFilter)
    {
        int rideableLayer = config.rideablePhysicsLayer;
        if (rideableLayer < 0 || rideableLayer > 30)
            rideableLayer = 15;

        uint groundLayerMask = (1u << terrainLayer) | (1u << rideableLayer);

        groundFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = groundLayerMask,
            GroupIndex = 0
        };
        // Steep Rideable walls (e.g. a halfpipe) are surfaces to be carried up and along, so they get the
        // sliding response. Steep Terrain is a cliff to be stopped by, so it gets the blocking response
        // alongside obstacles. Sharing one sweep gave cliffs the halfpipe redirect and launched the rider.
        rideableSweepFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = 1u << rideableLayer,
            GroupIndex = 0
        };
        terrainWallFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = 1u << terrainLayer,
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
    /// Probes the supporting surface under the board footprint: a centre column plus fore/aft and
    /// left/right columns at <see cref="PlayerFollowObjectGroundConfig.contactProbeRadius"/>. Opposing
    /// pairs give a fitted plane normal, which is far steadier than a single ray's per-triangle normal.
    /// Each probe is then extrapolated along that plane to the body's XZ and the highest result wins, so
    /// a rigid board bridges narrow crests instead of dropping into every gap between them.
    /// Each column is judged against the plane <paramref name="referenceHeight"/> /
    /// <paramref name="supportGradient"/> describes, and hits more than <paramref name="maxSurfaceRise"/>
    /// above their predicted height are discarded as walls rather than ground. Terrain steeper than
    /// <see cref="WalkableSlopeThreshold"/> is discarded outright, since a cliff face is something to
    /// collide with rather than stand on; steep Rideable surfaces are kept so halfpipes still work.
    /// </summary>
    private static bool TryProbeGround(
        CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        uint terrainLayerMask,
        uint rideableLayerMask,
        float3 position,
        float yaw,
        float referenceHeight,
        float2 supportGradient,
        float maxSurfaceRise,
        in PlayerFollowObjectGroundConfig config,
        out float supportHeight,
        out float3 normal,
        out bool onRideable)
    {
        supportHeight = position.y;
        normal = math.up();
        onRideable = false;

        float radius = math.max(0f, config.contactProbeRadius);
        float3 forward = new float3(math.sin(yaw), 0f, math.cos(yaw));
        float3 right = new float3(forward.z, 0f, -forward.x);
        float3 forwardOffset = forward * radius;
        float3 rightOffset = right * radius;

        float ceilingAtBody = referenceHeight + maxSurfaceRise;

        GroundProbe centre = ProbeColumn(
            collisionWorld, groundFilter, terrainLayerMask, rideableLayerMask,
            position, float3.zero, ceilingAtBody, supportGradient, config);

        if (radius < MinProbeRadius)
        {
            if (!centre.hit)
                return false;

            supportHeight = centre.height;
            normal = centre.rawNormal;
            onRideable = centre.isRideable;
            return true;
        }

        GroundProbe fore = ProbeColumn(
            collisionWorld, groundFilter, terrainLayerMask, rideableLayerMask,
            position, forwardOffset, ceilingAtBody, supportGradient, config);
        GroundProbe aft = ProbeColumn(
            collisionWorld, groundFilter, terrainLayerMask, rideableLayerMask,
            position, -forwardOffset, ceilingAtBody, supportGradient, config);
        GroundProbe starboard = ProbeColumn(
            collisionWorld, groundFilter, terrainLayerMask, rideableLayerMask,
            position, rightOffset, ceilingAtBody, supportGradient, config);
        GroundProbe port = ProbeColumn(
            collisionWorld, groundFilter, terrainLayerMask, rideableLayerMask,
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
            // heights. Fall back to centre-only support instead of riding that wall. Steep rideable
            // centres are valid (ProbeColumn already dropped steep terrain).
            if (normal.y < WalkableSlopeThreshold)
            {
                if (!centre.hit)
                    return false;

                normal = centre.rawNormal;
                gradient = float2.zero;
                supportHeight = math.min(centre.height, ceilingAtBody);
                onRideable = centre.isRideable;
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
            onRideable = fallback.isRideable;
            gradient = float2.zero;
        }

        supportHeight = float.MinValue;
        AccumulateSupport(centre, float3.zero, gradient, ref supportHeight, ref onRideable);
        AccumulateSupport(fore, forwardOffset, gradient, ref supportHeight, ref onRideable);
        AccumulateSupport(aft, -forwardOffset, gradient, ref supportHeight, ref onRideable);
        AccumulateSupport(starboard, rightOffset, gradient, ref supportHeight, ref onRideable);
        AccumulateSupport(port, -rightOffset, gradient, ref supportHeight, ref onRideable);

        // Extrapolating along the fitted plane can overshoot the accepted hits over a crest, so re-apply
        // the ceiling to guarantee the continuity limit holds for the height the suspension actually sees.
        supportHeight = math.min(supportHeight, ceilingAtBody);

        return true;
    }

    /// <summary>
    /// Projects a probe hit along the fitted plane back to the body's XZ and keeps it if it is the
    /// highest support found so far. The winning probe's rideable flag becomes <paramref name="onRideable"/>.
    /// </summary>
    private static void AccumulateSupport(
        in GroundProbe probe,
        float3 horizontalOffset,
        float2 gradient,
        ref float supportHeight,
        ref bool onRideable)
    {
        if (!probe.hit)
            return;

        float heightAtBody = probe.height - (gradient.x * horizontalOffset.x + gradient.y * horizontalOffset.z);
        if (heightAtBody > supportHeight)
        {
            supportHeight = heightAtBody;
            onRideable = probe.isRideable;
        }
        else if (math.abs(heightAtBody - supportHeight) <= 1e-4f)
        {
            onRideable |= probe.isRideable;
        }
    }

    private static GroundProbe ProbeColumn(
        CollisionWorld collisionWorld,
        CollisionFilter groundFilter,
        uint terrainLayerMask,
        uint rideableLayerMask,
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
        bool isRideable = IsLayerSurface(collisionWorld, hit, rideableLayerMask);

        // A terrain cliff face is something to hit, not to stand on. Accepting it made the suspension
        // adopt a near-horizontal contact normal and then latch there via the steep-wall rule below,
        // gluing the rider to the wall. Steep Rideable surfaces are still accepted so halfpipes work.
        if (hitNormal.y < WalkableSlopeThreshold
            && !isRideable
            && IsLayerSurface(collisionWorld, hit, terrainLayerMask))
        {
            return probe;
        }

        probe.hit = true;
        probe.height = hit.Position.y;
        probe.rawNormal = hitNormal;
        probe.isRideable = isRideable;
        return probe;
    }

    /// <summary>Tests whether a query hit belongs to the given physics layer mask.</summary>
    private static bool IsLayerSurface(in CollisionWorld collisionWorld, in RaycastHit hit, uint layerMask)
    {
        if (layerMask == 0u || hit.RigidBodyIndex < 0 || hit.RigidBodyIndex >= collisionWorld.NumBodies)
            return false;

        RigidBody body = collisionWorld.Bodies[hit.RigidBodyIndex];
        if (!body.Collider.IsCreated)
            return false;

        return (body.Collider.Value.GetCollisionFilter(hit.ColliderKey).BelongsTo & layerMask) != 0u;
    }

    /// <summary>
    /// Carries the body along a steep Rideable wall, redirecting its speed up the face when the approach
    /// is near head-on. This is the halfpipe response; steep Terrain is handled by
    /// <see cref="ResolveBlockingCollision"/> instead.
    /// </summary>
    private static bool ResolveRideableCollision(
        ref float3 position,
        ref float3 terrainRelativeVelocity,
        float3 startPosition,
        float3 displacement,
        in PlayerFollowObjectGroundConfig config,
        CollisionWorld collisionWorld,
        CollisionFilter rideableFilter,
        out float3 steepNormal)
    {
        steepNormal = math.up();
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
                rideableFilter))
        {
            return false;
        }

        float3 wallNormal = math.normalizesafe(castHit.SurfaceNormal, math.up());
        if (wallNormal.y >= WalkableSlopeThreshold)
            return false;

        if (math.dot(direction, wallNormal) >= -ObstacleSkin)
            return false;

        steepNormal = wallNormal;

        float3 slideDisplacement = ComputeSteepWallSlideDisplacement(displacement, wallNormal, direction);
        position = startPosition + slideDisplacement;

        // Sliding is resolved against the wall surface, so the redirect happens in terrain-relative space.
        float inboundSpeed = math.length(terrainRelativeVelocity);
        float vNormal = math.dot(terrainRelativeVelocity, wallNormal);

        if (vNormal < 0f)
            terrainRelativeVelocity -= wallNormal * vNormal;

        float minTangentSpeedSq = inboundSpeed * inboundSpeed * MinTangentFractionSq;
        if (math.lengthsq(terrainRelativeVelocity) < minTangentSpeedSq && inboundSpeed > MinSlideSpeed)
        {
            float3 slideDir = math.normalizesafe(
                RemoveNormalComponent(displacement, wallNormal),
                GetTangentComponent(math.up(), wallNormal));
            terrainRelativeVelocity = slideDir * inboundSpeed;
        }

        return true;
    }

    private static float3 ComputeSteepWallSlideDisplacement(
        float3 displacement,
        float3 wallNormal,
        float3 direction)
    {
        float slideDistance = math.length(displacement);
        float3 slideDisplacement = RemoveNormalComponent(displacement, wallNormal);
        float minTangentDistSq = slideDistance * slideDistance * MinTangentFractionSq;

        if (math.lengthsq(slideDisplacement) < minTangentDistSq)
        {
            float3 uphillTangent = GetTangentComponent(math.up(), wallNormal);
            slideDisplacement = math.normalizesafe(uphillTangent, direction) * slideDistance;
        }
        else
        {
            slideDisplacement = math.normalizesafe(slideDisplacement, direction) * slideDistance;
        }

        return slideDisplacement;
    }

    /// <summary>
    /// Blocks the body against a steep surface and lets it scrape along the face. Walkable hits are
    /// skipped by <see cref="SteepHitCollector"/> so a floor underfoot cannot authorize a step through
    /// the cliff behind it. The step is truncated at the steep contact, then the remainder is re-swept
    /// along the plumb barrier plane so grinding still progresses without an unswept tunnel into the
    /// face. Head-on hits deflect along the horizontal wall tangent — never up the face — which is what
    /// separates a cliff collision from the halfpipe response in <see cref="ResolveRideableCollision"/>.
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
        // can never throw the rider up the face the way the rideable redirect does.
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

        // Friction resists sliding against the terrain surface, which moves at -scrollVelocity in world
        // space, so it must damp the terrain-relative tangent rather than the world tangent.
        float3 surfaceRelative = TerrainScrollVelocityMath.TerrainRelativeFromWorld(worldVelocity, scrollVelocity);
        float3 tangent = RemoveNormalComponent(surfaceRelative, normal);
        float damping = math.max(0f, 1f - groundFriction * dt);
        surfaceRelative = normal * math.dot(surfaceRelative, normal) + tangent * damping;
        worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(surfaceRelative, scrollVelocity);
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
