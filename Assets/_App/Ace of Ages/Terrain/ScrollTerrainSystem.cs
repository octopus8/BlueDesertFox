using Unity.Burst;
using Unity.Entities;

/// <summary>
/// System that updates the terrain scroll offset each frame for auto-scrolling terrain.
/// Runs before TileSpawningSystem to ensure tiles spawn with updated scroll position.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TileSpawningSystem))]
public partial struct ScrollTerrainSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScrollConfig>();
        state.RequireForUpdate<ScrollOffset>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ScrollConfig>();
        
        if (!config.enabled || config.scrollSpeed == 0f)
            return;
        
        // Get the scroll offset singleton
        RefRW<ScrollOffset> scrollOffset = SystemAPI.GetSingletonRW<ScrollOffset>();
        
        // Accumulate scroll distance based on delta time
        float scrollDelta = config.scrollSpeed * SystemAPI.Time.DeltaTime;
        scrollOffset.ValueRW.accumulatedScrollZ += scrollDelta;
        
        #if UNITY_EDITOR
        // Optional debug logging (can be commented out for production)
        if (UnityEngine.Mathf.Abs(scrollOffset.ValueRO.accumulatedScrollZ % 100f) < config.scrollSpeed * SystemAPI.Time.DeltaTime)
        {
            UnityEngine.Debug.Log($"ScrollTerrainSystem: Scrolled {scrollOffset.ValueRO.accumulatedScrollZ:F1}m");
        }
        #endif
    }
}

