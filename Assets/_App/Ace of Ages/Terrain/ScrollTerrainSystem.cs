using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that updates the terrain scroll offset each frame for auto-scrolling terrain.
/// Scrolls in the direction the player is facing (projected onto XZ plane).
/// Runs before TileSpawningSystem to ensure tiles spawn with updated scroll position.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TileSpawningSystem))]
public partial struct ScrollTerrainSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScrollConfig>();
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<PlayerTransformReference>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ScrollConfig>();
        
        if (!config.enabled || config.scrollSpeed == 0f)
            return;
        
        // Get player transform to determine scroll direction
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        
        if (playerRef == null || playerRef.playerTransform == null)
            return;
        
        // Get player's forward direction and project onto XZ plane (remove Y component)
        UnityEngine.Vector3 forward = playerRef.playerTransform.forward;
        float3 scrollDirection = math.normalize(new float3(forward.x, 0, forward.z));
        
        // Get the scroll offset singleton
        RefRW<ScrollOffset> scrollOffset = SystemAPI.GetSingletonRW<ScrollOffset>();
        
        // Accumulate scroll distance in the player's forward direction
        float scrollDelta = config.scrollSpeed * SystemAPI.Time.DeltaTime;
        scrollOffset.ValueRW.accumulatedOffset += scrollDirection * scrollDelta;
        
        #if UNITY_EDITOR
        // Optional debug logging (can be commented out for production)
        float totalDistance = math.length(scrollOffset.ValueRO.accumulatedOffset);
        if (totalDistance % 100f < config.scrollSpeed * SystemAPI.Time.DeltaTime)
        {
            UnityEngine.Debug.Log($"ScrollTerrainSystem: Scrolled {totalDistance:F1}m in direction {scrollDirection}");
        }
        #endif
    }
}
