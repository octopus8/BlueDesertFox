using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using Unity.Burst;

/// <summary>
/// OPTIMIZED VERSION: System that batches Transform data updates for better performance.
/// </summary>
/// <remarks>
/// This optimized approach works around the fundamental limitation by:
/// 1. Collecting all Transform data on the main thread ONCE per frame into a native container
/// 2. Processing all entities in a Burst-compiled job using the cached Transform data
/// 
/// This is much more efficient when you have many entities following Transforms,
/// but still requires main thread access to read Transform data initially.
/// 
/// To use this instead of TransformFollowerSystem:
/// 1. Disable TransformFollowerSystem (add [DisableAutoCreation] attribute)
/// 2. Enable this system by removing [DisableAutoCreation] attribute below
/// </remarks>
[DisableAutoCreation] // Remove this to use the optimized version
[RequireMatchingQueriesForUpdate]
public partial class TransformFollowerSystemOptimized : SystemBase
{
    private EntityQuery _followerQuery;
    private NativeList<TransformData> _transformDataCache;
    
    struct TransformData
    {
        public float3 position;
        public quaternion rotation;
    }
    
    protected override void OnCreate()
    {
        _followerQuery = GetEntityQuery(
            ComponentType.ReadWrite<LocalTransform>(),
            ComponentType.ReadOnly<TransformFollowerSettings>(),
            ComponentType.ReadOnly<TransformReference>()
        );
        
        _transformDataCache = new NativeList<TransformData>(Allocator.Persistent);
    }
    
    protected override void OnDestroy()
    {
        if (_transformDataCache.IsCreated)
        {
            _transformDataCache.Dispose();
        }
    }
    
    protected override void OnUpdate()
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
        
        // Step 2: Process all entities in a Burst-compiled job
        var transformData = _transformDataCache.AsArray();
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        new TransformFollowerJob
        {
            transformData = transformData,
            deltaTime = deltaTime
        }.ScheduleParallel();
    }
    
    [BurstCompile]
    partial struct TransformFollowerJob : IJobEntity
    {
        [ReadOnly] public NativeArray<TransformData> transformData;
        public float deltaTime;
        
        private int _index;
        
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




