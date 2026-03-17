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
    private float3 _lastPlayerPosition;
    private bool _initialized;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<FloatingOriginConfig>();
        state.RequireForUpdate<WorldOriginOffset>();
        
        _initialized = false;
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

        // CRITICAL: Sample player position BEFORE any modifications occur this frame
        float3 playerPosition = playerRef.playerTransform.position;
        
        // Initialize last position on first run to player's starting position
        if (!_initialized)
        {
            _lastPlayerPosition = playerPosition;
            _initialized = true;
        }
        
        // Calculate movement delta from starting position
        float3 deltaFromStart = playerPosition - _lastPlayerPosition;
        float distanceFromStart = math.length(deltaFromStart);
        
        // Check if we need to shift the world (based on distance moved from starting position)
        if (distanceFromStart > config.shiftThreshold)
        {
            // Calculate the offset to apply (delta movement since last shift)
            float3 shiftOffset = deltaFromStart;
            
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
            
            // CRITICAL: Shift the player GameObject directly BEFORE firing event
            // This prevents ObjectFollower interference and double-shifting
            playerRef.playerTransform.position -= (UnityEngine.Vector3)shiftOffset;
            
            // Fire event for non-player GameObject synchronization (terrain props, particles, etc.)
            // Note: Player GameObject is already shifted above, so subscribers should exclude it
            FloatingOriginEvents.InvokeNonPlayerOriginShifted(shiftOffset);
            
            // Update last position after shift (should be near origin now)
            _lastPlayerPosition = playerRef.playerTransform.position;
            
            #if UNITY_EDITOR
            UnityEngine.Debug.Log($"FloatingOriginSystem: Origin shifted by {shiftOffset}, accumulated offset: {worldOffset.ValueRO.accumulatedOffset}");
            #endif
        }
    }
}

/// <summary>
/// Job that shifts all entities with FloatingOriginEnabled by subtracting the offset from their positions.
/// Also immediately updates LocalToWorld matrices to prevent visual glitches during rendering.
/// NOTE: PhysicsColliderValid tags are NOT removed - colliders remain geometrically valid after position shift.
/// </summary>
[BurstCompile]
[WithAll(typeof(FloatingOriginEnabled))]
public partial struct ShiftWorldOriginJob : IJobEntity
{
    public float3 offset;
    
    public void Execute(ref LocalTransform transform, ref LocalToWorld localToWorld)
    {
        // Shift the local position
        transform.Position -= offset;
        
        // Immediately update LocalToWorld matrix to prevent one-frame visual glitch
        // Reconstruct the matrix with the new position
        localToWorld.Value = float4x4.TRS(
            transform.Position,
            transform.Rotation,
            transform.Scale
        );
    }
}

