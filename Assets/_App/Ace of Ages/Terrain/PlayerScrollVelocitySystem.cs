using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that provides scroll velocity based on the player's facing direction.
/// Reads PlayerTransformReference, projects forward direction to XZ plane, and writes to TerrainScrollVelocity singleton.
/// Only runs when PlayerTerrainScrollVelocityConfig exists in the scene.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ScrollTerrainSystem))]
public partial class PlayerScrollVelocitySystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<TerrainScrollVelocity>();
        RequireForUpdate<PlayerTerrainScrollVelocityConfig>();
        RequireForUpdate<PlayerTransformReference>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<PlayerTerrainScrollVelocityConfig>();
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        
        // Early return if player transform is null or not yet initialized
        if (playerRef?.playerTransform == null)
            return;
        
        // Get player's forward direction and project onto XZ plane (remove Y component)
        UnityEngine.Vector3 forward = playerRef.playerTransform.forward;
        float3 scrollDirection = new float3(forward.x, 0, forward.z);
        
        // Normalize direction (expected by ScrollTerrainSystem)
        if (math.lengthsq(scrollDirection) > 0.0001f)
            scrollDirection = math.normalize(scrollDirection);
        else
            scrollDirection = new float3(0, 0, 1); // Default forward if no valid direction
        
        // Update TerrainScrollVelocity singleton
        RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();
        scrollVelocity.ValueRW.direction = scrollDirection;
        scrollVelocity.ValueRW.speed = config.speed;
    }
}



