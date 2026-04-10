using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// System that updates terrain anchor positions based on scroll offset.
/// Works identically to TileScrollPositionSystem but for arbitrary entities tagged as terrain anchors.
/// This allows GameObjects converted to entities to move with the scrolling terrain.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TerrainAnchorSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScrollOffset>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        
        // Update all terrain anchor positions based on their base position and scroll offset
        foreach (var (anchor, transform) in SystemAPI.Query<RefRO<TerrainAnchorTag>, RefRW<LocalTransform>>())
        {
            // Apply scroll offset (subtract to make anchor move opposite to scroll direction)
            // This creates the effect of the anchor scrolling with the terrain
            transform.ValueRW.Position = anchor.ValueRO.basePosition - scrollOffset.accumulatedOffset;
        }
    }
}

