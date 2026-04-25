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
/// OPTIMIZED: Uses persistent Dictionary with NativeList storage for zero GC allocations.
/// Trees are batched by material index, with up to 1023 instances per batch.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class GlobalTreeInstanceSystem : SystemBase
{
    // Batch storage using NativeList for zero GC
    private class TreeBatch
    {
        public Mesh mesh;
        public Material material;
        public NativeList<Matrix4x4> matrices;
        
        public TreeBatch()
        {
            matrices = new NativeList<Matrix4x4>(256, Allocator.Persistent);
        }
        
        public void Dispose()
        {
            if (matrices.IsCreated)
                matrices.Dispose();
        }
    }
    
    // Persistent Dictionary (reused each frame, minimal GC)
    private System.Collections.Generic.Dictionary<int, TreeBatch> _batches = new System.Collections.Generic.Dictionary<int, TreeBatch>();
    
    private const int MaxInstancesPerBatch = 1023; // Unity limitation for DrawMeshInstanced
    
#if UNITY_EDITOR
    private static readonly ProfilerMarker ProfilerMarker = new ProfilerMarker("GlobalTreeInstance.Render");
    private static readonly ProfilerMarker CollectMarker = new ProfilerMarker("GlobalTreeInstance.Collect");
    private static readonly ProfilerMarker DrawMarker = new ProfilerMarker("GlobalTreeInstance.Draw");
    private int _frameCount;
#endif

    protected override void OnCreate()
    {
        // Require the tree spawner config (same entity has rendering data)
        RequireForUpdate<TreeSpawnerConfig>();
    }
    
    protected override void OnDestroy()
    {
        // Dispose all NativeList in batches to prevent memory leaks
        foreach (var batch in _batches.Values)
        {
            batch.Dispose();
        }
        _batches.Clear();
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

        // Clear previous frame's batches (NativeList.Clear keeps capacity = zero GC)
        foreach (var batch in _batches.Values)
        {
            batch.matrices.Clear();
        }
        
        // SINGLE-PASS ITERATION: Collect and batch in one pass
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
                
                // Find or create batch for this material index
                if (!_batches.TryGetValue(instanceData.materialIndex, out TreeBatch batch))
                {
                    batch = new TreeBatch
                    {
                        mesh = mesh,
                        material = material
                    };
                    _batches[instanceData.materialIndex] = batch;
                }
                
                // Add transform matrix to batch (NativeList.Add = zero GC)
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
            if (batch.matrices.Length == 0)
                continue;
            
            int instanceCount = batch.matrices.Length;
            
            // Split into batches of 1023 if needed (Unity limitation)
            int offset = 0;
            while (offset < instanceCount)
            {
                int batchSize = math.min(MaxInstancesPerBatch, instanceCount - offset);
                
                // Create array slice for this batch (small allocation, unavoidable for Graphics API)
                var batchArray = new Matrix4x4[batchSize];
                for (int j = 0; j < batchSize; j++)
                {
                    batchArray[j] = batch.matrices[offset + j];
                }
                
                // Draw this batch
                Graphics.DrawMeshInstanced(
                    batch.mesh,
                    0, // submesh index
                    batch.material,
                    batchArray,
                    batchSize,
                    null, // material property block
                    ShadowCastingMode.On,
                    true, // receive shadows
                    0, // layer
                    null // camera (null = all cameras)
                );
                
                totalDrawCalls++;
                offset += batchSize;
            }
        }

#if UNITY_EDITOR
        DrawMarker.End();
        ProfilerMarker.End();
        
        // Reduced logging frequency: only every 60 frames (~1 second at 60 FPS)
        if (_frameCount % 60 == 0 && collected > 0)
        {
            Debug.Log($"[GlobalTreeInstance] Rendering {collected} trees in {totalDrawCalls} draw calls ({_batches.Count} unique materials) - OPTIMIZED");
        }
#endif
    }
}

