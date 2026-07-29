using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that updates tree positions when their owning terrain tiles move.
/// Uses parallel IJobEntity to efficiently process thousands of trees across multiple CPU cores.
/// This approach avoids the performance overhead of transform hierarchy while maintaining visual cohesion.
/// Runs in <see cref="TransformSystemGroup"/>, which already updates after
/// <see cref="TileScrollPositionSystem"/> (<see cref="SimulationSystemGroup"/>, UpdateBefore TransformSystemGroup).
/// </summary>
[UpdateInGroup(typeof(TransformSystemGroup))]
[BurstCompile]
public partial struct objectPositionUpdateSystem : ISystem
{
    private ComponentLookup<LocalTransform> _tileTransformLookup;

    /// <summary>Registers the <see cref="StaticObjectTileOwnership"/> requirement and caches the tile transform lookup.</summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectTileOwnership>();
        _tileTransformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    /// <summary>
    /// Updates the tile transform lookup and schedules <c>objectPositionUpdateJob</c> in parallel to
    /// recompute each static object's world position as <c>tilePosition + localOffset</c>.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Update lookup for this frame
        _tileTransformLookup.Update(ref state);
        
        // Schedule parallel job to update tree positions
        var updateJob = new objectPositionUpdateJob
        {
            tileTransformLookup = _tileTransformLookup
        };
        
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
    }
    
    /// <summary>
    /// Burst-compiled parallel job that updates tree positions based on tile ownership.
    /// Runs across multiple threads for maximum performance with constantly moving tiles.
    /// </summary>
    [BurstCompile]
    private partial struct objectPositionUpdateJob : IJobEntity
    {
        [ReadOnly]
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> tileTransformLookup;
        
        /// <summary>
        /// If the owning tile entity still exists, sets the static object's world position to
        /// <c>tileTransform.Position + ownership.localOffset</c> and reapplies the spawn rotation
        /// stored in <see cref="StaticObjectTileOwnership.localRotation"/>.
        /// </summary>
        private void Execute(
            in StaticObjectTileOwnership ownership,
            ref LocalTransform transform)
        {
            if (!tileTransformLookup.HasComponent(ownership.tileEntity))
                return;

            var tileTransform = tileTransformLookup[ownership.tileEntity];

            transform = LocalTransform.FromPositionRotationScale(
                tileTransform.Position + ownership.localOffset,
                ownership.localRotation,
                transform.Scale);
        }
    }
}

