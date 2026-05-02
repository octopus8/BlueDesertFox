using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that renders all tree entities using Graphics.DrawMeshInstanced for maximum batching efficiency.
/// This approach dramatically reduces draw calls compared to individual entity rendering.
/// Trees are batched by mesh/material combination, with up to 1023 instances per batch.
/// OPTIMIZED v2.0: Native-only batching pipeline with distance culling for Quest 3 VR performance.
/// - Uses NativeList for zero-GC batch storage
/// - Burst-compiled parallel jobs for matrix collection
/// - Distance-based culling before frustum culling
/// - Optimized batch conversion with native array operations
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GlobalTreeInstanceSystem : SystemBase
{
    /// <summary>
    /// Native struct for batch data - replaces managed TreeBatch class.
    /// Uses NativeList for zero-allocation matrix storage.
    /// </summary>
    private struct TreeBatchNative
    {
        public int meshIndex;
        public int materialIndex;
        public NativeList<Matrix4x4> matrices;
    }
    
    /// <summary>
    /// Burst-compiled parallel job that collects tree transform matrices into batches.
    /// Processes thousands of trees across multiple CPU cores for maximum performance.
    /// OPTIMIZED v2.0: Added distance culling before frustum culling for Quest 3 VR.
    /// </summary>
    [BurstCompile]
    private partial struct CollectTreeMatricesJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Matrix4x4>.ParallelWriter BatchMatrices;
        public int MeshArrayLength;
        public int MaterialArrayLength;
        
        [ReadOnly] public NativeArray<float4> FrustumPlanes;
        public bool EnableFrustumCulling;
        
        // NEW: Distance culling parameters
        [ReadOnly] public float3 PlayerPosition;
        public float MaxRenderDistance;
        public bool EnableDistanceCulling;
        
        private void Execute(in LocalTransform transform, in GlobalTreeInstanceData instanceData)
        {
            // Validate indices
            if (instanceData.meshIndex < 0 || instanceData.meshIndex >= MeshArrayLength ||
                instanceData.materialIndex < 0 || instanceData.materialIndex >= MaterialArrayLength)
                return;
            
            float3 treePos = transform.Position;
            
            // OPTIMIZATION: Distance culling first (cheapest check)
            if (EnableDistanceCulling)
            {
                // 2D distance check (XZ plane) - cheaper than 3D
                float2 treePos2D = new float2(treePos.x, treePos.z);
                float2 playerPos2D = new float2(PlayerPosition.x, PlayerPosition.z);
                float distanceSq = math.distancesq(treePos2D, playerPos2D);
                
                if (distanceSq > MaxRenderDistance * MaxRenderDistance)
                    return;
            }
            
            // Frustum culling: Test if tree is visible
            if (EnableFrustumCulling && FrustumPlanes.Length == 6)
            {
                float treeRadius = transform.Scale * 10f; // Estimate: 10m radius for typical tree
                
                // Test against all 6 frustum planes
                for (int i = 0; i < 6; i++)
                {
                    float4 plane = FrustumPlanes[i];
                    float3 planeNormal = plane.xyz;
                    float planeDistance = plane.w;
                    
                    // Distance from point to plane
                    float dist = math.dot(planeNormal, treePos) + planeDistance;
                    
                    // If tree is completely outside this plane, skip it
                    if (dist < -treeRadius)
                        return;
                }
            }
            
            // Calculate batch key: meshIndex * 1000 + materialIndex
            // Safe for <1000 materials per mesh type
            int batchKey = instanceData.meshIndex * 1000 + instanceData.materialIndex;
            
            // Add transform matrix to batch
            var matrix = Matrix4x4.TRS(
                treePos,
                transform.Rotation,
                new Vector3(transform.Scale, transform.Scale, transform.Scale)
            );
            
            BatchMatrices.Add(batchKey, matrix);
        }
    }
    
    /// <summary>
    /// Converts NativeMultiHashMap to batched NativeList arrays on main thread.
    /// This replaces the managed collection conversion that was causing GC allocations.
    /// NOTE: Runs on main thread (not a job) because nested native containers not allowed in jobs.
    /// </summary>
    private void ConvertToBatches()
    {
        _batchKeys.Clear();
        
        // Get all batch keys and build unique set
        var allKeys = _batchMatrices.GetKeyArray(Allocator.Temp);
        var uniqueKeysSet = new NativeHashSet<int>(allKeys.Length, Allocator.Temp);
        
        // Build unique keys set
        for (int i = 0; i < allKeys.Length; i++)
        {
            uniqueKeysSet.Add(allKeys[i]);
        }
        
        // Process each unique batch key
        var uniqueKeysArray = uniqueKeysSet.ToNativeArray(Allocator.Temp);
        for (int i = 0; i < uniqueKeysArray.Length; i++)
        {
            int batchKey = uniqueKeysArray[i];
            
            // Get or create batch
            if (!_batchesNative.TryGetValue(batchKey, out var batch))
            {
                // Create new batch with native list
                batch = new TreeBatchNative
                {
                    meshIndex = batchKey / 1000,
                    materialIndex = batchKey % 1000,
                    matrices = new NativeList<Matrix4x4>(256, Allocator.Persistent)
                };
            }
            else
            {
                // Clear existing batch for reuse
                batch.matrices.Clear();
            }
            
            // Collect all matrices for this batch key
            if (_batchMatrices.TryGetFirstValue(batchKey, out var matrix, out var iterator))
            {
                do
                {
                    batch.matrices.Add(matrix);
                }
                while (_batchMatrices.TryGetNextValue(out matrix, ref iterator));
            }
            
            // Store batch back
            _batchesNative[batchKey] = batch;
            _batchKeys.Add(batchKey);
        }
        
        // Clean up
        allKeys.Dispose();
        uniqueKeysSet.Dispose();
        uniqueKeysArray.Dispose();
    }
    
    // Native collections for zero-GC batch processing
    private NativeParallelMultiHashMap<int, Matrix4x4> _batchMatrices;
    private NativeParallelHashMap<int, TreeBatchNative> _batchesNative;
    private NativeList<int> _batchKeys;
    
    // Rendering data
    private Matrix4x4[] _renderMatrixArray;
    private EntityQuery _treeQuery;
    private Plane[] _frustumPlanes = new Plane[6];
    private Camera _mainCamera;
    private const int MaxInstancesPerBatch = 1023; // Unity limitation for DrawMeshInstanced
    
    // VR-optimized culling parameters
    private const float DefaultMaxRenderDistance = 400f; // Quest 3 recommended: 300-400m
    private float _maxRenderDistance = DefaultMaxRenderDistance;
    
    // ✅ Cached rendering data to avoid GC allocations every frame
    private GlobalTreeRenderingData _cachedRenderingData;
    
    // Frame counter for periodic debug logging (used outside UNITY_EDITOR for flag-based control)
    private int _frameCount;
    
