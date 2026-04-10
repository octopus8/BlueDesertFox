using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that provides scroll velocity based on the player's facing direction and rotates world origin to face scroll direction.
/// Reads PlayerTransformReference and WorldOriginTransformReference, calculates scroll direction,
/// writes to TerrainScrollVelocity singleton, and rotates the world origin to face the scroll direction.
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
        
        // If world origin is not available, use player forward only
        if (worldOriginRef?.worldOriginTransform == null)
        {
            // Update TerrainScrollVelocity with player direction only (no rotation)
            RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();
            scrollVelocity.ValueRW.direction = baseScrollDirection;
            scrollVelocity.ValueRW.speed = config.speed;
            return;
        }
        
        // Get world origin's forward direction and project onto XZ plane
        UnityEngine.Vector3 worldOriginForward = worldOriginRef.worldOriginTransform.forward;
        float3 worldOriginDirection = new float3(worldOriginForward.x, 0, worldOriginForward.z);
        
        // Normalize world origin direction
        if (math.lengthsq(worldOriginDirection) > 0.0001f)
            worldOriginDirection = math.normalize(worldOriginDirection);
        else
            worldOriginDirection = baseScrollDirection; // Fallback to player direction
        
        // Calculate signed angle between player forward and world origin forward on XZ plane
        float angle = UnityEngine.Vector3.SignedAngle(
            new UnityEngine.Vector3(baseScrollDirection.x, 0, baseScrollDirection.z),
            new UnityEngine.Vector3(worldOriginDirection.x, 0, worldOriginDirection.z),
            UnityEngine.Vector3.up
        );

        // Calculate rotation to apply this frame (proportional to angle and rotation speed)
        float rotationThisFrame = -angle * config.rotationSpeed * SystemAPI.Time.DeltaTime;
        
        // Apply rotation to current scroll direction around Y axis
        RefRW<TerrainScrollVelocity> scrollVelocityFinal = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();

        float3 currentDirection = scrollVelocityFinal.ValueRO.direction;
        if (math.lengthsq(currentDirection) < 0.0001f)
            currentDirection = new float3(0, 0, 1); // Default forward if no valid direction
        
        quaternion rotation = quaternion.AxisAngle(math.up(), math.radians(rotationThisFrame));
        float3 rotatedDirection = math.mul(rotation, currentDirection);
        rotatedDirection = math.normalizesafe(rotatedDirection);
        
        scrollVelocityFinal.ValueRW.direction = rotatedDirection;
        scrollVelocityFinal.ValueRW.speed = config.speed;
        
        // Rotate the world origin to face the scroll direction
        UnityEngine.Vector3 scrollDir3D = new UnityEngine.Vector3(rotatedDirection.x, 0, rotatedDirection.z);
        if (scrollDir3D.sqrMagnitude > 0.0001f)
        {
            UnityEngine.Quaternion targetRotation = UnityEngine.Quaternion.LookRotation(scrollDir3D, UnityEngine.Vector3.up);
            worldOriginRef.worldOriginTransform.rotation = targetRotation;
        }
    }
}
