using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
/// OPTIMIZED: Uses Burst-compiled parallel jobs for collecting tree matrices across multiple CPU cores.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GlobalTreeInstanceSystem : SystemBase
{
    // Batch key for grouping trees by mesh/material
    private struct BatchKey : System.IEquatable<BatchKey>
    {
        public UnityEngine.Mesh mesh;
        public UnityEngine.Material material;
        
        public bool Equals(BatchKey other)
        {
            return mesh == other.mesh && material == other.material;
        }
        
        public override int GetHashCode()
        {
            return (mesh != null ? mesh.GetHashCode() : 0) ^ (material != null ? material.GetHashCode() : 0);
        }
    }
    
    /// <summary>
    /// Burst-compiled parallel job that collects tree transform matrices into batches.
    /// Processes thousands of trees across multiple CPU cores for maximum performance.
    /// OPTIMIZED: Includes frustum culling to skip trees outside camera view.
    /// </summary>
    [BurstCompile]
    private partial struct CollectTreeMatricesJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Matrix4x4>.ParallelWriter BatchMatrices;
        public int MeshArrayLength;
        public int MaterialArrayLength;
        
        [ReadOnly] public NativeArray<float4> FrustumPlanes;
        public bool EnableFrustumCulling;
        
        private void Execute(in LocalTransform transform, in GlobalTreeInstanceData instanceData)
        {
            // Validate indices
            if (instanceData.meshIndex < 0 || instanceData.meshIndex >= MeshArrayLength ||
                instanceData.materialIndex < 0 || instanceData.materialIndex >= MaterialArrayLength)
                return;
            
            // Frustum culling: Test if tree is visible
            if (EnableFrustumCulling && FrustumPlanes.Length == 6)
            {
                float3 treePos = transform.Position;
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
                transform.Position,
                transform.Rotation,
                new Vector3(transform.Scale, transform.Scale, transform.Scale)
            );
            
            BatchMatrices.Add(batchKey, matrix);
        }
    }
    
    // Batch data for rendering
    private class TreeBatch
    {
        public UnityEngine.Mesh mesh;
        public UnityEngine.Material material;
        public System.Collections.Generic.List<Matrix4x4> matrices = new System.Collections.Generic.List<Matrix4x4>(256);
    }
    
    private NativeParallelMultiHashMap<int, Matrix4x4> _batchMatrices;
    private System.Collections.Generic.Dictionary<BatchKey, TreeBatch> _batches;
    private Matrix4x4[] _renderMatrixArray;
    private EntityQuery _treeQuery;
    private Plane[] _frustumPlanes = new Plane[6];
    private Camera _mainCamera;
    private const int MaxInstancesPerBatch = 1023; // Unity limitation for DrawMeshInstanced
    
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
        
        // Initialize native collections with larger capacity
        // Increased from 1000 to 10000 to handle more trees
        _batchMatrices = new NativeParallelMultiHashMap<int, Matrix4x4>(10000, Allocator.Persistent);
        _batches = new System.Collections.Generic.Dictionary<BatchKey, TreeBatch>();
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
        // Dispose native collections
        if (_batchMatrices.IsCreated)
            _batchMatrices.Dispose();
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
        }
        
        // Schedule parallel Burst-compiled job to collect tree matrices
        var collectJob = new CollectTreeMatricesJob
        {
            BatchMatrices = _batchMatrices.AsParallelWriter(),
            MeshArrayLength = _cachedRenderingData.meshes.Length,
            MaterialArrayLength = _cachedRenderingData.materials.Length,
            FrustumPlanes = frustumPlanesNative,
            EnableFrustumCulling = enableCulling
        };
        
        Dependency = collectJob.ScheduleParallel(Dependency);
        
        // Complete job before accessing results
        Dependency.Complete();
        
        // Dispose temporary frustum planes array
        if (frustumPlanesNative.IsCreated)
            frustumPlanesNative.Dispose();

#if UNITY_EDITOR
        CollectMarker.End();
        ConvertMarker.Begin();
#endif

        // Convert NativeMultiHashMap to rendering batches
        // Clear previous frame's batches
        foreach (var batch in _batches.Values)
        {
            batch.matrices.Clear();
        }
        
        // Get all unique batch keys
        var batchKeys = _batchMatrices.GetKeyArray(Allocator.Temp);
        var uniqueKeys = new NativeHashSet<int>(batchKeys.Length, Allocator.Temp);
        
        foreach (var key in batchKeys)
        {
            uniqueKeys.Add(key);
        }
        
        int collected = 0;
        
        // For each unique batch key, resolve mesh/material and collect matrices
        foreach (var batchKey in uniqueKeys)
        {
            // Extract mesh and material indices from batch key
            int meshIndex = batchKey / 1000;
            int materialIndex = batchKey % 1000;
            
            var mesh = _cachedRenderingData.meshes[meshIndex];
            var material = _cachedRenderingData.materials[materialIndex];
            
            if (mesh == null || material == null)
                continue;
            
            var key = new BatchKey { mesh = mesh, material = material };
            
            // Find or create batch
            if (!_batches.TryGetValue(key, out TreeBatch batch))
            {
                batch = new TreeBatch
                {
                    mesh = mesh,
                    material = material
                };
                _batches[key] = batch;
            }
            
            // Collect all matrices for this batch key
            if (_batchMatrices.TryGetFirstValue(batchKey, out var matrix, out var iterator))
            {
                do
                {
                    batch.matrices.Add(matrix);
                    collected++;
                }
                while (_batchMatrices.TryGetNextValue(out matrix, ref iterator));
            }
        }
        
        batchKeys.Dispose();
        uniqueKeys.Dispose();

#if UNITY_EDITOR
        ConvertMarker.End();
        DrawMarker.Begin();
#endif

        // Render all batches using Graphics.DrawMeshInstanced
        int totalDrawCalls = 0;
        foreach (var batch in _batches.Values)
        {
            if (batch.matrices.Count == 0)
                continue;
            
            // Unity DrawMeshInstanced has a limit of 1023 instances per call
            // If we have more, we need to split into multiple draw calls
            int instanceCount = batch.matrices.Count;
            int offset = 0;
            
            while (offset < instanceCount)
            {
                int count = Mathf.Min(MaxInstancesPerBatch, instanceCount - offset);
                
                // Copy matrices to pre-allocated array
                for (int i = 0; i < count; i++)
                {
                    _renderMatrixArray[i] = batch.matrices[offset + i];
                }
                
                // Draw this batch
                Graphics.DrawMeshInstanced(
                    batch.mesh,
                    0, // submesh index
                    batch.material,
                    _renderMatrixArray,
                    count, // number of instances to render
                    null, // material property block
                    ShadowCastingMode.On,
                    true, // receive shadows
                    0, // layer
                    null // camera (null = all cameras)
                );
                
                totalDrawCalls++;
                offset += count;
            }
        }

#if UNITY_EDITOR
        DrawMarker.End();
        ProfilerMarker.End();
#endif
        
        // Reduced logging frequency: only every 60 frames (~1 second at 60 FPS)
        if (lodConfig.enableTreeLODDebug && _frameCount % 60 == 0 && collected > 0)
        {
            Debug.Log($"[GlobalTreeInstance] Rendering {collected} trees in {totalDrawCalls} draw calls ({_batches.Count} unique mesh/material combinations)");
        }
    }
}

