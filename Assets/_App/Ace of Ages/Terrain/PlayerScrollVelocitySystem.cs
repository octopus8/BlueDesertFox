using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Drives terrain scroll and vertical world-origin movement from a single speed value distributed by pitch angle.
/// At pitch = 0 (level): terrain scrolls at full speed, vertical = 0.
/// At pitch = 90 (nose-up): scroll = 0, vertical rises at full speed.
/// Also rotates the world origin based on player roll (bank-to-turn steering).
/// Only runs when PlayerTerrainScrollVelocityConfig exists in the scene.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(TransformFollowerSystem))]
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
        
        // Decompose speed into horizontal scroll and vertical components using pitch angle.
        // playerForward.y == sin(pitch), so cos(pitch) gives the horizontal factor.
        // At pitch = 0 (level):    scroll = speed, vertical = 0
        // At pitch = 90 (nose-up): scroll = 0,     vertical = speed
        float sinPitch = playerForward.y;
        float cosPitch = math.sqrt(1f - sinPitch * sinPitch);

        RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();
        scrollVelocity.ValueRW.direction = baseScrollDirection;
        scrollVelocity.ValueRW.speed = config.speed * cosPitch;

        // Rotate world origin based on player's bank angle (local Z-axis rotation)
        if (worldOriginRef?.worldOriginTransform != null)
        {
            // Get player's world rotation euler angles (not local)
            UnityEngine.Vector3 playerEuler = playerRef.playerTransform.eulerAngles;
            float bankAngle = playerEuler.z;
            
            // Convert from 0-360 to -180 to 180 for proper direction
            if (bankAngle > 180f)
                bankAngle -= 360f;
            
            // Use sine function to map bank angle to rotation speed
            // At ±90°: sin = ±1.0 (full speed rotation)
            // At 0°/180°: sin = 0 (no rotation)
            // This creates a natural steering curve
            float bankRadians = math.radians(bankAngle);
            float rotationSpeed = -math.sin(bankRadians);
            
            float rotationAmount = rotationSpeed * config.rotationSpeed * SystemAPI.Time.DeltaTime;
            worldOriginRef.worldOriginTransform.rotation *= UnityEngine.Quaternion.Euler(0, rotationAmount, 0);
        }
        
        // Vertical velocity is the sin(pitch) portion of the total speed.
        // Nose-up (positive pitch) raises the world origin; nose-down lowers it.
        float verticalVelocity = config.speed * sinPitch;
        
        // Apply vertical movement with clamping
        UnityEngine.Vector3 currentPosition = worldOriginRef.worldOriginTransform.position;
        float newYPosition = currentPosition.y + (verticalVelocity * SystemAPI.Time.DeltaTime);
        
        // Clamp Y position to prevent extreme offsets
        newYPosition = math.clamp(newYPosition, config.minVerticalPosition, config.maxVerticalPosition);
        
        worldOriginRef.worldOriginTransform.position = new UnityEngine.Vector3(
            currentPosition.x,
            newYPosition,
            currentPosition.z
        );
        
        
    }
}
