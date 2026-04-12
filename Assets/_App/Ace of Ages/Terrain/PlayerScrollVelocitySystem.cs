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

        // Rotate world origin based on player's bank angle (local Z-axis rotation)
        if (worldOriginRef?.worldOriginTransform != null)
        {
            // Get player's world rotation euler angles (not local)
            UnityEngine.Vector3 playerEuler = playerRef.playerTransform.eulerAngles;
            float bankAngle = playerEuler.z;
            
            // Convert from 0-360 to -180 to 180 for proper direction
            if (bankAngle > 180f)
                bankAngle -= 360f;
            
            // Rotate world origin proportional to bank angle
            // Positive bank (right) rotates right, negative bank (left) rotates left
            // Speed is proportional to the amount of bank
            float rotationAmount = -bankAngle * config.rotationSpeed * SystemAPI.Time.DeltaTime;
            worldOriginRef.worldOriginTransform.rotation *= UnityEngine.Quaternion.Euler(0, rotationAmount, 0);
        }
        
    }
}
