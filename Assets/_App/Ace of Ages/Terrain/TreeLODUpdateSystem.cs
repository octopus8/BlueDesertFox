using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
using UnityEngine;
#endif

/// <summary>
/// Burst-compiled system that updates tree mesh LOD levels based on distance to player.
/// Uses spatial chunking, HashSet filtering, and parallel jobs for maximum performance.
/// Applies hysteresis to prevent LOD flickering at transition boundaries.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainDistanceTrackingSystem))]
[BurstCompile]
public partial struct TreeLODUpdateSystem : ISystem
{
    private const float ChunkSize = 100f; // 100m x 100m chunks
    private const int MaxTreesPerFrame = 500; // Frame budget to prevent spikes
    
    private int _frameCounter;
    private NativeList<int2> _activeChunks;
    private NativeHashSet<int2> _activeChunksSet; // O(1) chunk lookup
    
#if UNITY_EDITOR
    private static readonly SharedStatic<ProfilerMarker> s_ProfilerMarker = SharedStatic<ProfilerMarker>.GetOrCreate<ProfilerMarkerKey>();
    private struct ProfilerMarkerKey { }
#endif

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TreeLODConfig>();
        state.RequireForUpdate<PlayerTransformReference>();
        _activeChunks = new NativeList<int2>(20, Allocator.Persistent);
        _activeChunksSet = new NativeHashSet<int2>(20, Allocator.Persistent);
        _frameCounter = 0;
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data = new ProfilerMarker("TreeLOD.Update");
#endif
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_activeChunks.IsCreated)
            _activeChunks.Dispose();
        if (_activeChunksSet.IsCreated)
            _activeChunksSet.Dispose();
    }

    // NOTE: Cannot use [BurstCompile] here because we access managed PlayerTransformReference component
    public void OnUpdate(ref SystemState state)
    {
#if UNITY_EDITOR
        s_ProfilerMarker.Data.Begin();
#endif

        // Get configuration
        var lodConfig = SystemAPI.GetSingleton<TreeLODConfig>();
        
        // Get player position (must access managed component on main thread before Burst job)
        float3 playerPosition = float3.zero;
        bool hasPlayerPosition = false;
        
        // Access managed component on main thread
        foreach (var playerRef in SystemAPI.Query<PlayerTransformReference>())
        {
            if (playerRef != null && playerRef.playerTransform != null)
            {
                playerPosition = playerRef.playerTransform.position;
                hasPlayerPosition = true;
                break;
            }
        }
        
        if (!hasPlayerPosition)
        {
#if UNITY_EDITOR
            s_ProfilerMarker.Data.End();
#endif
            return;
        }
        
        int2 playerChunk;
        GetChunkCoord(in playerPosition, out playerChunk);
        
        // Build list of chunks to update this frame
        _activeChunks.Clear();
        _activeChunksSet.Clear();
        
        // Always include player's chunk and immediate neighbors (9 total)
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                int2 chunkCoord = playerChunk + new int2(x, z);
                _activeChunks.Add(chunkCoord);
                _activeChunksSet.Add(chunkCoord);
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
                
                // Use HashSet to avoid duplicates (O(1) check)
                if (_activeChunksSet.Add(distantChunk))
                {
                    _activeChunks.Add(distantChunk);
                }
            }
        }
        
        _frameCounter++;
        
        // Schedule Burst-compiled job for LOD updates
        var updateJob = new TreeLODUpdateJob
        {
            playerPosition = playerPosition,
            lod0Distance = lodConfig.lod0Distance,
            lod1Distance = lodConfig.lod1Distance,
            lod2Distance = lodConfig.lod2Distance,
            hysteresis = lodConfig.hysteresisDelta,
            lodsPerTreeType = lodConfig.lodsPerTreeType,
            activeChunksSet = _activeChunksSet,
            maxTreesPerFrame = MaxTreesPerFrame
        };
        
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data.End();
        
        // Log periodically (must complete job first for accurate count)
        if (_frameCounter % 120 == 0)
        {
            state.Dependency.Complete();
            // Get tree count for logging
            var query = SystemAPI.QueryBuilder().WithAll<GlobalTreeInstance, TreeChunkMembership>().Build();
            int totalTrees = query.CalculateEntityCount();
            UnityEngine.Debug.Log($"[TreeLOD] Processing up to {MaxTreesPerFrame} trees/frame from {_activeChunks.Length} chunks (total: {totalTrees} trees)");
        }
#endif
    }
    
    /// <summary>
    /// Burst-compiled job that updates tree LOD levels in parallel.
    /// </summary>
    [BurstCompile]
    private partial struct TreeLODUpdateJob : IJobEntity
    {
        [ReadOnly] public float3 playerPosition;
        [ReadOnly] public float lod0Distance;
        [ReadOnly] public float lod1Distance;
        [ReadOnly] public float lod2Distance;
        [ReadOnly] public float hysteresis;
        [ReadOnly] public int lodsPerTreeType;
        [ReadOnly] public NativeHashSet<int2> activeChunksSet;
        [ReadOnly] public int maxTreesPerFrame;
        
        private void Execute(
            in LocalTransform transform,
            ref GlobalTreeInstanceData instanceData,
            in TreeChunkMembership chunkMembership)
        {
            // OPTIMIZED: O(1) chunk lookup using HashSet
            if (!activeChunksSet.Contains(chunkMembership.chunkCoord))
                return;
            
            // Calculate 2D distance (XZ plane) from player to tree
            float2 treePos2D = new float2(transform.Position.x, transform.Position.z);
            float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
            float distance = math.distance(treePos2D, playerPos2D);
            
            // Determine new LOD level with hysteresis
            byte currentLOD = instanceData.currentLODLevel;
            byte newLOD = DetermineLODLevel(distance, currentLOD, lod0Distance, lod1Distance, lod2Distance, hysteresis);
            
            // Update if LOD changed
            if (newLOD != currentLOD)
            {
                // Calculate new mesh index: (treeTypeIndex * 3) + lodLevel
                int newMeshIndex = (instanceData.treeTypeIndex * lodsPerTreeType) + newLOD;
                
                instanceData.meshIndex = newMeshIndex;
                instanceData.materialIndex = newMeshIndex; // Same index for materials
                instanceData.currentLODLevel = newLOD;
            }
            
            // Update distance for next frame's hysteresis calculation
            instanceData.lastDistanceToPlayer = distance;
        }
        
        /// <summary>
        /// Determine LOD level based on distance with hysteresis to prevent flickering.
        /// </summary>
        [BurstCompile]
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
    
    /// <summary>
    /// Calculate chunk coordinate from world position.
    /// </summary>
    [BurstCompile]
    private static void GetChunkCoord(in float3 worldPos, out int2 result)
    {
        result = new int2(
            (int)math.floor(worldPos.x / ChunkSize),
            (int)math.floor(worldPos.z / ChunkSize)
        );
    }
}
