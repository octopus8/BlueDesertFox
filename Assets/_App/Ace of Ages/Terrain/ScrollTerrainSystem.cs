using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that updates the terrain scroll offset each frame for auto-scrolling terrain.
/// Reads scroll direction and speed from TerrainScrollVelocity component.
/// Runs before TileSpawningSystem to ensure tiles spawn with updated scroll position.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TileSpawningSystem))]
public partial struct ScrollTerrainSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainScrollVelocity>();
        state.RequireForUpdate<ScrollOffset>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>();
        
        // Early return if speed is zero
        if (scrollVelocity.speed == 0f)
            return;
        
        // Get the scroll offset singleton
        RefRW<ScrollOffset> scrollOffset = SystemAPI.GetSingletonRW<ScrollOffset>();
        
        // Accumulate scroll distance using provided direction and speed
        float3 scrollDelta = scrollVelocity.direction * scrollVelocity.speed * SystemAPI.Time.DeltaTime;
        scrollOffset.ValueRW.accumulatedOffset += scrollDelta;
    }
}
