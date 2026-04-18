using Unity.Entities;
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
    
    // Batch data for rendering
    private class TreeBatch
    {
        public UnityEngine.Mesh mesh;
        public UnityEngine.Material material;
        public System.Collections.Generic.List<Matrix4x4> matrices = new System.Collections.Generic.List<Matrix4x4>(256);
    }
    
    private System.Collections.Generic.Dictionary<BatchKey, TreeBatch> _batches = new System.Collections.Generic.Dictionary<BatchKey, TreeBatch>();
    private System.Collections.Generic.List<Matrix4x4> _tempMatrixArray = new System.Collections.Generic.List<Matrix4x4>(1023);
    private const int MaxInstancesPerBatch = 1023; // Unity limitation for DrawMeshInstanced
    
#if UNITY_EDITOR
    private static readonly ProfilerMarker ProfilerMarker = new ProfilerMarker("GlobalTreeInstance.Render");
    private static readonly ProfilerMarker CollectMarker = new ProfilerMarker("GlobalTreeInstance.Collect");
    private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("GlobalTreeInstance.Draw");
    private int _lastTreeCount;
    private int _lastBatchCount;
#endif

    protected override void OnCreate()
    {
        // Don't use RequireForUpdate - we want to run even if no trees yet
        // so we can detect when trees are spawned
    }

    protected override void OnUpdate()
    {
#if UNITY_EDITOR
        ProfilerMarker.Begin();
        CollectMarker.Begin();
#endif

        // Debug: Check if system is running
        int treeCount = 0;
        foreach (var entity in SystemAPI.Query<RefRO<GlobalTreeInstance>>().WithEntityAccess())
        {
            treeCount++;
        }
        
#if UNITY_EDITOR
        if (treeCount == 0)
        {
            Debug.Log("[GlobalTreeInstance] No trees with GlobalTreeInstance tag found");
            CollectMarker.End();
            ProfilerMarker.End();
            return;
        }
        else
        {
            Debug.Log($"[GlobalTreeInstance] Found {treeCount} trees with GlobalTreeInstance tag");
        }
#endif

        // Clear previous frame's batches
        foreach (var batch in _batches.Values)
        {
            batch.matrices.Clear();
        }
        
        // Collect all tree entities and group by mesh/material
        // Use EntityManager to query managed components
        int skippedNoData = 0;
        int skippedNullMesh = 0;
        int collected = 0;
        
        Entities
            .WithAll<GlobalTreeInstance>()
            .WithNone<Unity.Rendering.DisableRendering>()
            .ForEach((Entity entity, in LocalTransform localTransform) =>
            {
                // Get managed component data
                if (!EntityManager.HasComponent<GlobalTreeInstanceData>(entity))
                {
                    skippedNoData++;
                    return;
                }
                
                var instanceData = EntityManager.GetComponentData<GlobalTreeInstanceData>(entity);
                
                if (instanceData.mesh == null || instanceData.material == null)
                {
                    skippedNullMesh++;
                    return;
                }
                
                collected++;
                
                var batchKey = new BatchKey { mesh = instanceData.mesh, material = instanceData.material };
                
                // Find or create batch for this mesh/material combination
                if (!_batches.TryGetValue(batchKey, out TreeBatch batch))
                {
                    batch = new TreeBatch
                    {
                        mesh = instanceData.mesh,
                        material = instanceData.material
                    };
                    _batches[batchKey] = batch;
                }
                
                // Add transform matrix to batch
                Matrix4x4 matrix = Matrix4x4.TRS(
                    localTransform.Position,
                    localTransform.Rotation,
                    new Vector3(localTransform.Scale, localTransform.Scale, localTransform.Scale)
                );
                batch.matrices.Add(matrix);
            }).WithoutBurst().Run();

#if UNITY_EDITOR
        Debug.Log($"[GlobalTreeInstance] Collection results: Collected={collected}, SkippedNoData={skippedNoData}, SkippedNullMesh={skippedNullMesh}");
#endif

#if UNITY_EDITOR
        CollectMarker.End();
        DrawMarker.Begin();
#endif

        // Render all batches using Graphics.DrawMeshInstanced
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
                
                // Copy matrices to temporary array for this batch
                _tempMatrixArray.Clear();
                for (int i = 0; i < count; i++)
                {
                    _tempMatrixArray.Add(batch.matrices[offset + i]);
                }
                
                // Draw this batch
                Graphics.DrawMeshInstanced(
                    batch.mesh,
                    0, // submesh index
                    batch.material,
                    _tempMatrixArray,
                    null, // material property block
                    ShadowCastingMode.On,
                    true, // receive shadows
                    0, // layer
                    null // camera (null = all cameras)
                );
                
                offset += count;
            }
        }

#if UNITY_EDITOR
        DrawMarker.End();
        ProfilerMarker.End();
        
        // Debug output (only when counts change)
        int totalTrees = 0;
        int totalBatches = 0;
        foreach (var batch in _batches.Values)
        {
            if (batch.matrices.Count > 0)
            {
                totalTrees += batch.matrices.Count;
                totalBatches += (batch.matrices.Count + MaxInstancesPerBatch - 1) / MaxInstancesPerBatch;
            }
        }
        
        if (totalTrees != _lastTreeCount || totalBatches != _lastBatchCount)
        {
            Debug.Log($"[GlobalTreeInstance] Rendering {totalTrees} trees in {totalBatches} draw calls ({_batches.Count} unique mesh/material combinations)");
            _lastTreeCount = totalTrees;
            _lastBatchCount = totalBatches;
        }
#endif
    }
}




