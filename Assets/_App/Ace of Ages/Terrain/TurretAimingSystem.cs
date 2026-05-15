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
///   2. Derives the player's velocity relative to each turret from TerrainScrollVelocity:
///      since turrets scroll with the terrain, the player effectively approaches at
///      (TerrainScrollVelocity.direction * speed) in the turret's reference frame.
///   3. Schedules a Burst parallel job that solves the quadratic intercept equation and
///      writes a Y-axis rotation to LocalTransform.Rotation on every TurretDome entity.
///
/// Runs after objectPositionUpdateSystem so turret world positions are already current.
/// Only writes LocalTransform.Rotation; objectPositionUpdateSystem writes Position separately.
/// </summary>
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(objectPositionUpdateSystem))]
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
        // Player's effective velocity relative to any turret:
        // Turrets move at -scrollDir*speed with the terrain, so relative velocity = +scrollDir*speed.
        float3 playerVelocity = scrollVel.direction * scrollVel.speed;

        float deltaTime = SystemAPI.Time.DeltaTime;

        var job = new TurretAimJob
        {
            playerPos = playerPos,
            playerVelocity = playerVelocity,
            deltaTime = deltaTime
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Burst-compiled parallel job. For each TurretDome entity it:
    ///   - Solves the XZ-plane intercept quadratic for the smallest positive flight time.
    ///   - Converts the intercept direction to a Y-axis quaternion.
    ///   - Smooth-lerps from the current angle toward the target at dome.rotationSpeed deg/s.
    /// </summary>
    [BurstCompile]
    private partial struct TurretAimJob : IJobEntity
    {
        [ReadOnly] public float3 playerPos;
        [ReadOnly] public float3 playerVelocity;
        [ReadOnly] public float deltaTime;

        private void Execute(ref TurretDome dome, ref LocalTransform transform)
        {
            float3 turretPos = transform.Position;

            // Work entirely in the XZ plane for horizontal (Y-axis) aiming.
            float2 d = new float2(playerPos.x - turretPos.x, playerPos.z - turretPos.z);
            float2 v = new float2(playerVelocity.x, playerVelocity.z);
            float b = dome.bulletSpeed;

            float targetAngleRad = SolveInterceptAngle(d, v, b);

            // Smooth-lerp from current angle to target
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
        /// with a projectile of speed b. Returns the Y-axis angle (radians) the turret should face.
        ///
        /// Equation: |d + v*t|² = (b*t)²  →  (|v|²-b²)t² + 2·dot(d,v)·t + |d|² = 0
        ///
        /// Falls back to direct aim when no positive solution exists (e.g. bullet too slow).
        /// </summary>
        private static float SolveInterceptAngle(float2 d, float2 v, float bulletSpeed)
        {
            float2 aimDir;

            float vv = math.dot(v, v);
            float bb = bulletSpeed * bulletSpeed;
            float a = vv - bb;
            float bCoeff = 2f * math.dot(d, v);
            float c = math.dot(d, d);

            bool solved = false;
            float bestT = float.MaxValue;

            if (math.abs(a) < 1e-4f)
            {
                // Degenerate (bullet speed ≈ player speed): linear equation.
                if (math.abs(bCoeff) > 1e-4f)
                {
                    float t = -c / bCoeff;
                    if (t > 0f)
                    {
                        bestT = t;
                        solved = true;
                    }
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

                    solved = bestT < float.MaxValue;
                }
            }

            if (solved)
            {
                float2 interceptXZ = d + v * bestT;
                aimDir = math.normalizesafe(interceptXZ, new float2(0f, 1f));
            }
            else
            {
                // Fallback: aim directly at the player's current position.
                aimDir = math.normalizesafe(d, new float2(0f, 1f));
            }

            // atan2(x, z) gives the Y-axis angle measured from +Z (forward).
            return math.atan2(aimDir.x, aimDir.y);
        }
    }
}
