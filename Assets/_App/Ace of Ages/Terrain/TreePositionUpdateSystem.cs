using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// System that updates tree positions when their owning terrain tiles move.
/// Uses TreeTileOwnership to track which tile each tree belongs to without parent-child hierarchy.
/// This approach avoids the performance overhead of transform hierarchy while maintaining visual cohesion.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileScrollPositionSystem))]
public partial struct TreePositionUpdateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TreeTileOwnership>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get all tile positions in a lookup for fast access
        var tileTransforms = SystemAPI.GetComponentLookup<LocalTransform>(true);
        
        // Update tree positions based on their owning tile
        foreach (var (ownership, transform) in SystemAPI.Query<RefRO<TreeTileOwnership>, RefRW<LocalTransform>>())
        {
            // Check if tile still exists
            if (!tileTransforms.HasComponent(ownership.ValueRO.tileEntity))
                continue;
            
            // Get tile's current position
            var tileTransform = tileTransforms[ownership.ValueRO.tileEntity];
            
            // Update tree position: tile position + local offset
            transform.ValueRW.Position = tileTransform.Position + ownership.ValueRO.localOffset;
        }
    }
}

