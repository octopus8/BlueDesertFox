using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that provides scroll velocity based on the player's facing direction and rotates world origin based on player roll.
/// Reads PlayerTransformReference and WorldOriginTransformReference, calculates scroll direction,
/// writes to TerrainScrollVelocity singleton, rotates the world origin based on player's local roll angle.
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
        RequireForUpdate<WorldOriginTransformReference>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<PlayerTerrainScrollVelocityConfig>();
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        var worldOriginRef = SystemAPI.ManagedAPI.GetSingleton<WorldOriginTransformReference>();
        
        // Early return if player transform is null or not yet initialized
        if (playerRef?.playerTransform == null)
            return;
        
        // Get player's forward direction and project onto XZ plane (remove Y component)
        UnityEngine.Vector3 playerForward = playerRef.playerTransform.forward;
        float3 baseScrollDirection = new float3(playerForward.x, 0, playerForward.z);
        
        // Normalize base direction
        if (math.lengthsq(baseScrollDirection) > 0.0001f)
            baseScrollDirection = math.normalize(baseScrollDirection);
        else
            baseScrollDirection = new float3(0, 0, 1); // Default forward if no valid direction
        
        // Update the scroll direction and speed in the TerrainScrollVelocity singleton.
        RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();
        scrollVelocity.ValueRW.direction = baseScrollDirection;
        scrollVelocity.ValueRW.speed = config.speed;

        // Rotate the world origin slowly to the right.
        worldOriginRef.worldOriginTransform.rotation *= quaternion.Euler(0, config.rotationSpeed * SystemAPI.Time.DeltaTime, 0);
        
    }
}
