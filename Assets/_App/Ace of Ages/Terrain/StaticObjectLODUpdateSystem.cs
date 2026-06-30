using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
using UnityEngine;
#endif

/// <summary>
/// Burst-compiled system that updates static object mesh LOD levels based on distance to player.
/// Uses spatial chunking, HashSet filtering, and parallel jobs for maximum performance.
/// Applies hysteresis to prevent LOD flickering at transition boundaries.
/// VR OPTIMIZED: Runs every N frames on mobile VR platforms to reduce CPU load.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(objectPositionUpdateSystem))]
[UpdateAfter(typeof(TreeSpatialChunkingSystem))]
[BurstCompile]
public partial struct StaticObjectLODUpdateSystem : ISystem
{
    private const int MaxStaticObjectsPerFrame = 500; // Frame budget to prevent spikes
    
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

    /// <summary>
    /// Registers required singletons, allocates persistent native lists for active chunk tracking,
    /// resets the frame counter and velocity state, and initialises Profiler markers in Editor builds.
    /// </summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectLODConfig>();
        state.RequireForUpdate<StaticObjectLODMeshInfoReady>();
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<CameraDataSingleton>();
        _activeChunks = new NativeList<int2>(512, Allocator.Persistent);
        _activeChunksSet = new NativeHashSet<int2>(512, Allocator.Persistent);
        _frameCounter = 0;
        
        // OPTIMIZED v3.0: Initialize velocity tracking
        _lastPlayerPosition = float3.zero;
        _lastDeltaTime = 0f;
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data = new ProfilerMarker("StaticObjectLOD.Update");
        s_VelocityCalcMarker.Data = new ProfilerMarker("StaticObjectLOD.VelocityCalc");
        s_ChunkFilterMarker.Data = new ProfilerMarker("StaticObjectLOD.ChunkFilter");
#endif
    }

    /// <summary>Disposes persistent native lists used for chunk tracking.</summary>
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_activeChunks.IsCreated)
            _activeChunks.Dispose();
        if (_activeChunksSet.IsCreated)
            _activeChunksSet.Dispose();
    }

    // NOTE: Cannot use [BurstCompile] here because we access managed PlayerTransformReference component
    /// <summary>
    /// Reads the player world position (main thread), determines which spatial chunks are within
    /// view distance, then schedules <c>StaticObjectLODUpdateJob</c> in parallel to apply the correct
    /// <see cref="MaterialMeshInfo"/> LOD slot to each static object based on its distance to the player.
    /// Distance-tiered frame-budget skipping is applied for static objects far from the camera.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        _frameCounter++;
        
        // Get configuration
        var lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();
        
        // Read player position from CameraDataSingleton (updated at start of Simulation this frame).
        float3 playerPosition = SystemAPI.GetSingleton<CameraDataSingleton>().position;
        
#if UNITY_EDITOR
        s_VelocityCalcMarker.Data.Begin();
#endif
        
        // OPTIMIZED v3.0: Calculate velocity for adaptive throttling
        float velocity = 0f;
        if (_lastDeltaTime > 0)
        {
            velocity = math.length(playerPosition - _lastPlayerPosition) / _lastDeltaTime;
        }

        // Terrain scroll moves content past a stationary player — treat scroll speed as velocity
        if (SystemAPI.TryGetSingleton<TerrainScrollVelocity>(out var scrollVelocity) && scrollVelocity.speed > 0f)
        {
            velocity = math.max(velocity, scrollVelocity.speed);
        }
        
        // Determine frame skip based on velocity
        int effectiveFrameSkip = velocity > lodConfig.playerVelocityThreshold 
            ? lodConfig.vrFrameSkipScrolling 
            : VRFrameSkip;
        
#if UNITY_EDITOR
        s_VelocityCalcMarker.Data.End();
#endif
        
        // VR OPTIMIZATION: Adaptive frame skip based on player velocity.
        // Near-field objects (within lod0Distance) always update; distant chunks skip on off-frames.
        bool skipDistantUpdate = _frameCounter % effectiveFrameSkip != 0;
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data.Begin();
        s_ChunkFilterMarker.Data.Begin();
