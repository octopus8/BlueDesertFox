using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Keeps the howitzer barrel locked to its dome and pitched toward the predicted intercept point.
///
/// The ECS parent-child hierarchy is removed by StaticObjectHierarchyFlattenUtility, so
/// the barrel entity has an independent LocalTransform that is first written by
/// objectPositionUpdateSystem (tile-relative scrolling position). This system then
/// overwrites both Position and Rotation with values derived from the dome entity's
/// up-to-date world transform, effectively re-parenting the barrel to the dome at runtime.
///
/// Pitch is computed in dome-local space:
///   1. The desired aim direction is transformed into the dome's local frame.
///   2. The pitch delta = desired YZ angle − natural YZ angle (baked from model forward axis).
///   3. The delta is clamped and smooth-lerped, then applied as RotateX before localRotation.
///
/// Execution order:
///   objectPositionUpdateSystem  → sets tile-relative positions for all static objects
///   TurretAimingSystem          → rotates dome, writes dome.interceptPoint
///   TurretBarrelSystem          → positions barrel, applies pitch toward intercept (this system)
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(TurretAimingSystem))]
public partial struct TurretBarrelSystem : ISystem
{
    private ComponentLookup<LocalTransform> _domeTransformLookup;
    private ComponentLookup<TurretDome> _domeDataLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TurretBarrelTag>();
        _domeTransformLookup = state.GetComponentLookup<LocalTransform>(true);
        _domeDataLookup = state.GetComponentLookup<TurretDome>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _domeTransformLookup.Update(ref state);
        _domeDataLookup.Update(ref state);

        var job = new TurretBarrelUpdateJob
        {
            domeTransformLookup = _domeTransformLookup,
            domeDataLookup = _domeDataLookup,
            deltaTime = SystemAPI.Time.DeltaTime
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// For each barrel entity:
    ///   1. Look up the dome's current world transform and intercept point.
    ///   2. Compute barrel world position = domePos + rotate(domeRot, localOffset).
    ///   3. Transform desired aim direction to dome-local space.
    ///   4. Compute pitch delta = desiredYZAngle − neutralElevationAngle.
    ///   5. Clamp and smooth-lerp currentPitchAngle.
    ///   6. Apply: barrel.Rotation = dome.Rotation × RotateX(currentPitch) × localRotation.
    /// </summary>
    [BurstCompile]
    private partial struct TurretBarrelUpdateJob : IJobEntity
    {
        [ReadOnly]
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> domeTransformLookup;

        [ReadOnly]
        [NativeDisableParallelForRestriction]
        public ComponentLookup<TurretDome> domeDataLookup;

        [ReadOnly] public float deltaTime;

        private void Execute(ref TurretBarrelTag barrel, ref LocalTransform transform)
        {
            if (!domeTransformLookup.HasComponent(barrel.domeEntity) ||
                !domeDataLookup.HasComponent(barrel.domeEntity))
                return;

            var domeTf = domeTransformLookup[barrel.domeEntity];
            var domeData = domeDataLookup[barrel.domeEntity];

            float3 domePos = domeTf.Position;
            quaternion domeRot = domeTf.Rotation;

            // ---- Position ----
            float3 barrelPos = domePos + math.rotate(domeRot, barrel.localOffset);
            transform.Position = barrelPos;

            // ---- Pitch ----
            float3 intercept = domeData.interceptPoint;
            float3 toIntercept = intercept - barrelPos;
            float interceptDist = math.length(toIntercept);

            float pitchTarget = barrel.currentPitchAngle; // hold current angle if no valid intercept
            if (interceptDist > 0.01f)
            {
                // Transform desired world aim direction into dome-local space.
                float3 worldAimDir = toIntercept / interceptDist;
                float3 localAimDir = math.rotate(math.inverse(domeRot), worldAimDir);

                // Desired angle in dome's local YZ plane, then subtract the model's natural
                // elevation at pitch=0 to get the delta we need to apply.
                float desiredYZAngle = math.atan2(localAimDir.y, localAimDir.z);
                float pitchDelta = desiredYZAngle - barrel.neutralElevationAngle;

                // Clamp to configured limits.
                pitchTarget = math.clamp(pitchDelta, barrel.minPitchAngle, barrel.maxPitchAngle);
            }

            // Smooth-lerp toward target pitch.
            float newPitch;
            if (barrel.pitchSpeed <= 0f)
            {
                newPitch = pitchTarget;
            }
            else
            {
                float diff = pitchTarget - barrel.currentPitchAngle;
                // Wrap to [-PI, PI] for shortest arc.
                float twoPi = 2f * math.PI;
                diff = diff - twoPi * math.floor((diff + math.PI) / twoPi);

                float maxStep = math.radians(barrel.pitchSpeed) * deltaTime;
                newPitch = barrel.currentPitchAngle + math.clamp(diff, -maxStep, maxStep);
            }

            barrel.currentPitchAngle = newPitch;

            // ---- Rotation ----
            // dome.Rotation × RotateX(pitch) × localRotation
            // RotateX(pitch) is applied before localRotation so it rotates
            // around the dome's horizontal X axis, independent of model orientation.
            transform.Rotation = math.mul(domeRot,
                                 math.mul(quaternion.RotateX(newPitch), barrel.localRotation));
        }
    }
}
