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
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<FloatingOriginConfig>();
        state.RequireForUpdate<WorldOriginOffset>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<FloatingOriginConfig>();
        
        if (!config.enabled)
            return;

        // Find the player entity
        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        if (!SystemAPI.HasComponent<LocalTransform>(playerEntity))
            return;

        var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        var playerPosition = playerTransform.Position;
        
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
            
            // Shift all entities with FloatingOriginEnabled tag
            var shiftJob = new ShiftWorldOriginJob
            {
                offset = shiftOffset
            };
            shiftJob.ScheduleParallel();
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



