using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that updates tree positions when their owning terrain tiles move.
/// Uses parallel IJobEntity to efficiently process thousands of trees across multiple CPU cores.
/// This approach avoids the performance overhead of transform hierarchy while maintaining visual cohesion.
/// </summary>
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(TileScrollPositionSystem))]
[BurstCompile]
public partial struct TreePositionUpdateSystem : ISystem
{
    private ComponentLookup<LocalTransform> _tileTransformLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TreeTileOwnership>();
        _tileTransformLookup = state.GetComponentLookup<LocalTransform>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Update lookup for this frame
        _tileTransformLookup.Update(ref state);
        
        // Schedule parallel job to update tree positions
        var updateJob = new TreePositionUpdateJob
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
    private partial struct TreePositionUpdateJob : IJobEntity
    {
        [ReadOnly]
        [NativeDisableParallelForRestriction]
        [NativeDisableContainerSafetyRestriction]
        public ComponentLookup<LocalTransform> tileTransformLookup;
        
        private void Execute(
            in TreeTileOwnership ownership,
            ref LocalTransform transform)
        {
            // Check if tile still exists
            if (!tileTransformLookup.HasComponent(ownership.tileEntity))
                return;
            
            // Get tile's current position
            var tileTransform = tileTransformLookup[ownership.tileEntity];
            
            // Update tree position: tile position + local offset
            transform.Position = tileTransform.Position + ownership.localOffset;
        }
    }
}

