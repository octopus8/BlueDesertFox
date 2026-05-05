using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that updates terrain anchor positions based on scroll offset.
/// Uses parallel IJobEntity to efficiently process anchor entities across multiple CPU cores.
/// Works identically to TileScrollPositionSystem but for arbitrary entities tagged as terrain anchors.
/// This allows GameObjects converted to entities to move with the scrolling terrain.
/// Optimized for Quest 3 VR performance with heavy entity loads.
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
        state.RequireForUpdate<TerrainAnchorTag>(); // Only run if anchor entities exist
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        
        // Schedule parallel job to update anchor positions
        var updateJob = new TerrainAnchorUpdateJob
        {
            scrollOffset = scrollOffset.accumulatedOffset
        };
        
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
    }
    
    /// <summary>
    /// Burst-compiled parallel job that updates anchor positions based on scroll offset.
    /// Runs across multiple threads for maximum performance with constantly scrolling terrain.
    /// </summary>
    [BurstCompile]
    private partial struct TerrainAnchorUpdateJob : IJobEntity
    {
        [ReadOnly]
        public float3 scrollOffset;
        
        private void Execute(
            in TerrainAnchorTag anchor,
            ref LocalTransform transform)
        {
            // Apply scroll offset (subtract to make anchor move opposite to scroll direction)
            // This creates the effect of the anchor scrolling with the terrain
            transform.Position = anchor.basePosition - scrollOffset;
        }
    }
}