#if UNITY_EDITOR
    private static readonly ProfilerMarker ProfilerMarker = new ProfilerMarker("GlobalTreeInstance.Render");
    private static readonly ProfilerMarker CollectMarker = new ProfilerMarker("GlobalTreeInstance.Collect");
    private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("GlobalTreeInstance.Draw");
    private static readonly ProfilerMarker ConvertMarker = new ProfilerMarker("GlobalTreeInstance.Convert");
    private int _lastTreeCount;
    private int _lastBatchCount;
#endif

    protected override void OnCreate()
    {
        // Require the tree spawner config (same entity has rendering data)
        RequireForUpdate<TreeSpawnerConfig>();
        
        // Create entity query for tree counting
        _treeQuery = GetEntityQuery(
            ComponentType.ReadOnly<GlobalTreeInstance>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<GlobalTreeInstanceData>()
        );
        
        // Cache main camera for frustum culling
        _mainCamera = Camera.main;
        
        // Initialize native collections with larger capacity for VR performance
        // Increased from 1000 to 10000 to handle more trees without reallocation
        _batchMatrices = new NativeParallelMultiHashMap<int, Matrix4x4>(10000, Allocator.Persistent);
        _batchesNative = new NativeParallelHashMap<int, TreeBatchNative>(64, Allocator.Persistent);
        _batchKeys = new NativeList<int>(64, Allocator.Persistent);
        _renderMatrixArray = new Matrix4x4[MaxInstancesPerBatch];
    }
    
    protected override void OnStartRunning()
    {
        // ✅ Cache rendering data once to avoid GC allocations every frame
        var configEntity = SystemAPI.GetSingletonEntity<TreeSpawnerConfig>();
        if (EntityManager.HasComponent<GlobalTreeRenderingData>(configEntity))
        {
            _cachedRenderingData = EntityManager.GetComponentData<GlobalTreeRenderingData>(configEntity);
        }
    }
    
    protected override void OnDestroy()
    {
        // Dispose all native batch lists first
        if (_batchesNative.IsCreated)
        {
            foreach (var kvp in _batchesNative)
            {
                if (kvp.Value.matrices.IsCreated)
                    kvp.Value.matrices.Dispose();
            }
            _batchesNative.Dispose();
        }
        
        // Dispose other native collections
        if (_batchMatrices.IsCreated)
            _batchMatrices.Dispose();
        if (_batchKeys.IsCreated)
            _batchKeys.Dispose();
    }

    protected override void OnUpdate()
    {
        _frameCount++; // Increment frame counter for periodic debug logging
        
#if UNITY_EDITOR
        ProfilerMarker.Begin();
        CollectMarker.Begin();
#endif

        // ✅ Use cached rendering data - ZERO GC allocations
        if (_cachedRenderingData == null || _cachedRenderingData.meshes == null || _cachedRenderingData.materials == null)
        {
#if UNITY_EDITOR
            CollectMarker.End();
            ProfilerMarker.End();
#endif
            return;
        }
        
        // Get LOD config once for debug logging (used in multiple places)
        var lodConfig = SystemAPI.GetSingleton<TreeLODConfig>();
        
        // Get player position for distance culling
        float3 playerPosition = float3.zero;
        bool hasPlayerPosition = false;
        
        // Access managed component singleton
        if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef))
        {
            if (playerRef != null && playerRef.playerTransform != null)
            {
                playerPosition = playerRef.playerTransform.position;
                hasPlayerPosition = true;
            }
        }
        
        // Count trees to ensure hashmap has enough capacity
        int treeCount = _treeQuery.CalculateEntityCount();
        
        // Resize hashmap if needed (with 20% buffer for safety)
        int requiredCapacity = (int)(treeCount * 1.2f);
        if (_batchMatrices.Capacity < requiredCapacity)
        {
            _batchMatrices.Dispose();
            _batchMatrices = new NativeParallelMultiHashMap<int, Matrix4x4>(requiredCapacity, Allocator.Persistent);
            
            // Log resize event if debug logging is enabled
            if (lodConfig.enableTreeLODDebug)
            {
                Debug.Log($"[GlobalTreeInstance] Resized hashmap to capacity {requiredCapacity} for {treeCount} trees");
            }
        }

        // Clear native hash map from previous frame
        _batchMatrices.Clear();
        
        // Calculate frustum planes for culling
        bool enableCulling = false;
        NativeArray<float4> frustumPlanesNative;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
        }

        if (_mainCamera != null)
        {
            GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);
            
            // Convert to NativeArray for Burst job
            frustumPlanesNative = new NativeArray<float4>(6, Allocator.TempJob);
            for (int i = 0; i < 6; i++)
            {
                var plane = _frustumPlanes[i];
                frustumPlanesNative[i] = new float4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }
            enableCulling = true;
        }
        else
        {
            // Create empty array if no camera (job requires constructed NativeArray)
            frustumPlanesNative = new NativeArray<float4>(0, Allocator.TempJob);
            
            if (lodConfig.enableTreeLODDebug && _frameCount % 60 == 0)
            {
                Debug.LogWarning("[GlobalTreeInstance] Camera is NULL - frustum culling disabled!");
            }
        }
        
        // Schedule parallel Burst-compiled job to collect tree matrices
        var collectJob = new CollectTreeMatricesJob
        {
            BatchMatrices = _batchMatrices.AsParallelWriter(),
            MeshArrayLength = _cachedRenderingData.meshes.Length,
            MaterialArrayLength = _cachedRenderingData.materials.Length,
            FrustumPlanes = frustumPlanesNative,
            EnableFrustumCulling = enableCulling,
            PlayerPosition = playerPosition,
            MaxRenderDistance = _maxRenderDistance,
            EnableDistanceCulling = hasPlayerPosition
        };
        
        Dependency = collectJob.ScheduleParallel(Dependency);
        
        // Complete matrix collection job before conversion
        Dependency.Complete();
        
        // Dispose temporary frustum planes array
        if (frustumPlanesNative.IsCreated)
            frustumPlanesNative.Dispose();

