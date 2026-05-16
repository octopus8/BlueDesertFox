using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Rotates turret dome entities to aim directly at the player's current position.
///
/// Each frame the system:
///   1. Reads the player's world position from PlayerTransformReference (managed, main thread).
///   2. Schedules a Burst parallel job that computes the XZ bearing to the player,
///      writes a Y-axis rotation to LocalTransform.Rotation on every TurretDome entity,
///      and stores the player's 3D world-space position in TurretDome.interceptPoint for
///      TurretBarrelSystem to use for pitch calculation.
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
        float deltaTime = SystemAPI.Time.DeltaTime;

        var job = new TurretAimJob
        {
            playerPos = playerPos,
            deltaTime = deltaTime
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Burst-compiled parallel job. For each TurretDome entity it:
    ///   - Computes the XZ bearing from the dome to the player.
    ///   - Converts the bearing to a Y-axis quaternion for the dome rotation.
    ///   - Smooth-lerps from the current angle toward the target at dome.rotationSpeed deg/s.
    ///   - Stores the player's 3D world-space position in dome.interceptPoint for barrel pitch use.
    /// </summary>
    [BurstCompile]
    private partial struct TurretAimJob : IJobEntity
    {
        [ReadOnly] public float3 playerPos;
        [ReadOnly] public float deltaTime;

        private void Execute(ref TurretDome dome, ref LocalTransform transform)
        {
            float3 turretPos = transform.Position;

            // Aim directly at the player's current position.
            dome.interceptPoint = playerPos;

            // Derive Y-axis aim angle from the XZ direction to the player.
            float2 d = new float2(playerPos.x - turretPos.x, playerPos.z - turretPos.z);
            float2 aimDir = math.normalizesafe(d, new float2(0f, 1f));
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
    }
}
