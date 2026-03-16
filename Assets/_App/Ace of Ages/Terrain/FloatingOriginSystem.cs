using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// System that monitors player distance from origin and triggers world shifts to prevent floating-point precision errors.
/// Runs in the TransformSystemGroup to ensure transforms are up-to-date.
/// </summary>
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(LocalToWorldSystem))]
public partial struct FloatingOriginSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<FloatingOriginConfig>();
        state.RequireForUpdate<WorldOriginOffset>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<FloatingOriginConfig>();
        
        if (!config.enabled)
            return;

        // Get the player transform reference (managed component, cannot use Burst)
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        
        // Check if player transform is valid
        if (playerRef == null || playerRef.playerTransform == null)
        {
            // Only log warning once per second to avoid spam
            if (math.fmod((float)SystemAPI.Time.ElapsedTime, 1.0f) < SystemAPI.Time.DeltaTime)
            {
                UnityEngine.Debug.LogWarning("FloatingOriginSystem: Player transform reference is null! Assign playerToTrack in TerrainConfigAuthoring.");
            }
            return;
        }

        // Get player position from GameObject Transform
        float3 playerPosition = playerRef.playerTransform.position;
        
        // Calculate distance from origin
        float distanceFromOrigin = math.length(playerPosition);
        
        // Check if we need to shift the world
        if (distanceFromOrigin > config.shiftThreshold)
        {
            // Calculate the offset to apply (move player back to origin)
            float3 shiftOffset = playerPosition;
            
            // Update the accumulated world offset (for terrain generation consistency)
            RefRW<WorldOriginOffset> worldOffset = SystemAPI.GetSingletonRW<WorldOriginOffset>();
            worldOffset.ValueRW.accumulatedOffset += shiftOffset;
            
            // Complete all pending jobs to avoid dependency conflicts with LocalToWorldSystem
            state.Dependency.Complete();
            
            // Shift all entities with FloatingOriginEnabled tag using synchronous execution
            // Use .Run() instead of .ScheduleParallel() to ensure GameObjects can shift in the same frame
            var shiftJob = new ShiftWorldOriginJob
            {
                offset = shiftOffset
            };
            shiftJob.Run();
            
            // Fire event for GameObject synchronization (after ECS shift completes)
            FloatingOriginEvents.InvokeOriginShifted(shiftOffset);
            
            UnityEngine.Debug.Log($"FloatingOriginSystem: Origin shifted by {shiftOffset}, accumulated offset: {worldOffset.ValueRO.accumulatedOffset}");
        }
    }
}

/// <summary>
/// Job that shifts all entities with FloatingOriginEnabled by subtracting the offset from their positions.
/// </summary>
[BurstCompile]
[WithAll(typeof(FloatingOriginEnabled))]
public partial struct ShiftWorldOriginJob : IJobEntity
{
    public float3 offset;
    
    public void Execute(ref LocalTransform transform)
    {
        transform.Position -= offset;
    }
}



