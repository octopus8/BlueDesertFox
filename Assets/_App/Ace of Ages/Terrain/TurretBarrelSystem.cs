using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Keeps the howitzer barrel locked to its dome's current world transform each frame.
///
/// The ECS parent-child hierarchy is removed by StaticObjectHierarchyFlattenUtility, so
/// the barrel entity has an independent LocalTransform that is first written by
/// objectPositionUpdateSystem (tile-relative scrolling position). This system then
/// overwrites both Position and Rotation with values derived from the dome entity's
/// up-to-date world transform, effectively re-parenting the barrel to the dome at runtime.
///
/// Execution order:
///   objectPositionUpdateSystem  → sets tile-relative positions for all static objects
///   TurretAimingSystem          → rotates dome to predictive aim angle
///   TurretBarrelSystem          → re-derives barrel world transform from dome (this system)
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(TurretAimingSystem))]
public partial struct TurretBarrelSystem : ISystem
{
    private ComponentLookup<LocalTransform> _domeTransformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TurretBarrelTag>();
        _domeTransformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        _domeTransformLookup.Update(ref state);

        var job = new TurretBarrelUpdateJob
        {
            domeTransformLookup = _domeTransformLookup
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// For each barrel entity: look up the dome's current world transform and compute
    ///   world position  = domePos + rotate(domeRot, barrelLocalOffset)
    ///   world rotation  = domeRot * barrelLocalRotation
    /// This mirrors Unity's normal parent-child transform hierarchy in a Burst-safe way.
    /// </summary>
    [BurstCompile]
    private partial struct TurretBarrelUpdateJob : IJobEntity
    {
        [ReadOnly]
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> domeTransformLookup;

        private void Execute(in TurretBarrelTag barrel, ref LocalTransform transform)
        {
            if (!domeTransformLookup.HasComponent(barrel.domeEntity))
                return;

            var domeTf = domeTransformLookup[barrel.domeEntity];

            transform.Position = domeTf.Position + math.rotate(domeTf.Rotation, barrel.localOffset);
            transform.Rotation = math.mul(domeTf.Rotation, barrel.localRotation);
        }
    }
}
