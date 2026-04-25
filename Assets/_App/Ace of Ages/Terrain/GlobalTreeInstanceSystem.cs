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
    private int _frameCount;
#endif

    protected override void OnCreate()
    {
        // Require the tree spawner config (same entity has rendering data)
        RequireForUpdate<TreeSpawnerConfig>();
    }

    protected override void OnUpdate()
    {
#if UNITY_EDITOR
        ProfilerMarker.Begin();
        CollectMarker.Begin();
        _frameCount++;
#endif

        // Get singleton rendering data (ONE lookup instead of thousands)
        var configEntity = SystemAPI.GetSingletonEntity<TreeSpawnerConfig>();
        
        // Check if GlobalTreeRenderingData exists
        if (!EntityManager.HasComponent<GlobalTreeRenderingData>(configEntity))
        {
#if UNITY_EDITOR
            CollectMarker.End();
            ProfilerMarker.End();
#endif
            return;
        }
        
        var renderingData = EntityManager.GetComponentData<GlobalTreeRenderingData>(configEntity);
        
        if (renderingData == null || renderingData.meshes == null || renderingData.materials == null)
        {
#if UNITY_EDITOR
            CollectMarker.End();
            ProfilerMarker.End();
#endif
            return;
        }

        // Clear previous frame's batches
        foreach (var batch in _batches.Values)
        {
            batch.matrices.Clear();
        }
        
        // OPTIMIZED: Use index-based lookup with unmanaged component data
        int collected = 0;
        
        Entities
            .WithAll<GlobalTreeInstance>()
            .WithNone<Unity.Rendering.DisableRendering>()
            .ForEach((Entity entity, in LocalTransform localTransform, in GlobalTreeInstanceData instanceData) =>
            {
                // Validate indices
                if (instanceData.meshIndex < 0 || instanceData.meshIndex >= renderingData.meshes.Length ||
                    instanceData.materialIndex < 0 || instanceData.materialIndex >= renderingData.materials.Length)
                    return;
                
                var mesh = renderingData.meshes[instanceData.meshIndex];
                var material = renderingData.materials[instanceData.materialIndex];
                
                if (mesh == null || material == null)
                    return;
                
                var batchKey = new BatchKey { mesh = mesh, material = material };
                
                // Find or create batch for this mesh/material combination
                if (!_batches.TryGetValue(batchKey, out TreeBatch batch))
                {
                    batch = new TreeBatch
                    {
                        mesh = mesh,
                        material = material
                    };
                    _batches[batchKey] = batch;
                }
                
                // Add transform matrix to batch
                batch.matrices.Add(Matrix4x4.TRS(
                    localTransform.Position,
                    localTransform.Rotation,
                    new Vector3(localTransform.Scale, localTransform.Scale, localTransform.Scale)
                ));
                
                collected++;
            }).WithoutBurst().Run();

#if UNITY_EDITOR
        CollectMarker.End();
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
                
                totalDrawCalls++;
                offset += count;
            }
        }

#if UNITY_EDITOR
        DrawMarker.End();
        ProfilerMarker.End();
        
        // Reduced logging frequency: only every 60 frames (~1 second at 60 FPS)
        if (_frameCount % 60 == 0 && collected > 0)
        {
            Debug.Log($"[GlobalTreeInstance] Rendering {collected} trees in {totalDrawCalls} draw calls ({_batches.Count} unique mesh/material combinations)");
        }
#endif
    }
}