#endif

        int2 playerChunk = StaticObjectSpatialChunkUtility.GetChunkCoord(playerPosition);
        
        // Build list of chunks to update this frame
        _activeChunks.Clear();
        _activeChunksSet.Clear();
        
        float coverageDistance = skipDistantUpdate
            ? lodConfig.lod0Distance + lodConfig.hysteresisDelta
            : lodConfig.lod2Distance + lodConfig.hysteresisDelta;
        int chunkRadius = StaticObjectSpatialChunkUtility.GetChunkRadiusForDistance(coverageDistance);
        for (int x = -chunkRadius; x <= chunkRadius; x++)
        {
            for (int z = -chunkRadius; z <= chunkRadius; z++)
            {
                int2 chunkCoord = playerChunk + new int2(x, z);
                if (_activeChunksSet.Add(chunkCoord))
                    _activeChunks.Add(chunkCoord);
            }
        }
        
        // Add rotating chunks beyond LOD coverage for stale LOD correction on distant visible objects
        int extraChunksNeeded = skipDistantUpdate
            ? 0
            : math.max(0, lodConfig.maxChunksUpdatedPerFrame - _activeChunks.Length);
        if (extraChunksNeeded > 0)
        {
            int outerRadius = chunkRadius + 1;
            int offset = _frameCounter % (outerRadius * 8);
            for (int i = 0; i < extraChunksNeeded; i++)
            {
                int angle = (offset + i * 8) % 360;
                float rad = math.radians(angle);
                int2 distantChunk = playerChunk + new int2(
                    (int)math.round(math.cos(rad) * outerRadius),
                    (int)math.round(math.sin(rad) * outerRadius)
                );
                
                if (_activeChunksSet.Add(distantChunk))
                    _activeChunks.Add(distantChunk);
            }
        }
        
#if UNITY_EDITOR
        s_ChunkFilterMarker.Data.End();
#endif
        
        // Read LOD MaterialMeshInfo lookup from config entity buffer (TempJob — disposed after job completes).
        var configEntity = SystemAPI.GetSingletonEntity<StaticObjectLODConfig>();
        var lodInfoBuffer = state.EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity, isReadOnly: true);
        var lodMeshInfos = new NativeArray<MaterialMeshInfo>(lodInfoBuffer.Length, Allocator.TempJob);
        for (int i = 0; i < lodInfoBuffer.Length; i++)
            lodMeshInfos[i] = lodInfoBuffer[i].materialMeshInfo;

        NativeArray<AABB> lodRenderBounds = default;
        if (state.EntityManager.HasBuffer<StaticObjectLODRenderBoundsElement>(configEntity))
        {
            var boundsBuffer = state.EntityManager.GetBuffer<StaticObjectLODRenderBoundsElement>(configEntity, isReadOnly: true);
            lodRenderBounds = new NativeArray<AABB>(boundsBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < boundsBuffer.Length; i++)
                lodRenderBounds[i] = boundsBuffer[i].bounds;
        }

        int objectTypeCount = lodConfig.lodsPerObjectType > 0
            ? lodMeshInfos.Length / lodConfig.lodsPerObjectType
            : 0;
        var objectTypeScales = new NativeArray<StaticObjectTypeScaleElement>(objectTypeCount, Allocator.TempJob);
        var defaultTypeScale = new StaticObjectTypeScaleElement
        {
            baseScale = 1f,
            lod1ScaleMultiplier = 1f,
            lod2ScaleMultiplier = 1f
        };
        if (state.EntityManager.HasBuffer<StaticObjectTypeScaleElement>(configEntity))
        {
            var typeScaleBuffer = state.EntityManager.GetBuffer<StaticObjectTypeScaleElement>(configEntity, isReadOnly: true);
            for (int i = 0; i < objectTypeCount; i++)
                objectTypeScales[i] = i < typeScaleBuffer.Length ? typeScaleBuffer[i] : defaultTypeScale;
        }
        else
        {
            for (int i = 0; i < objectTypeCount; i++)
                objectTypeScales[i] = defaultTypeScale;
        }
        
        // Schedule Burst-compiled job for LOD updates with distance-tiered filtering
        var updateJob = new StaticObjectLODUpdateJob
        {
            playerPosition = playerPosition,
            lod0Distance = lodConfig.lod0Distance,
            lod1Distance = lodConfig.lod1Distance,
            lod2Distance = lodConfig.lod2Distance,
            hysteresis = lodConfig.hysteresisDelta,
            lodsPerObjectType = lodConfig.lodsPerObjectType,
            activeChunksSet = _activeChunksSet,
            maxStaticObjectsPerFrame = MaxStaticObjectsPerFrame,
            frameCounter = _frameCounter,
            maxUpdateDistance = skipDistantUpdate ? lodConfig.lod0Distance : 0f,
            lodMeshInfos = lodMeshInfos,
            lodRenderBounds = lodRenderBounds,
            objectTypeScales = objectTypeScales
        };
        
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
        lodMeshInfos.Dispose(state.Dependency);
        if (lodRenderBounds.IsCreated)
            lodRenderBounds.Dispose(state.Dependency);
        objectTypeScales.Dispose(state.Dependency);
        
#if UNITY_EDITOR
        s_ProfilerMarker.Data.End();