#if UNITY_EDITOR
        CollectMarker.End();
        ConvertMarker.Begin();
#endif

        // OPTIMIZATION: Convert to native batches on main thread (fast, just organizing data)
        // NOTE: Can't use job due to nested native containers limitation
        ConvertToBatches();

#if UNITY_EDITOR
        ConvertMarker.End();
        DrawMarker.Begin();
#endif

        // Render all batches using Graphics.DrawMeshInstanced
        int totalDrawCalls = 0;
        int totalRendered = 0;
        
        // Iterate through batch keys (native list, no GC)
        for (int i = 0; i < _batchKeys.Length; i++)
        {
            int batchKey = _batchKeys[i];
            if (!_batchesNative.TryGetValue(batchKey, out var batch))
                continue;
            
            if (batch.matrices.Length == 0)
                continue;
            
            // Resolve mesh and material from cached data
            var mesh = _cachedRenderingData.meshes[batch.meshIndex];
            var material = _cachedRenderingData.materials[batch.materialIndex];
            
            if (mesh == null || material == null)
                continue;
            
            // Unity DrawMeshInstanced has a limit of 1023 instances per call
            // If we have more, we need to split into multiple draw calls
            int instanceCount = batch.matrices.Length;
            int offset = 0;
            
            while (offset < instanceCount)
            {
                int count = Mathf.Min(MaxInstancesPerBatch, instanceCount - offset);
                
                // OPTIMIZATION: Use NativeArray slice for zero-copy access
                var matricesSlice = batch.matrices.AsArray().GetSubArray(offset, count);
                
                // Copy to render array (unavoidable - Graphics API requires managed array)
                for (int j = 0; j < count; j++)
                {
                    _renderMatrixArray[j] = matricesSlice[j];
                }
                
                // Draw this batch
                Graphics.DrawMeshInstanced(
                    mesh,
                    0, // submesh index
                    material,
                    _renderMatrixArray,
                    count, // number of instances to render
                    null, // material property block
                    ShadowCastingMode.On,
                    true, // receive shadows
                    0, // layer
                    null // camera (null = all cameras)
                );
                
                totalDrawCalls++;
                totalRendered += count;
                offset += count;
            }
        }

#if UNITY_EDITOR
        DrawMarker.End();
        ProfilerMarker.End();
#endif
        
        // Reduced logging frequency: only every 60 frames (~1 second at 60 FPS)
        if (lodConfig.enableTreeLODDebug && _frameCount % 60 == 0 && totalRendered > 0)
        {
            Debug.Log($"[GlobalTreeInstance] Rendered {totalRendered}/{treeCount} trees in {totalDrawCalls} draw calls " +
                      $"({_batchKeys.Length} unique batches, max distance: {_maxRenderDistance}m)");
        }
    }
}

