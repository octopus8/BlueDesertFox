using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that updates tree mesh LOD levels based on distance to player.
/// Uses spatial chunking and frame budgeting to process updates efficiently.
/// Applies hysteresis to prevent LOD flickering at transition boundaries.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainDistanceTrackingSystem))]
public partial class TreeLODUpdateSystem : SystemBase
{
    private const float ChunkSize = 100f; // 100m x 100m chunks
    private int _frameCounter;
    private NativeList<int2> _activeChunks;
    
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TreeLOD.Update");
    private int _treesUpdatedThisFrame;
    private int _lodTransitionsThisFrame;
#endif

    protected override void OnCreate()
    {
        RequireForUpdate<TreeLODConfig>();
        RequireForUpdate<PlayerTransformReference>();
        _activeChunks = new NativeList<int2>(100, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (_activeChunks.IsCreated)
            _activeChunks.Dispose();
    }

    protected override void OnUpdate()
    {
#if UNITY_EDITOR
        s_ProfilerMarker.Begin();
        _treesUpdatedThisFrame = 0;
        _lodTransitionsThisFrame = 0;
#endif

        // Get configuration
        var lodConfig = SystemAPI.GetSingleton<TreeLODConfig>();
        
        // Get player position
        if (!SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) ||
            playerRef == null || 
            playerRef.playerTransform == null)
        {
#if UNITY_EDITOR
            s_ProfilerMarker.End();
#endif
            return;
        }
        
        float3 playerPosition = playerRef.playerTransform.position;
        int2 playerChunk = GetChunkCoord(playerPosition);
        
        // Build list of chunks to update this frame
        _activeChunks.Clear();
        
        // Always include player's chunk and immediate neighbors
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                _activeChunks.Add(playerChunk + new int2(x, z));
            }
        }
        
        // Add rotating chunks based on frame counter for full coverage
        int extraChunksNeeded = math.max(0, lodConfig.maxChunksUpdatedPerFrame - _activeChunks.Length);
        if (extraChunksNeeded > 0)
        {
            // Use frame counter to rotate through distant chunks
            int offset = _frameCounter % 100;
            for (int i = 0; i < extraChunksNeeded; i++)
            {
                int angle = (offset + i * 8) % 360;
                float rad = math.radians(angle);
                int2 distantChunk = playerChunk + new int2(
                    (int)(math.cos(rad) * 3),
                    (int)(math.sin(rad) * 3)
                );
                
                // Avoid adding duplicates
                bool alreadyAdded = false;
                for (int j = 0; j < _activeChunks.Length; j++)
                {
                    if (math.all(_activeChunks[j] == distantChunk))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }
                
                if (!alreadyAdded)
                {
                    _activeChunks.Add(distantChunk);
                }
            }
        }
        
        _frameCounter++;
        
        // Capture variables for job
        var activeChunks = _activeChunks.AsArray();
        var playerPos = playerPosition;
        var lod0Dist = lodConfig.lod0Distance;
        var lod1Dist = lodConfig.lod1Distance;
        var lod2Dist = lodConfig.lod2Distance;
        var hysteresis = lodConfig.hysteresisDelta;
        var lodsPerType = lodConfig.lodsPerTreeType;
        
#if UNITY_EDITOR
        int treesUpdated = 0;
        int lodTransitions = 0;