#endif
        
        // Update velocity tracking for next frame
        _lastPlayerPosition = playerPosition;
        _lastDeltaTime = SystemAPI.Time.DeltaTime;
        
        // Log periodically (must complete job first for accurate count)
        if (lodConfig.enableObjectLODDebug && _frameCounter % 120 == 0)
        {
            state.Dependency.Complete();
            // Get static object count for logging
            var query = SystemAPI.QueryBuilder().WithAll<GlobalStaticObjectInstance, StaticObjectChunkMembership>().Build();
            int totalStaticObjects = query.CalculateEntityCount();
            UnityEngine.Debug.Log($"[StaticObjectLOD] Velocity: {velocity:F2} m/s, FrameSkip: {effectiveFrameSkip}, Processing {_activeChunks.Length} chunks (total: {totalStaticObjects} static objects)");
        }
    }
    
    /// <summary>
    /// Burst-compiled job that updates static object LOD levels in parallel.
    /// On a LOD change, writes the correct <see cref="MaterialMeshInfo"/> directly to the entity
    /// so Entities.Graphics (BRG) immediately switches the rendered mesh/material.
    /// OPTIMIZED v3.0: Added distance-tiered updates (4 tiers: 0-100m, 100-200m, 200-300m, 300m+).
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(RenderBounds))]
    private partial struct StaticObjectLODUpdateJob : IJobEntity
    {
        [ReadOnly] public float3 playerPosition;
        [ReadOnly] public float lod0Distance;
        [ReadOnly] public float lod1Distance;
        [ReadOnly] public float lod2Distance;
        [ReadOnly] public float hysteresis;
        [ReadOnly] public int lodsPerObjectType;
        [ReadOnly] public NativeHashSet<int2> activeChunksSet;
        [ReadOnly] public int maxStaticObjectsPerFrame;
        [ReadOnly] public int frameCounter;
        /// <summary>When &gt; 0, only objects within this distance are updated (near-field bypass on skipped frames).</summary>
        [ReadOnly] public float maxUpdateDistance;
        [ReadOnly] public NativeArray<MaterialMeshInfo> lodMeshInfos;
        [ReadOnly] public NativeArray<AABB> lodRenderBounds;
        [ReadOnly] public NativeArray<StaticObjectTypeScaleElement> objectTypeScales;
        
        /// <summary>
        /// Skips static objects outside the active chunk set, applies distance-tiered frame-budget skipping,
        /// calculates XZ distance to the player, selects the appropriate LOD slot with hysteresis,
        /// and writes the correct <see cref="MaterialMeshInfo"/> BRG ID for the chosen LOD.
        /// </summary>
        private void Execute(
            ref LocalTransform transform,
            ref GlobalStaticObjectInstanceData instanceData,
            ref MaterialMeshInfo materialMeshInfo,
            ref RenderBounds renderBounds,
            in StaticObjectChunkMembership chunkMembership)
        {
            // OPTIMIZED: O(1) chunk lookup using HashSet
            if (!activeChunksSet.Contains(chunkMembership.chunkCoord))
                return;
            
            // Calculate 2D distance (XZ plane) from player to static object
            float2 objectPos2D = new float2(transform.Position.x, transform.Position.z);
            float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
            float distance = math.distance(objectPos2D, playerPos2D);

            if (maxUpdateDistance > 0f && distance > maxUpdateDistance)
                return;
            
            // OPTIMIZED v3.0: Distance-tiered updates (4 tiers)
            if (maxUpdateDistance <= 0f)
            {
                if (distance > 300f && frameCounter % 8 != 0)
                    return;
                if (distance > 200f && frameCounter % 4 != 0)
                    return;
                if (distance > 100f && frameCounter % 2 != 0)
                    return;
            }
            
            // Determine new LOD level with hysteresis
            byte currentLOD = instanceData.currentLODLevel;
            byte newLOD = DetermineLODLevel(distance, currentLOD, lod0Distance, lod1Distance, lod2Distance, hysteresis);

            float spawnScale = instanceData.spawnScale > 0f ? instanceData.spawnScale : transform.Scale;
            if (instanceData.objectTypeIndex < objectTypeScales.Length)
            {
                var typeScale = objectTypeScales[instanceData.objectTypeIndex];
                // Apply target LOD scale before mesh swap on the transition frame (no early pre-scale).
                transform.Scale = spawnScale * typeScale.GetLodScaleMultiplier(newLOD);
            }

            // Update if LOD changed — write MaterialMeshInfo after scale so BRG switches mesh/material.
            if (newLOD != currentLOD)
            {
                int newMeshIndex = (instanceData.objectTypeIndex * lodsPerObjectType) + newLOD;

                if (lodMeshInfos.Length > newMeshIndex)
                    materialMeshInfo = lodMeshInfos[newMeshIndex];

                if (lodRenderBounds.IsCreated && lodRenderBounds.Length > newMeshIndex)
                    renderBounds.Value = lodRenderBounds[newMeshIndex];

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
}
