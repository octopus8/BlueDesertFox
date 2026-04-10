using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that provides scroll velocity based on the player's facing direction with head-tracking rotation.
/// Reads PlayerTransformReference and HeadsetTransformReference, calculates rotation based on angle difference,
/// and writes to TerrainScrollVelocity singleton.
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
        RequireForUpdate<HeadsetTransformReference>();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<PlayerTerrainScrollVelocityConfig>();
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        var headsetRef = SystemAPI.ManagedAPI.GetSingleton<HeadsetTransformReference>();
        
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
        
        // If headset is not available, disable rotation and use player forward only
        if (headsetRef?.headsetTransform == null)
        {
            // Update TerrainScrollVelocity with player direction only (no rotation)
            RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();
            scrollVelocity.ValueRW.direction = baseScrollDirection;
            scrollVelocity.ValueRW.speed = config.speed;
            return;
        }
        
        // Get headset's forward direction and project onto XZ plane
        UnityEngine.Vector3 headsetForward = headsetRef.headsetTransform.forward;
        float3 headsetDirection = new float3(headsetForward.x, 0, headsetForward.z);
        
        // Normalize headset direction
        if (math.lengthsq(headsetDirection) > 0.0001f)
            headsetDirection = math.normalize(headsetDirection);
        else
            headsetDirection = baseScrollDirection; // Fallback to player direction
        
        // Calculate signed angle between player forward and headset forward on XZ plane
        float angle = UnityEngine.Vector3.SignedAngle(
            new UnityEngine.Vector3(baseScrollDirection.x, 0, baseScrollDirection.z),
            new UnityEngine.Vector3(headsetDirection.x, 0, headsetDirection.z),
            UnityEngine.Vector3.up
        );
        
        // Calculate rotation to apply this frame (proportional to angle and rotation speed)
        float rotationThisFrame = angle * config.rotationSpeed * SystemAPI.Time.DeltaTime;
        
        // Apply rotation to base scroll direction around Y axis
        quaternion rotation = quaternion.AxisAngle(math.up(), math.radians(rotationThisFrame));
        RefRW<TerrainScrollVelocity> scrollVelocityFinal = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();

        float3 currentDirection = scrollVelocityFinal.ValueRO.direction;
        if (math.lengthsq(currentDirection) < 0.0001f)
            currentDirection = new float3(0, 0, 1); // Default forward if no valid direction
        
        
        float3 rotatedDirection = math.mul(rotation, currentDirection);

        // Not sure if this is needed.
        rotatedDirection = math.normalizesafe(rotatedDirection);
        
        scrollVelocityFinal.ValueRW.direction = rotatedDirection;
        scrollVelocityFinal.ValueRW.speed = config.speed;
    }
}
