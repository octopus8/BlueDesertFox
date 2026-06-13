#if UNITY_EDITOR
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Debug visualization system for tree LOD levels in Scene view.
/// Shows LOD levels with color-coded spheres and chunk boundaries.
/// Only active in Unity Editor.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class TreeLODDebugSystem : SystemBase
{
    /// <summary>
    /// Toggle to enable/disable visualization. Set to true to see LOD debug gizmos in Scene view.
    /// </summary>
    public static bool EnableVisualization = false;
    
    private const float ChunkSize = 100f;
    private NativeList<float3> _lod0Positions;
    private NativeList<float3> _lod1Positions;
    private NativeList<float3> _lod2Positions;
    private NativeHashSet<int2> _activeChunks;

    /// <summary>Registers <see cref="StaticObjectLODConfig"/> requirement and allocates persistent native collections for per-LOD position lists and chunk tracking.</summary>
    protected override void OnCreate()
    {
        RequireForUpdate<StaticObjectLODConfig>();
        _lod0Positions = new NativeList<float3>(1000, Allocator.Persistent);
        _lod1Positions = new NativeList<float3>(1000, Allocator.Persistent);
        _lod2Positions = new NativeList<float3>(1000, Allocator.Persistent);
        _activeChunks = new NativeHashSet<int2>(100, Allocator.Persistent);
    }

    /// <summary>Disposes all persistent native collections allocated in <see cref="OnCreate"/>.</summary>
    protected override void OnDestroy()
    {
        if (_lod0Positions.IsCreated) _lod0Positions.Dispose();
        if (_lod1Positions.IsCreated) _lod1Positions.Dispose();
        if (_lod2Positions.IsCreated) _lod2Positions.Dispose();
        if (_activeChunks.IsCreated) _activeChunks.Dispose();
    }

    /// <summary>
    /// When <see cref="EnableVisualization"/> is true, collects all tree world positions into per-LOD
    /// lists and records active spatial chunks for <see cref="OnDrawGizmos"/> to render.
    /// </summary>
    protected override void OnUpdate()
    {
        if (!EnableVisualization)
            return;
        
        // Clear previous frame data
        _lod0Positions.Clear();
        _lod1Positions.Clear();
        _lod2Positions.Clear();
        _activeChunks.Clear();
        
        // Collect tree positions by LOD level
        foreach (var (transform, instanceData, chunkMembership) in 
                 SystemAPI.Query<RefRO<LocalTransform>, RefRO<GlobalStaticObjectInstanceData>, RefRO<StaticObjectChunkMembership>>()
                     .WithAll<GlobalStaticObjectInstance>())
        {
            float3 pos = transform.ValueRO.Position;
            byte lod = instanceData.ValueRO.currentLODLevel;
            
            switch (lod)
            {
                case 0:
                    _lod0Positions.Add(pos);
                    break;
                case 1:
                    _lod1Positions.Add(pos);
                    break;
                case 2:
                    _lod2Positions.Add(pos);
                    break;
            }
            
            _activeChunks.Add(chunkMembership.ValueRO.chunkCoord);
        }
    }
    
    /// <summary>Renders color-coded gizmo spheres for each tree LOD level and draws active spatial chunk boundaries in the Scene view.</summary>
    void OnDrawGizmos()
    {
        if (!EnableVisualization || !_lod0Positions.IsCreated)
            return;
        
        // Draw LOD0 trees (green)
        Gizmos.color = Color.green;
        for (int i = 0; i < _lod0Positions.Length; i++)
        {
            Gizmos.DrawWireSphere(_lod0Positions[i], 1f);
        }
        
        // Draw LOD1 trees (yellow)
        Gizmos.color = Color.yellow;
        for (int i = 0; i < _lod1Positions.Length; i++)
        {
            Gizmos.DrawWireSphere(_lod1Positions[i], 2f);
        }
        
        // Draw LOD2 trees (orange)
        Gizmos.color = new Color(1f, 0.5f, 0f); // Orange
        for (int i = 0; i < _lod2Positions.Length; i++)
        {
            Gizmos.DrawWireSphere(_lod2Positions[i], 3f);
        }
        
        // Draw chunk boundaries (gray)
        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        var chunkArray = _activeChunks.ToNativeArray(Allocator.Temp);
        for (int i = 0; i < chunkArray.Length; i++)
        {
            int2 chunk = chunkArray[i];
            Vector3 chunkCenter = new Vector3(
                chunk.x * ChunkSize + ChunkSize * 0.5f,
                0f,
                chunk.y * ChunkSize + ChunkSize * 0.5f
            );
            Vector3 chunkSize = new Vector3(ChunkSize, 0f, ChunkSize);
            Gizmos.DrawWireCube(chunkCenter, chunkSize);
        }
        chunkArray.Dispose();
    }
}
#endif


