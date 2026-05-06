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
/// VR OPTIMIZED: Runs every N frames on mobile VR platforms to reduce CPU load.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainDistanceTrackingSystem))]
[BurstCompile]
public partial struct TreeLODUpdateSystem : ISystem
{
    private const float ChunkSize = 100f; // 100m x 100m chunks
    private const int MaxTreesPerFrame = 500; // Frame budget to prevent spikes
    
    // VR optimization: Skip frames on mobile platforms
    private const int VRFrameSkip = 2; // Update every 2-3 frames on Quest 3
    
    // OPTIMIZED v3.0: Velocity-aware throttling
    private float3 _lastPlayerPosition;
    private float _lastDeltaTime;
    
    private int _frameCounter;
    private NativeList<int2> _activeChunks;
    private NativeHashSet<int2> _activeChunksSet; // O(1) chunk lookup
    
#if UNITY_EDITOR
    private static readonly SharedStatic<ProfilerMarker> s_ProfilerMarker = SharedStatic<ProfilerMarker>.GetOrCreate<ProfilerMarkerKey>();
    private static readonly SharedStatic<ProfilerMarker> s_VelocityCalcMarker = SharedStatic<ProfilerMarker>.GetOrCreate<VelocityMarkerKey>();
    private static readonly SharedStatic<ProfilerMarker> s_ChunkFilterMarker = SharedStatic<ProfilerMarker>.GetOrCreate<ChunkFilterMarkerKey>();
    private struct ProfilerMarkerKey { }
    private struct VelocityMarkerKey { }
    private struct ChunkFilterMarkerKey { }
#endif

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TreeLODConfig>();
        state.RequireForUpdate<PlayerTransformReference>();
        _activeChunks = new NativeList<int2>(20, Allocator.Persistent);
        _activeChunksSet = new NativeHashSet<int2>(20, Allocator.Persistent);
        _frameCounter = 0;
        
        // OPTIMIZED v3.0: Initialize velocity tracking
        _lastPlayerPosition = float3.zero;
        _lastDeltaTime = 0f;
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data = new ProfilerMarker("TreeLOD.Update");
        s_VelocityCalcMarker.Data = new ProfilerMarker("TreeLOD.VelocityCalc");
        s_ChunkFilterMarker.Data = new ProfilerMarker("TreeLOD.ChunkFilter");
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
        _frameCounter++;
        
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
            return;
        }
        
#if UNITY_EDITOR
        s_VelocityCalcMarker.Data.Begin();
#endif
        
        // OPTIMIZED v3.0: Calculate velocity for adaptive throttling
        float velocity = 0f;
        if (_lastDeltaTime > 0)
        {
            velocity = math.length(playerPosition - _lastPlayerPosition) / _lastDeltaTime;
        }
        
        // Determine frame skip based on velocity
        int effectiveFrameSkip = velocity > lodConfig.playerVelocityThreshold 
            ? lodConfig.vrFrameSkipScrolling 
            : VRFrameSkip;
        
#if UNITY_EDITOR
        s_VelocityCalcMarker.Data.End();
#endif
        
        // VR OPTIMIZATION: Adaptive frame skip based on player velocity
        if (_frameCounter % effectiveFrameSkip != 0)
        {
            return;
        }
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data.Begin();
        s_ChunkFilterMarker.Data.Begin();
#endif

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
        
#if UNITY_EDITOR
        s_ChunkFilterMarker.Data.End();
#endif
        
        // Schedule Burst-compiled job for LOD updates with distance-tiered filtering
        var updateJob = new TreeLODUpdateJob
        {
            playerPosition = playerPosition,
            lod0Distance = lodConfig.lod0Distance,
            lod1Distance = lodConfig.lod1Distance,
            lod2Distance = lodConfig.lod2Distance,
            hysteresis = lodConfig.hysteresisDelta,
            lodsPerTreeType = lodConfig.lodsPerTreeType,
            activeChunksSet = _activeChunksSet,
            maxTreesPerFrame = MaxTreesPerFrame,
            frameCounter = _frameCounter // Pass frame counter for distance-tiered updates
        };
        
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data.End();
#endif
        
        // Update velocity tracking for next frame
        _lastPlayerPosition = playerPosition;
        _lastDeltaTime = SystemAPI.Time.DeltaTime;
        
        // Log periodically (must complete job first for accurate count)
        if (lodConfig.enableTreeLODDebug && _frameCounter % 120 == 0)
        {
            state.Dependency.Complete();
            // Get tree count for logging
            var query = SystemAPI.QueryBuilder().WithAll<GlobalTreeInstance, TreeChunkMembership>().Build();
            int totalTrees = query.CalculateEntityCount();
            UnityEngine.Debug.Log($"[TreeLOD] Velocity: {velocity:F2} m/s, FrameSkip: {effectiveFrameSkip}, Processing {_activeChunks.Length} chunks (total: {totalTrees} trees)");
        }
    }
    
    /// <summary>
    /// Burst-compiled job that updates tree LOD levels in parallel.
    /// OPTIMIZED v3.0: Added distance-tiered updates (4 tiers: 0-100m, 100-200m, 200-300m, 300m+).
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
        [ReadOnly] public int frameCounter; // For distance-tiered updates
        
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
            
            // OPTIMIZED v3.0: Distance-tiered updates (4 tiers)
            // Near trees (0-100m): Update every frame
            // Mid trees (100-200m): Update every 2 frames
            // Far trees (200-300m): Update every 4 frames
            // Very far trees (300m+): Update every 8 frames
            if (distance > 300f && frameCounter % 8 != 0)
                return;
            if (distance > 200f && frameCounter % 4 != 0)
                return;
            if (distance > 100f && frameCounter % 2 != 0)
                return;
            
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
