using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that updates tile positions based on scroll offset for auto-scrolling terrain.
/// Uses parallel IJobEntity to efficiently process tiles across multiple CPU cores.
/// Runs after ScrollTerrainSystem to update existing tiles as scroll changes.
/// This ensures ALL tiles (not just newly spawned ones) move with the scroll.
/// Optimized for Quest 3 VR performance with heavy tile loads.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TileScrollPositionSystem : ISystem
{
    /// <summary>Registers required singletons: <see cref="ScrollOffset"/>, <see cref="TerrainTileConfig"/>, and <see cref="TerrainTile"/>.</summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<TerrainTile>(); // Only run if tile entities exist
    }

    /// <summary>
    /// Schedules <see cref="UpdateTilePositionsJob"/> in parallel to recompute every terrain tile's
    /// world position as <c>gridCoordinate * tileSize − scrollOffset</c> so that tiles smoothly
    /// scroll past the player each frame.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        var tileConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Schedule parallel job across Quest 3's 8 CPU cores
        var updateJob = new UpdateTilePositionsJob
        {
            scrollOffset = scrollOffset.accumulatedOffset,
            tileSize = tileConfig.tileSize
        };
        
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
    }
    
    /// <summary>
    /// Burst-compiled parallel job that updates tile positions based on scroll offset.
    /// Runs across multiple threads for maximum performance with constantly scrolling terrain.
    /// </summary>
    [BurstCompile]
    private partial struct UpdateTilePositionsJob : IJobEntity
    {
        /// <summary>Current accumulated scroll offset; subtracted from the tile's base grid position.</summary>
        [ReadOnly]
        public float3 scrollOffset;
        
        /// <summary>World-space size of each square terrain tile.</summary>
        [ReadOnly]
        public float tileSize;
        
        /// <summary>
        /// Computes the tile's base world position from its grid coordinate (centred) and subtracts
        /// the scroll offset so the tile scrolls opposite to the player's direction of travel.
        /// </summary>
        private void Execute(
            in TerrainTile tile,
            ref LocalTransform transform)
        {
            // Calculate base position from grid coordinates (centered for accurate LOD distance)
            // Tile transform is placed at the CENTER of the tile, not the corner
            float3 basePosition = new float3(
                tile.gridCoordinate.x * tileSize + tileSize * 0.5f,
                0,
                tile.gridCoordinate.y * tileSize + tileSize * 0.5f
            );
            
            // Apply directional scroll offset (subtract to make tiles move opposite to scroll direction)
            // This creates the effect of tiles scrolling through a fixed center point (the player)
            transform.Position = basePosition - scrollOffset;
        }
    }
}
