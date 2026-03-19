using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// System that updates tile positions based on scroll offset for auto-scrolling terrain.
/// Runs after ScrollTerrainSystem to update existing tiles as scroll changes.
/// This ensures ALL tiles (not just newly spawned ones) move with the scroll.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TileScrollPositionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScrollConfig>();
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<TerrainTileConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ScrollConfig>();
        
        if (!config.enabled)
            return;
        
        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        var tileConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Update all terrain tile positions based on their grid coordinates and scroll offset
        foreach (var (tile, transform) in SystemAPI.Query<RefRO<TerrainTile>, RefRW<LocalTransform>>())
        {
            // Calculate base position from grid coordinates
            float3 basePosition = new float3(
                tile.ValueRO.gridCoordinate.x * tileConfig.tileSize,
                0,
                tile.ValueRO.gridCoordinate.y * tileConfig.tileSize
            );
            
            // Apply scroll offset (subtract to make tiles move backward/toward player as scroll increases)
            // This creates the effect of tiles scrolling through a fixed center point (the player)
            transform.ValueRW.Position = new float3(
                basePosition.x,
                basePosition.y,
                basePosition.z - scrollOffset.accumulatedScrollZ
            );
        }
    }
}

