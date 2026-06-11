using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Rotates turret dome entities to predictively aim at the player using a ballistic intercept calculation.
///
/// Each frame the system:
///   1. Reads the player's world position from PlayerTransformReference (managed, main thread).
///   2. Uses terrain scroll direction and speed only for predictive lead on the XZ plane (player
///      locomotion is ignored).
///   3. Schedules a Burst parallel job that solves the quadratic intercept equation from the
///      muzzle launch point (dome position + <see cref="TurretLaunchOffset"/> when baked),
///      writes a Y-axis rotation to LocalTransform.Rotation on every TurretDome entity,
///      and stores the 3D world-space intercept point in TurretDome.interceptPoint for
///      TurretBarrelSystem to use for pitch calculation.
///
/// Runs after objectPositionUpdateSystem so turret world positions are already current.
/// Only writes LocalTransform.Rotation; objectPositionUpdateSystem writes Position separately.
/// </summary>
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(objectPositionUpdateSystem))]
[UpdateBefore(typeof(LocalToWorldSystem))]
public partial struct TurretAimingSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TurretDome>();
        state.RequireForUpdate<TerrainScrollVelocity>();
        state.RequireForUpdate<PlayerTransformReference>();
    }

    // OnUpdate is intentionally NOT [BurstCompile] — it must read the managed
    // PlayerTransformReference singleton on the main thread before scheduling the job.
    public void OnUpdate(ref SystemState state)
    {
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        if (playerRef?.playerTransform == null)
            return;

        float3 playerPos = playerRef.playerTransform.position;

        var scrollVel = SystemAPI.GetSingleton<TerrainScrollVelocity>();
        float3 scrollVelocity = scrollVel.direction * scrollVel.speed;

        // Lead from scroll only: ignore player-relative motion (VR locomotion etc.).
        float3 playerVelocity = scrollVelocity;

        float deltaTime = SystemAPI.Time.DeltaTime;

        var domeLaunchOffsets = new NativeHashMap<Entity, float3>(16, Allocator.TempJob);
        foreach (var (barrel, launchOffset) in
            SystemAPI.Query<RefRO<TurretBarrelTag>, RefRO<TurretLaunchOffset>>())
        {
            domeLaunchOffsets[barrel.ValueRO.domeEntity] = launchOffset.ValueRO.domeLocalOffset;
        }

        var job = new TurretAimJob
        {
            playerPos = playerPos,
            playerVelocity = playerVelocity,
            deltaTime = deltaTime,
            domeLaunchOffsets = domeLaunchOffsets.AsReadOnly()
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
        state.Dependency = domeLaunchOffsets.Dispose(state.Dependency);
    }

    /// <summary>
    /// Burst-compiled parallel job. For each TurretDome entity it:
    ///   - Solves the XZ-plane intercept quadratic for the smallest positive flight time.
    ///   - Converts the intercept displacement to a Y-axis quaternion for the dome rotation.
    ///   - Smooth-lerps from the current angle toward the target at dome.rotationSpeed deg/s.
    ///   - Stores the 3D world-space intercept point in dome.interceptPoint for barrel pitch use.
    ///     The intercept Y is always the player's current world Y (no vertical lead needed).
    /// </summary>
    [BurstCompile]
    private partial struct TurretAimJob : IJobEntity
    {
        [ReadOnly] public float3 playerPos;
        [ReadOnly] public float3 playerVelocity;
        [ReadOnly] public float deltaTime;

        [ReadOnly] public NativeHashMap<Entity, float3>.ReadOnly domeLaunchOffsets;

        private void Execute(Entity entity, ref TurretDome dome, ref LocalTransform transform)
        {
            float3 domePos = transform.Position;
            quaternion domeRot = transform.Rotation;

            float3 launchLocal = float3.zero;
            if (domeLaunchOffsets.TryGetValue(entity, out float3 offset))
                launchLocal = offset;

            float3 launchPos = domePos + math.rotate(domeRot, launchLocal);

            // Work entirely in the XZ plane for horizontal (Y-axis) aiming.
            float2 d = new float2(playerPos.x - launchPos.x, playerPos.z - launchPos.z);
            float2 v = new float2(playerVelocity.x, playerVelocity.z);
            float b = dome.bulletSpeed;

            // Solve returns the XZ displacement from launch point to intercept point.
            float2 interceptXZ = SolveIntercept(d, v, b);

            // Store 3D world-space intercept. Y = player's current height (no vertical lead).
            dome.interceptPoint = new float3(
                launchPos.x + interceptXZ.x,
                playerPos.y,
                launchPos.z + interceptXZ.y);

            // Derive Y-axis aim angle from the same intercept vector.
            float2 aimDir = math.normalizesafe(interceptXZ, new float2(0f, 1f));
            float targetAngleRad = math.atan2(aimDir.x, aimDir.y);

            // Smooth-lerp from current angle to target.
            float newAngleRad;
            if (dome.rotationSpeed <= 0f)
            {
                newAngleRad = targetAngleRad;
            }
            else
            {
                float diff = targetAngleRad - dome.currentYAngle;
                // Wrap difference to [-PI, PI] to always take the shortest arc.
                float twoPi = 2f * math.PI;
                diff = diff - twoPi * math.floor((diff + math.PI) / twoPi);

                float maxStep = math.radians(dome.rotationSpeed) * deltaTime;
                newAngleRad = dome.currentYAngle + math.clamp(diff, -maxStep, maxStep);
            }

            dome.currentYAngle = newAngleRad;
            transform.Rotation = quaternion.RotateY(newAngleRad);
        }

        /// <summary>
        /// Solves the 2-D ballistic intercept problem for a target at displacement d moving at velocity v,
        /// with a projectile of speed b. Returns the XZ displacement from launch origin to the intercept point.
        ///
        /// Equation: |d + v*t|² = (b*t)²  →  (|v|²-b²)t² + 2·dot(d,v)·t + |d|² = 0
        ///
        /// Falls back to direct aim (returns d) when no positive solution exists.
        /// </summary>
        private static float2 SolveIntercept(float2 d, float2 v, float bulletSpeed)
        {
            float vv = math.dot(v, v);
            float bb = bulletSpeed * bulletSpeed;
            float a = vv - bb;
            float bCoeff = 2f * math.dot(d, v);
            float c = math.dot(d, d);

            float bestT = float.MaxValue;

            if (math.abs(a) < 1e-4f)
            {
                // Degenerate (bullet speed ≈ player speed): linear equation.
                if (math.abs(bCoeff) > 1e-4f)
                {
                    float t = -c / bCoeff;
                    if (t > 0f)
                        bestT = t;
                }
            }
            else
            {
                float discriminant = bCoeff * bCoeff - 4f * a * c;
                if (discriminant >= 0f)
                {
                    float sqrtDisc = math.sqrt(discriminant);
                    float t1 = (-bCoeff + sqrtDisc) / (2f * a);
                    float t2 = (-bCoeff - sqrtDisc) / (2f * a);

                    if (t1 > 1e-4f) bestT = t1;
                    if (t2 > 1e-4f && t2 < bestT) bestT = t2;
                }
            }

            // Return world-XZ displacement from launch origin to intercept.
            // Falls back to d (direct aim toward current player position) if no solution.
            return bestT < float.MaxValue ? d + v * bestT : d;
        }
    }
}
