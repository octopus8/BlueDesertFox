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
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<TerrainTileConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        var tileConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Update all terrain tile positions based on their grid coordinates and scroll offset
        foreach (var (tile, transform) in SystemAPI.Query<RefRO<TerrainTile>, RefRW<LocalTransform>>())
        {
            // Calculate base position from grid coordinates (centered for accurate LOD distance)
            // Tile transform is placed at the CENTER of the tile, not the corner
            float3 basePosition = new float3(
                tile.ValueRO.gridCoordinate.x * tileConfig.tileSize + tileConfig.tileSize * 0.5f,
                0,
                tile.ValueRO.gridCoordinate.y * tileConfig.tileSize + tileConfig.tileSize * 0.5f
            );
            
            // Apply directional scroll offset (subtract to make tiles move opposite to scroll direction)
            // This creates the effect of tiles scrolling through a fixed center point (the player)
            transform.ValueRW.Position = basePosition - scrollOffset.accumulatedOffset;
        }
    }
}
