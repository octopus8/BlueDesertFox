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
    /// <summary>
    /// Registers required singletons (<see cref="TerrainScrollVelocity"/> and <see cref="ScrollOffset"/>)
    /// so the system only runs when auto-scrolling is configured.
    /// </summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainScrollVelocity>();
        state.RequireForUpdate<ScrollOffset>();
    }

    /// <summary>
    /// Accumulates the scroll delta for this frame into <see cref="ScrollOffset.accumulatedOffset"/>
    /// based on the current <see cref="TerrainScrollVelocity"/> direction and speed.
    /// Does nothing if <see cref="TerrainScrollVelocity.speed"/> is zero.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var paused) && paused.Value)
            return;

        var scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>();
        
        if (scrollVelocity.speed == 0f && scrollVelocity.verticalSpeed == 0f)
            return;
        
        RefRW<ScrollOffset> scrollOffset = SystemAPI.GetSingletonRW<ScrollOffset>();
        
        float3 scrollDelta = scrollVelocity.WorldVelocity * SystemAPI.Time.DeltaTime;
        scrollOffset.ValueRW.accumulatedOffset += scrollDelta;
    }
}
