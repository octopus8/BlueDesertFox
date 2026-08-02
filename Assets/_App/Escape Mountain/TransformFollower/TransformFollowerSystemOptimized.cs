using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;

/// <summary>
/// Batches Transform data updates so entities can follow GameObject Transforms efficiently.
/// </summary>
/// <remarks>
/// Approach:
/// 1. Collect all Transform data on the main thread ONCE per frame into a native container
/// 2. Process all entities in a Burst-compiled parallel job using the cached Transform data
/// 
/// Efficient for many followers, but still requires main-thread access to read Transform data.
/// Uses ISystem with proper dependency chaining to prevent race conditions with rendering
/// systems (e.g., frustum culling). MUST use
/// <c>state.Dependency = job.ScheduleParallel(state.Dependency)</c>.
/// </remarks>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TransformFollowerSystemOptimized : ISystem
{
    private EntityQuery _followerQuery;
    private NativeList<TransformData> _transformDataCache;
    
    /// <summary>Cached world-space transform snapshot read from a managed <see cref="UnityEngine.Transform"/> on the main thread.</summary>
    struct TransformData
    {
        /// <summary>World-space position of the target Transform this frame.</summary>
        public float3 position;
        /// <summary>World-space rotation of the target Transform this frame.</summary>
        public quaternion rotation;
    }
    
    /// <summary>Builds the follower entity query and allocates the persistent transform-data cache list.</summary>
    public void OnCreate(ref SystemState state)
    {
        _followerQuery = state.GetEntityQuery(
            ComponentType.ReadWrite<LocalTransform>(),
            ComponentType.ReadOnly<TransformFollowerSettings>(),
            ComponentType.ReadOnly<TransformReference>()
        );
        
        _transformDataCache = new NativeList<TransformData>(Allocator.Persistent);
    }
    
    /// <summary>Disposes the persistent transform-data cache and frees native memory.</summary>
    public void OnDestroy(ref SystemState state)
    {
        if (_transformDataCache.IsCreated)
        {
            _transformDataCache.Dispose();
        }
    }
    
    /// <summary>
    /// Step 1 (main thread): reads each managed <see cref="TransformReference"/>.target's world position and
    /// rotation into the native <c>_transformDataCache</c> list.
    /// Step 2: schedules the Burst-compiled <c>TransformFollowerJob</c> in parallel with proper dependency
    /// chaining so rendering systems receive up-to-date <see cref="LocalTransform"/> values.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        int entityCount = _followerQuery.CalculateEntityCount();
        
        if (entityCount == 0)
        {
            return;
        }
        
        // Resize cache if needed
        _transformDataCache.Clear();
        if (_transformDataCache.Capacity < entityCount)
        {
            _transformDataCache.Capacity = entityCount;
        }
        
        // Step 1: Collect all Transform data on the main thread (cannot be avoided)
        // Must use managed API since TransformReference is a managed component
        foreach (var transformRef in SystemAPI.Query<TransformReference>())
        {
            TransformData data = default;
            
            if (transformRef.target != null)
            {
                data.position = transformRef.target.position;
                data.rotation = transformRef.target.rotation;
            }
            
            _transformDataCache.Add(data);
        }
        
        // Step 2: Process all entities in a Burst-compiled parallel job with proper dependency chaining
        var transformData = _transformDataCache.AsArray();
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        var job = new TransformFollowerJob
        {
            transformData = transformData,
            deltaTime = deltaTime
        };
        
        // ✅ CRITICAL FIX: Chain dependencies to prevent race conditions
        // This ensures the job completes before rendering systems (like GlobalTreeInstanceSystem)
        // read the updated LocalTransform components for frustum culling
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }
    
    /// <summary>
    /// Burst-compiled parallel job that applies the pre-collected transform-data snapshot to each
    /// follower entity's <see cref="LocalTransform"/>, respecting position offset and optional
    /// rotation following with configurable smooth interpolation.
    /// </summary>
    [BurstCompile]
    partial struct TransformFollowerJob : IJobEntity
    {
        /// <summary>Read-only array of per-entity target transform snapshots collected on the main thread.</summary>
        [ReadOnly] public NativeArray<TransformData> transformData;
        /// <summary>Elapsed time in seconds since the last frame, used for smooth interpolation.</summary>
        public float deltaTime;
        
        /// <summary>
        /// Reads the cached <see cref="TransformData"/> for this entity's index, computes the target
        /// position with settings offset, and updates <see cref="LocalTransform"/> with optional smoothing.
        /// </summary>
        public void Execute(
            [EntityIndexInQuery] int entityIndexInQuery,
            ref LocalTransform localTransform,
            in TransformFollowerSettings settings)
        {
            // Get the cached transform data for this entity
            var data = transformData[entityIndexInQuery];
            
            // Calculate target position with offset
            float3 targetPosition = data.position + settings.offset;
            
            // Apply smoothing if needed
            if (settings.smoothTime > 0)
            {
                float smoothFactor = math.saturate(deltaTime / settings.smoothTime);
                localTransform.Position = math.lerp(localTransform.Position, targetPosition, smoothFactor);
            }
            else
            {
                localTransform.Position = targetPosition;
            }
            
            // Follow rotation if enabled
            if (settings.followRotation)
            {
                quaternion targetRotation = data.rotation;
                
                if (settings.smoothTime > 0)
                {
                    float smoothFactor = math.saturate(deltaTime / settings.smoothTime);
                    localTransform.Rotation = math.slerp(localTransform.Rotation, targetRotation, smoothFactor);
                }
                else
                {
                    localTransform.Rotation = targetRotation;
                }
            }
        }
    }
}