#endif
        
        // Update trees in selected chunks
        foreach (var (transform, instanceData, chunkMembership, entity) in 
                 SystemAPI.Query<RefRO<LocalTransform>, RefRW<GlobalTreeInstanceData>, RefRO<TreeChunkMembership>>()
                     .WithAll<GlobalTreeInstance>()
                     .WithEntityAccess())
        {
            // Check if this tree's chunk should be updated this frame
            bool shouldUpdate = false;
            for (int i = 0; i < activeChunks.Length; i++)
            {
                if (math.all(chunkMembership.ValueRO.chunkCoord == activeChunks[i]))
                {
                    shouldUpdate = true;
                    break;
                }
            }
            
            if (!shouldUpdate)
                continue;
            
            // Calculate 2D distance (XZ plane) from player to tree
            float2 treePos2D = new float2(transform.ValueRO.Position.x, transform.ValueRO.Position.z);
            float2 playerPos2D = new float2(playerPos.x, playerPos.z );
            float distance = math.distance(treePos2D, playerPos2D);
            
            // Determine new LOD level with hysteresis
            byte currentLOD = instanceData.ValueRO.currentLODLevel;
            byte newLOD = DetermineLODLevel(distance, currentLOD, lod0Dist, lod1Dist, lod2Dist, hysteresis);
            
            // Update if LOD changed
            if (newLOD != currentLOD)
            {
                // Calculate new mesh index: (treeTypeIndex * 3) + lodLevel
                int newMeshIndex = (instanceData.ValueRO.treeTypeIndex * lodsPerType) + newLOD;
                
                instanceData.ValueRW.meshIndex = newMeshIndex;
                instanceData.ValueRW.materialIndex = newMeshIndex; // Same index for materials
                instanceData.ValueRW.currentLODLevel = newLOD;
                
#if UNITY_EDITOR
                lodTransitions++;
#endif
            }
            
            // Update distance for next frame's hysteresis calculation
            instanceData.ValueRW.lastDistanceToPlayer = distance;
            
#if UNITY_EDITOR
            treesUpdated++;
#endif
        }
        
#if UNITY_EDITOR
        _treesUpdatedThisFrame = treesUpdated;
        _lodTransitionsThisFrame = lodTransitions;
        s_ProfilerMarker.End();
        
        // Log periodically
        if (_frameCounter % 120 == 0 && treesUpdated > 0)
        {
            Debug.Log($"[TreeLOD] Updated {treesUpdated} trees, {lodTransitions} LOD transitions, {activeChunks.Length} chunks processed");
        }
#endif
    }
    
    /// <summary>
    /// Calculate chunk coordinate from world position.
    /// </summary>
    private static int2 GetChunkCoord(float3 worldPos)
    {
        return new int2(
            (int)math.floor(worldPos.x / ChunkSize),
            (int)math.floor(worldPos.z / ChunkSize)
        );
    }
    
    /// <summary>
    /// Determine LOD level based on distance with hysteresis to prevent flickering.
    /// </summary>
    private static byte DetermineLODLevel(float distance, byte currentLOD, float lod0Dist, float lod1Dist, float lod2Dist, float hysteresis)
    {
        // LOD0 (highest detail) -> LOD1 transition
        if (distance < lod0Dist)
        {
            // Within LOD0 range, or transitioning down from LOD1
            if (currentLOD == 0)
                return 0; // Stay in LOD0
            else
                // Transitioning down: apply negative hysteresis (more strict to transition down)
                return distance < (lod0Dist - hysteresis) ? (byte)0 : currentLOD;
        }
        
        // LOD1 (medium detail) range
        if (distance < lod1Dist)
        {
            // Within LOD1 range
            if (currentLOD == 1)
                return 1; // Stay in LOD1
            else if (currentLOD == 0)
                // Transitioning up from LOD0: apply positive hysteresis (less strict to transition up)
                return distance > (lod0Dist + hysteresis) ? (byte)1 : (byte)0;
            else // currentLOD == 2
                // Transitioning down from LOD2: apply negative hysteresis
                return distance < (lod1Dist - hysteresis) ? (byte)1 : (byte)2;
        }
        
        // LOD2 (lowest detail) range
        if (distance < lod2Dist)
        {
            // Within LOD2 range, or transitioning up from LOD1
            if (currentLOD == 2)
                return 2; // Stay in LOD2
            else
                // Transitioning up: apply positive hysteresis
                return distance > (lod1Dist + hysteresis) ? (byte)2 : currentLOD;
        }
        
        // Beyond LOD2 range - could add culling here in future
        return 2;
    }
}


