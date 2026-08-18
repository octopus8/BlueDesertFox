using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// Schedules terrain mesh generation Burst jobs at the end of the frame (PresentationSystemGroup).
/// By scheduling here, worker threads can process the CPU-intensive noise/vertex math during
/// EarlyUpdate.XRUpdate of the next frame while the main thread is blocked waiting for tracking data.
/// Pairs with <see cref="TerrainMeshCompleteSystem"/> which completes the jobs in
/// InitializationSystemGroup (immediately after XRUpdate finishes).
/// Reads <see cref="CameraDataSingleton"/> written earlier in the frame by
/// <see cref="CameraDataUpdateSystem"/> (<see cref="SimulationSystemGroup"/> runs before Presentation).
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial struct TerrainMeshScheduleSystem : ISystem
{
    private NativeQueue<Entity> _pendingTiles;
    public NativeHashSet<Entity> _queuedTiles;

    // In-flight job state — these are Persistent-allocated and survive the frame boundary.
    // They are disposed by TerrainMeshCompleteSystem after it calls Complete().
    public NativeArray<float3> _inFlightVertices;
    public NativeArray<float3> _inFlightNormals;
    public NativeArray<float2> _inFlightUVs;
    public NativeArray<float> _inFlightHeights;
    public NativeArray<TileMeshJobData> _inFlightTileData;
    public NativeArray<float> _inFlightTrailLuts;
    public NativeList<Entity> _inFlightEntities;
    public JobHandle _inFlightHandle;
    public bool _hasInFlight;
    public int _verticesPerTile;
    public int _heightsPerTile;
    public int _heightGridSide;
    public int _inFlightLutLength;

    // Shared triangle topology for a given verticesPerSide (copied into each tile on Complete).
    public NativeArray<int> _sharedIndexTemplate;
    private int _sharedIndexVerticesPerSide;
    private Entity _trackedConfigEntity;

#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainMesh.Schedule");
    private static readonly ProfilerMarker s_PrioritySortMarker = new ProfilerMarker("TerrainMesh.PrioritySort");
#endif

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<CameraDataSingleton>();
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<TerrainHeightAlignState>();

        _pendingTiles = new NativeQueue<Entity>(Allocator.Persistent);
        _queuedTiles = new NativeHashSet<Entity>(256, Allocator.Persistent);
        _sharedIndexVerticesPerSide = -1;
        _trackedConfigEntity = Entity.Null;
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_pendingTiles.IsCreated) _pendingTiles.Dispose();
        if (_queuedTiles.IsCreated) _queuedTiles.Dispose();

        if (_hasInFlight)
        {
            _inFlightHandle.Complete();
            DisposeInFlightArrays();
        }

        if (_sharedIndexTemplate.IsCreated)
            _sharedIndexTemplate.Dispose();
    }

    /// <summary>
    /// Cancels in-flight mesh work and clears queues when TerrainTileConfig disappears
    /// (SubScene unload / scene reload). Prevents stale entity handles from blocking new tiles.
    /// </summary>
    public void OnStopRunning(ref SystemState state)
    {
        CancelInFlightAndClearQueues();
        _trackedConfigEntity = Entity.Null;
    }

    /// <summary>
    /// Completes any cross-frame mesh jobs and clears pending queues.
    /// Safe to call before destroying Default-World tiles on AutoLoad SubScene reload.
    /// </summary>
    public void CancelInFlightAndClearQueues()
    {
        if (_hasInFlight)
        {
            _inFlightHandle.Complete();
            DisposeInFlightArrays();
            _hasInFlight = false;
        }

        if (_pendingTiles.IsCreated)
            _pendingTiles.Clear();
        if (_queuedTiles.IsCreated)
            _queuedTiles.Clear();
    }

    internal void DisposeInFlightArrays()
    {
        if (_inFlightVertices.IsCreated) _inFlightVertices.Dispose();
        if (_inFlightNormals.IsCreated) _inFlightNormals.Dispose();
        if (_inFlightUVs.IsCreated) _inFlightUVs.Dispose();
        if (_inFlightHeights.IsCreated) _inFlightHeights.Dispose();
        if (_inFlightTileData.IsCreated) _inFlightTileData.Dispose();
        if (_inFlightTrailLuts.IsCreated) _inFlightTrailLuts.Dispose();
        if (_inFlightEntities.IsCreated) _inFlightEntities.Dispose();
    }

    private void EnsureSharedIndexTemplate(int verticesPerSide)
    {
        if (_sharedIndexTemplate.IsCreated && _sharedIndexVerticesPerSide == verticesPerSide)
            return;

        if (_sharedIndexTemplate.IsCreated)
            _sharedIndexTemplate.Dispose();

        int totalTriangles = (verticesPerSide - 1) * (verticesPerSide - 1) * 2;
        int totalIndices = totalTriangles * 3;
        _sharedIndexTemplate = new NativeArray<int>(totalIndices, Allocator.Persistent);

        int write = 0;
        for (int z = 0; z < verticesPerSide - 1; z++)
        {
            for (int x = 0; x < verticesPerSide - 1; x++)
            {
                int baseIndex = z * verticesPerSide + x;

                _sharedIndexTemplate[write++] = baseIndex;
                _sharedIndexTemplate[write++] = baseIndex + verticesPerSide;
                _sharedIndexTemplate[write++] = baseIndex + 1;

                _sharedIndexTemplate[write++] = baseIndex + 1;
                _sharedIndexTemplate[write++] = baseIndex + verticesPerSide;
                _sharedIndexTemplate[write++] = baseIndex + verticesPerSide + 1;
            }
        }

        _sharedIndexVerticesPerSide = verticesPerSide;
    }

    public void OnUpdate(ref SystemState state)
    {
        var configEntity = SystemAPI.GetSingletonEntity<TerrainTileConfig>();
        if (_trackedConfigEntity != configEntity)
        {
            // AutoLoad SubScene reload can skip OnStopRunning; drop stale in-flight entity handles.
            if (_trackedConfigEntity != Entity.Null || _hasInFlight ||
                (_queuedTiles.IsCreated && !_queuedTiles.IsEmpty))
            {
                CancelInFlightAndClearQueues();
            }
            _trackedConfigEntity = configEntity;
        }

#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.renderTerrain)
                return;

            if (SystemAPI.GetSingleton<TerrainHeightAlignState>().aligned == 0)
                return;

            if (_hasInFlight)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[TerrainMesh] Schedule skipped: previous job still in flight. TerrainMeshCompleteSystem may have been skipped.");
#endif
                return;
            }

            float3 cameraPosition = float3.zero;
            float3 cameraForward = new float3(0, 0, 1);

            var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
            cameraPosition = cameraData.position;
            cameraForward = cameraData.fullForward;
            float3 scrollOffset = SystemAPI.GetSingleton<ScrollOffset>().accumulatedOffset;

            foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
                .WithAll<VertexElement>()
                .WithAll<NormalElement>()
                .WithAll<UVElement>()
                .WithAll<IndexElement>()
                .WithEntityAccess())
            {
                if ((!tile.ValueRO.meshGenerated || tile.ValueRO.needsRegeneration) && _queuedTiles.Add(entity))
                    _pendingTiles.Enqueue(entity);
            }

            int maxMeshesPerFrame = math.max(1, config.maxCollidersCreatedPerFrame);

            var tilesWithPriority = new NativeList<MeshTileWithPriority>(
                math.min(_pendingTiles.Count, maxMeshesPerFrame * 2), Allocator.Temp);
            var processedEntities = new NativeHashSet<Entity>(_pendingTiles.Count, Allocator.Temp);

            while (_pendingTiles.Count > 0)
            {
                var entity = _pendingTiles.Dequeue();

                if (processedEntities.Contains(entity))
                    continue;

                if (!state.EntityManager.Exists(entity))
                {
                    _queuedTiles.Remove(entity);
                    continue;
                }

                var tile = SystemAPI.GetComponent<TerrainTile>(entity);
                if (!tile.meshGenerated || tile.needsRegeneration)
                {
                    float priority = CalculateTilePriority(tile, config, cameraPosition, cameraForward, scrollOffset);
                    tilesWithPriority.Add(new MeshTileWithPriority { entity = entity, priority = priority });
                    processedEntities.Add(entity);
                }
                else
                {
                    _queuedTiles.Remove(entity);
                }
            }

            processedEntities.Dispose();

            if (tilesWithPriority.Length == 0)
            {
                tilesWithPriority.Dispose();
                return;
            }

#if UNITY_EDITOR
            using (s_PrioritySortMarker.Auto())
#endif
            {
                if (tilesWithPriority.Length > maxMeshesPerFrame)
                    tilesWithPriority.Sort(new TilePriorityComparer());
            }

            int tilesToProcessCount = math.min(tilesWithPriority.Length, maxMeshesPerFrame);

            _inFlightEntities = new NativeList<Entity>(tilesToProcessCount, Allocator.Persistent);
            for (int i = 0; i < tilesToProcessCount; i++)
                _inFlightEntities.Add(tilesWithPriority[i].entity);

            for (int i = tilesToProcessCount; i < tilesWithPriority.Length; i++)
                _pendingTiles.Enqueue(tilesWithPriority[i].entity);

            tilesWithPriority.Dispose();

            int verticesPerSide = config.verticesPerSide;
            int totalVertices = verticesPerSide * verticesPerSide;
            int heightGridSide = verticesPerSide + 2;
            int heightsPerTile = heightGridSide * heightGridSide;

            EnsureSharedIndexTemplate(verticesPerSide);

            _verticesPerTile = totalVertices;
            _heightsPerTile = heightsPerTile;
            _heightGridSide = heightGridSide;

            _inFlightVertices = new NativeArray<float3>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightNormals = new NativeArray<float3>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightUVs = new NativeArray<float2>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightHeights = new NativeArray<float>(heightsPerTile * tilesToProcessCount, Allocator.Persistent);
            _inFlightTileData = new NativeArray<TileMeshJobData>(tilesToProcessCount, Allocator.Persistent);

            float baseSlopeTan = math.tan(math.radians(config.slopeAngleDegrees));
            float minSlopeTan = math.tan(math.radians(config.slopeAngleDegrees - config.slopeVariationAmplitude));
            float maxSlopeTan = baseSlopeTan;
            float slopeVariationSeedOffset = TerrainMeshNoise.SlopeVariationSeedOffset(config.slopeVariationSeed);

            TrailConfig trailConfig = default;
            TrailPathConfig trailPath = new TrailPathConfig
            {
                startX = 0f,
                startZ = 0f,
                straightLength = 80f,
                weaveFadeLength = 30f,
                startAligned = 0,
                snapStartToPlayer = 1
            };
            byte activeTrailMask = 0;

            if (SystemAPI.HasSingleton<TrailConfig>())
            {
                trailConfig = SystemAPI.GetSingleton<TrailConfig>();
                if (trailConfig.lutStepMeters <= 0f)
                    trailConfig.lutStepMeters = 1f;
                activeTrailMask = TrailInfluenceBurst.GetActiveTrailMask(
                    trailConfig.trail1, trailConfig.trail2, trailConfig.trail3);
            }

            if (SystemAPI.HasSingleton<TrailPathConfig>())
                trailPath = TrailInfluenceBurst.NormalizeTrailPathSettings(
                    SystemAPI.GetSingleton<TrailPathConfig>());

            TrailPaths trailPaths = default;
            if (SystemAPI.HasSingleton<TrailPaths>())
                trailPaths = SystemAPI.GetSingleton<TrailPaths>();

            float trailLutStep = trailConfig.lutStepMeters > 0f ? trailConfig.lutStepMeters : 1f;
            float maxSearchRange = TrailInfluenceBurst.GetMaxSearchRangeAcrossTrails(
                trailConfig.trail1, trailConfig.trail2, trailConfig.trail3, activeTrailMask);
            _inFlightLutLength = activeTrailMask != 0
                ? TrailInfluenceBurst.ComputeLutLength(config.tileSize, maxSearchRange, trailLutStep)
                : 0;

            if (_inFlightLutLength > 0)
            {
                _inFlightTrailLuts = new NativeArray<float>(
                    tilesToProcessCount * 3 * _inFlightLutLength, Allocator.Persistent);
            }
            else
            {
                _inFlightTrailLuts = new NativeArray<float>(0, Allocator.Persistent);
            }

            for (int i = 0; i < tilesToProcessCount; i++)
            {
                var entity = _inFlightEntities[i];
                var tile = SystemAPI.GetComponent<TerrainTile>(entity);

                float tileWorldX = tile.gridCoordinate.x * config.tileSize;
                float tileWorldZ = tile.gridCoordinate.y * config.tileSize;

                double3 tileWorldPos = new double3(tileWorldX, 0, tileWorldZ);

                byte tileTrailMask = activeTrailMask != 0
                    ? TrailInfluenceBurst.ComputeTileTrailMask(
                        tileWorldX, tileWorldZ, config.tileSize, trailConfig, trailPath, trailPaths, activeTrailMask)
                    : (byte)0;

                float lutZOrigin = TrailInfluenceBurst.ComputeLutZOrigin(tileWorldZ, maxSearchRange);
                int baseLutOffset = i * 3 * _inFlightLutLength;

                _inFlightTileData[i] = new TileMeshJobData
                {
                    tileWorldPos = tileWorldPos,
                    verticesPerSide = verticesPerSide,
                    tileSize = config.tileSize,
                    baseSlopeTan = baseSlopeTan,
                    minSlopeTan = minSlopeTan,
                    maxSlopeTan = maxSlopeTan,
                    slopeVariationAmplitude = config.slopeVariationAmplitude,
                    slopeVariationFrequency = config.slopeVariationFrequency,
                    slopeVariationSeedOffset = slopeVariationSeedOffset,
                    noiseFrequency = config.noiseFrequency,
                    noiseAmplitude = config.noiseAmplitude,
                    noiseOctaves = config.noiseOctaves,
                    noiseLacunarity = config.noiseLacunarity,
                    noisePersistence = config.noisePersistence,
                    continentalFrequency = config.continentalFrequency,
                    continentalExponent = config.continentalExponent,
                    vertexOffset = i * totalVertices,
                    heightOffset = config.heightOffset,
                    trailConfig = trailConfig,
                    trailPath = trailPath,
                    trailPaths = trailPaths,
                    activeTrailMask = activeTrailMask,
                    tileTrailMask = tileTrailMask,
                    trail1Lut = new TrailCenterlineLUT
                    {
                        offset = baseLutOffset,
                        length = _inFlightLutLength,
                        zOrigin = lutZOrigin,
                        zStep = trailLutStep
                    },
                    trail2Lut = new TrailCenterlineLUT
                    {
                        offset = baseLutOffset + _inFlightLutLength,
                        length = _inFlightLutLength,
                        zOrigin = lutZOrigin,
                        zStep = trailLutStep
                    },
                    trail3Lut = new TrailCenterlineLUT
                    {
                        offset = baseLutOffset + 2 * _inFlightLutLength,
                        length = _inFlightLutLength,
                        zOrigin = lutZOrigin,
                        zStep = trailLutStep
                    }
                };
            }

            var buildLutsJob = new BuildTileTrailCenterlineLutsJob
            {
                tileData = _inFlightTileData,
                trailLuts = _inFlightTrailLuts
            };

            var heightsJob = new GenerateTileHeightsJob
            {
                tileData = _inFlightTileData,
                trailLuts = _inFlightTrailLuts,
                heightsPerTile = heightsPerTile,
                heightGridSide = heightGridSide,
                allHeights = _inFlightHeights
            };

            var meshJob = new GenerateTileMeshFromHeightsJob
            {
                tileData = _inFlightTileData,
                heightsPerTile = heightsPerTile,
                heightGridSide = heightGridSide,
                verticesPerTile = totalVertices,
                allHeights = _inFlightHeights,
                allVertices = _inFlightVertices,
                allUVs = _inFlightUVs
            };

            var normalsJob = new GenerateTileNormalsJob
            {
                tileData = _inFlightTileData,
                heightsPerTile = heightsPerTile,
                heightGridSide = heightGridSide,
                verticesPerTile = totalVertices,
                allHeights = _inFlightHeights,
                allNormals = _inFlightNormals
            };

            JobHandle lutHandle = state.Dependency;
            if (_inFlightLutLength > 0)
            {
                lutHandle = buildLutsJob.Schedule(tilesToProcessCount, 1, state.Dependency);
            }

            JobHandle heightsHandle = heightsJob.Schedule(heightsPerTile * tilesToProcessCount, 64, lutHandle);
            JobHandle meshHandle = meshJob.Schedule(totalVertices * tilesToProcessCount, 64, heightsHandle);
            JobHandle normalsHandle = normalsJob.Schedule(totalVertices * tilesToProcessCount, 64, heightsHandle);
            _inFlightHandle = JobHandle.CombineDependencies(meshHandle, normalsHandle);
            _hasInFlight = true;
        }
    }

    private static float CalculateTilePriority(TerrainTile tile, TerrainTileConfig config, float3 cameraPosition, float3 cameraForward, float3 scrollOffset)
    {
        float3 tileCenterBase = new float3(
            tile.gridCoordinate.x * config.tileSize + config.tileSize * 0.5f,
            0f,
            tile.gridCoordinate.y * config.tileSize + config.tileSize * 0.5f);
        float3 tileCenterScrolled = tileCenterBase - scrollOffset;

        float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);
        float2 tileCenter2D = new float2(tileCenterScrolled.x, tileCenterScrolled.z);
        float2 toTile = tileCenter2D - cameraPos2D;
        float distance = math.length(toTile);
        float normalizedDistance = math.clamp(distance / config.viewDistance, 0f, 1f);

        float2 cameraForward2D = math.normalize(new float2(cameraForward.x, cameraForward.z));
        float2 toTileNormalized = math.normalize(toTile);
        float dotProduct = math.dot(cameraForward2D, toTileNormalized);
        float viewScore = (dotProduct + 1f) * 0.5f;

        return (1f - viewScore) * 1000f + normalizedDistance * 500f;
    }
}

/// <summary>
/// Completes the terrain mesh generation jobs scheduled by <see cref="TerrainMeshScheduleSystem"/>
/// the previous frame, then copies results into ECS DynamicBuffers.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial struct TerrainMeshCompleteSystem : ISystem
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainMesh.Complete");
    private static readonly ProfilerMarker s_BufferCopyMarker = new ProfilerMarker("TerrainMesh.BufferCopy");
#endif

    public void OnCreate(ref SystemState state) { }

    public void OnUpdate(ref SystemState state)
    {
        var scheduleHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<TerrainMeshScheduleSystem>();
        if (scheduleHandle == SystemHandle.Null)
            return;

        ref var sched = ref state.WorldUnmanaged.GetUnsafeSystemRef<TerrainMeshScheduleSystem>(scheduleHandle);

        if (!sched._hasInFlight)
            return;

#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            sched._inFlightHandle.Complete();

            int totalVertices = sched._verticesPerTile;
            int totalIndices = sched._sharedIndexTemplate.Length;

#if UNITY_EDITOR
            using (s_BufferCopyMarker.Auto())
#endif
            {
                for (int i = 0; i < sched._inFlightEntities.Length; i++)
                {
                    var entity = sched._inFlightEntities[i];

                    if (!state.EntityManager.Exists(entity))
                    {
                        sched._queuedTiles.Remove(entity);
                        continue;
                    }

                    var tile = state.EntityManager.GetComponentData<TerrainTile>(entity);

                    var vertexBuffer = state.EntityManager.GetBuffer<VertexElement>(entity);
                    var normalBuffer = state.EntityManager.GetBuffer<NormalElement>(entity);
                    var uvBuffer = state.EntityManager.GetBuffer<UVElement>(entity);
                    var indexBuffer = state.EntityManager.GetBuffer<IndexElement>(entity);

                    int vertexOffset = i * totalVertices;

                    vertexBuffer.ResizeUninitialized(totalVertices);
                    normalBuffer.ResizeUninitialized(totalVertices);
                    uvBuffer.ResizeUninitialized(totalVertices);
                    indexBuffer.ResizeUninitialized(totalIndices);

                    NativeArray<float3>.Copy(sched._inFlightVertices, vertexOffset,
                        vertexBuffer.Reinterpret<float3>().AsNativeArray(), 0, totalVertices);
                    NativeArray<float3>.Copy(sched._inFlightNormals, vertexOffset,
                        normalBuffer.Reinterpret<float3>().AsNativeArray(), 0, totalVertices);
                    NativeArray<float2>.Copy(sched._inFlightUVs, vertexOffset,
                        uvBuffer.Reinterpret<float2>().AsNativeArray(), 0, totalVertices);
                    NativeArray<int>.Copy(sched._sharedIndexTemplate,
                        indexBuffer.Reinterpret<int>().AsNativeArray());

                    tile.meshGenerated = true;
                    tile.needsRegeneration = false;
                    state.EntityManager.SetComponentData(entity, tile);

                    sched._queuedTiles.Remove(entity);

                    // Always reset static-object spawn state on mesh (re)complete so trees
                    // recalculate against the new vertices. Previously only ran when
                    // StaticObjectsSpawned was present, leaving mid-calc tiles stuck.
                    if (state.EntityManager.HasBuffer<SpawnedStaticObjectReference>(entity))
                    {
                        var spawnedObjects = state.EntityManager.GetBuffer<SpawnedStaticObjectReference>(entity);
                        for (int objIdx = spawnedObjects.Length - 1; objIdx >= 0; objIdx--)
                        {
                            var objectEntity = spawnedObjects[objIdx].objectEntity;
                            StaticObjectHierarchyDestroyUtility.DestroyHierarchyImmediate(
                                objectEntity, state.EntityManager);
                            spawnedObjects.RemoveAt(objIdx);
                        }
                    }

                    if (state.EntityManager.HasComponent<StaticObjectsSpawned>(entity))
                        state.EntityManager.RemoveComponent<StaticObjectsSpawned>(entity);
                    if (state.EntityManager.HasComponent<StaticObjectSpawnProgress>(entity))
                        state.EntityManager.RemoveComponent<StaticObjectSpawnProgress>(entity);
                    if (state.EntityManager.HasComponent<StaticObjectPositionCalcProgress>(entity))
                        state.EntityManager.RemoveComponent<StaticObjectPositionCalcProgress>(entity);
                    if (state.EntityManager.HasBuffer<StaticObjectSpawnPosition>(entity))
                        state.EntityManager.GetBuffer<StaticObjectSpawnPosition>(entity).Clear();
                }
            }

            sched.DisposeInFlightArrays();
            sched._hasInFlight = false;
        }
    }
}

/// <summary>
/// Data passed to each job for mesh generation.
/// </summary>
public struct TileMeshJobData
{
    public double3 tileWorldPos;
    public int verticesPerSide;
    public float tileSize;
    public float baseSlopeTan;
    public float minSlopeTan;
    public float maxSlopeTan;
    public float slopeVariationAmplitude;
    public float slopeVariationFrequency;
    public float slopeVariationSeedOffset;
    public float noiseFrequency;
    public float noiseAmplitude;
    public int noiseOctaves;
    public float noiseLacunarity;
    public float noisePersistence;
    public float continentalFrequency;
    public float continentalExponent;
    public int vertexOffset;
    public float heightOffset;

    /// <summary>Trail instance shape + shared height.</summary>
    public TrailConfig trailConfig;
    /// <summary>Shared start / straight-run path.</summary>
    public TrailPathConfig trailPath;
    /// <summary>Optional spline-authored centerlines (uncreated blobs = noise weave).</summary>
    public TrailPaths trailPaths;
    public byte activeTrailMask;
    public byte tileTrailMask;
    public TrailCenterlineLUT trail1Lut;
    public TrailCenterlineLUT trail2Lut;
    public TrailCenterlineLUT trail3Lut;
}

#if UNITY_EDITOR
internal static class TerrainMeshProfiler
{
    internal static readonly ProfilerMarker TrailLUTBuild = new ProfilerMarker("TerrainMesh.TrailLUTBuild");
    internal static readonly ProfilerMarker TrailInfluence = new ProfilerMarker("TerrainMesh.TrailInfluence");
    internal static readonly ProfilerMarker BaseNoise = new ProfilerMarker("TerrainMesh.BaseNoise");

    [BurstDiscard]
    internal static void Begin(ProfilerMarker marker) => marker.Begin();

    [BurstDiscard]
    internal static void End(ProfilerMarker marker) => marker.End();
}
#endif

/// <summary>
/// Builds per-tile trail centerline LUTs once before height generation.
/// </summary>
[BurstCompile]
public struct BuildTileTrailCenterlineLutsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;
    [NativeDisableParallelForRestriction] public NativeArray<float> trailLuts;

    public void Execute(int tileIndex)
    {
#if UNITY_EDITOR
        TerrainMeshProfiler.Begin(TerrainMeshProfiler.TrailLUTBuild);
#endif
        var data = tileData[tileIndex];

        if (data.tileTrailMask == 0)
        {
#if UNITY_EDITOR
            TerrainMeshProfiler.End(TerrainMeshProfiler.TrailLUTBuild);
#endif
            return;
        }

        var trailConfig = data.trailConfig;
        var trailPath = data.trailPath;

        if ((data.tileTrailMask & TrailMask.Trail1) != 0)
        {
            TrailInfluenceBurst.BuildTrailCenterlineLUT(
                trailLuts, data.trail1Lut.offset, data.trail1Lut.zOrigin, data.trail1Lut.zStep,
                data.trail1Lut.length, trailConfig.trail1, trailPath, data.trailPaths.trail1);
        }

        if ((data.tileTrailMask & TrailMask.Trail2) != 0)
        {
            TrailInfluenceBurst.BuildTrailCenterlineLUT(
                trailLuts, data.trail2Lut.offset, data.trail2Lut.zOrigin, data.trail2Lut.zStep,
                data.trail2Lut.length, trailConfig.trail2, trailPath, data.trailPaths.trail2);
        }

        if ((data.tileTrailMask & TrailMask.Trail3) != 0)
        {
            TrailInfluenceBurst.BuildTrailCenterlineLUT(
                trailLuts, data.trail3Lut.offset, data.trail3Lut.zOrigin, data.trail3Lut.zStep,
                data.trail3Lut.length, trailConfig.trail3, trailPath, data.trailPaths.trail3);
        }

#if UNITY_EDITOR
        TerrainMeshProfiler.End(TerrainMeshProfiler.TrailLUTBuild);
#endif
    }
}

/// <summary>
/// Samples terrain heights for an (N+2)×(N+2) grid per tile (1-cell halo for seamless normals).
/// </summary>
[BurstCompile]
public struct GenerateTileHeightsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;
    [ReadOnly] public NativeArray<float> trailLuts;
    public int heightsPerTile;
    public int heightGridSide;

    [NativeDisableParallelForRestriction] public NativeArray<float> allHeights;

    public void Execute(int globalHeightIndex)
    {
        int tileIndex = globalHeightIndex / heightsPerTile;
        int localHeightIndex = globalHeightIndex - tileIndex * heightsPerTile;

        var data = tileData[tileIndex];
        int verticesPerSide = data.verticesPerSide;
        int hx = localHeightIndex % heightGridSide;
        int hz = localHeightIndex / heightGridSide;

        // Halo coords: mesh (x,z) maps to height (x+1, z+1); hx/hz in [0, N+1] → mesh-space [-1, N]
        int meshX = hx - 1;
        int meshZ = hz - 1;

        float stepSize = data.tileSize / (verticesPerSide - 1);
        double worldX = data.tileWorldPos.x + meshX * stepSize;
        double worldZ = data.tileWorldPos.z + meshZ * stepSize;

        int heightOffset = tileIndex * heightsPerTile;
        allHeights[heightOffset + localHeightIndex] = TerrainMeshNoise.SampleHeight(
            worldX, worldZ, data, trailLuts);
    }
}

/// <summary>
/// Builds mesh vertex positions and UVs from the pre-sampled height halo grid.
/// </summary>
[BurstCompile]
public struct GenerateTileMeshFromHeightsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;
    [ReadOnly] public NativeArray<float> allHeights;
    public int heightsPerTile;
    public int heightGridSide;
    public int verticesPerTile;

    [NativeDisableParallelForRestriction] public NativeArray<float3> allVertices;
    [NativeDisableParallelForRestriction] public NativeArray<float2> allUVs;

    public void Execute(int globalVertexIndex)
    {
        int tileIndex = globalVertexIndex / verticesPerTile;
        int localVertexIndex = globalVertexIndex - tileIndex * verticesPerTile;

        var data = tileData[tileIndex];
        int verticesPerSide = data.verticesPerSide;
        int x = localVertexIndex % verticesPerSide;
        int z = localVertexIndex / verticesPerSide;

        float stepSize = data.tileSize / (verticesPerSide - 1);
        float halfTileSize = data.tileSize * 0.5f;
        float localX = x * stepSize;
        float localZ = z * stepSize;

        int heightOffset = tileIndex * heightsPerTile;
        int heightIndex = heightOffset + (z + 1) * heightGridSide + (x + 1);
        float height = allHeights[heightIndex];

        int flatIndex = data.vertexOffset + localVertexIndex;
        allVertices[flatIndex] = new float3(localX - halfTileSize, height, localZ - halfTileSize);
        allUVs[flatIndex] = new float2(
            (float)x / (verticesPerSide - 1),
            (float)z / (verticesPerSide - 1));
    }
}

/// <summary>
/// Computes normals in parallel from the height halo grid (no re-sampling).
/// </summary>
[BurstCompile]
public struct GenerateTileNormalsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;
    [ReadOnly] public NativeArray<float> allHeights;
    public int heightsPerTile;
    public int heightGridSide;
    public int verticesPerTile;

    [NativeDisableParallelForRestriction] public NativeArray<float3> allNormals;

    public void Execute(int globalVertexIndex)
    {
        int tileIndex = globalVertexIndex / verticesPerTile;
        int localVertexIndex = globalVertexIndex - tileIndex * verticesPerTile;

        var data = tileData[tileIndex];
        int verticesPerSide = data.verticesPerSide;
        int x = localVertexIndex % verticesPerSide;
        int z = localVertexIndex / verticesPerSide;

        float stepSize = data.tileSize / (verticesPerSide - 1);
        int heightOffset = tileIndex * heightsPerTile;

        // Mesh (x,z) → halo (x+1, z+1); neighbors are always in-range.
        int hx = x + 1;
        int hz = z + 1;
        float heightLeft = allHeights[heightOffset + hz * heightGridSide + (hx - 1)];
        float heightRight = allHeights[heightOffset + hz * heightGridSide + (hx + 1)];
        float heightDown = allHeights[heightOffset + (hz - 1) * heightGridSide + hx];
        float heightUp = allHeights[heightOffset + (hz + 1) * heightGridSide + hx];

        float3 tangentX = new float3(2.0f * stepSize, heightRight - heightLeft, 0);
        float3 tangentZ = new float3(0, heightUp - heightDown, 2.0f * stepSize);
        allNormals[data.vertexOffset + localVertexIndex] = math.normalize(math.cross(tangentZ, tangentX));
    }
}

/// <summary>
/// Height sampling helpers shared by mesh generation jobs.
/// </summary>
[BurstCompile]
public static class TerrainMeshNoise
{
    public static float SampleHeight(
        double worldX,
        double worldZ,
        in TileMeshJobData data,
        NativeArray<float> trailLuts)
    {
#if UNITY_EDITOR
        TerrainMeshProfiler.Begin(TerrainMeshProfiler.BaseNoise);
#endif
        float terrainHeight = SampleBaseTerrainHeight(worldX, worldZ, data);
#if UNITY_EDITOR
        TerrainMeshProfiler.End(TerrainMeshProfiler.BaseNoise);
#endif

        if (data.tileTrailMask == 0)
            return terrainHeight + data.heightOffset;

#if UNITY_EDITOR
        TerrainMeshProfiler.Begin(TerrainMeshProfiler.TrailInfluence);
#endif
        float maxInfluence = 0f;
        float trailSlopeZ = 0f;

        if ((data.tileTrailMask & TrailMask.Trail1) != 0)
        {
            var result = TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                (float)worldX, (float)worldZ, data.trailConfig.trail1, data.trail1Lut, trailLuts);
            if (result.influence > maxInfluence)
            {
                maxInfluence = result.influence;
                trailSlopeZ = result.centerlineZ;
            }
        }

        if ((data.tileTrailMask & TrailMask.Trail2) != 0)
        {
            var result = TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                (float)worldX, (float)worldZ, data.trailConfig.trail2, data.trail2Lut, trailLuts);
            if (result.influence > maxInfluence)
            {
                maxInfluence = result.influence;
                trailSlopeZ = result.centerlineZ;
            }
        }

        if ((data.tileTrailMask & TrailMask.Trail3) != 0)
        {
            var result = TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                (float)worldX, (float)worldZ, data.trailConfig.trail3, data.trail3Lut, trailLuts);
            if (result.influence > maxInfluence)
            {
                maxInfluence = result.influence;
                trailSlopeZ = result.centerlineZ;
            }
        }
#if UNITY_EDITOR
        TerrainMeshProfiler.End(TerrainMeshProfiler.TrailInfluence);
#endif

        if (maxInfluence > 0f)
        {
            float slopedTrailHeight = data.trailConfig.height + SampleGradeHeight(trailSlopeZ, data);
            return math.lerp(terrainHeight, slopedTrailHeight, maxInfluence) + data.heightOffset;
        }

        return terrainHeight + data.heightOffset;
    }

    /// <summary>
    /// Samples terrain height at a world XZ using the same noise/grade/trail rules as mesh
    /// generation, without applying <see cref="TileMeshJobData.heightOffset"/>.
    /// Used by <see cref="TerrainHeightAlignSystem"/> to compute the one-shot vertical align.
    /// </summary>
    public static float SampleUnalignedHeightAt(
        float worldX,
        float worldZ,
        in TerrainTileConfig config,
        bool hasTrailConfig,
        in TrailConfig trailConfig,
        in TrailPathConfig trailPath,
        in TrailPaths trailPaths)
    {
        float baseSlopeTan = math.tan(math.radians(config.slopeAngleDegrees));
        float minSlopeTan = math.tan(math.radians(config.slopeAngleDegrees - config.slopeVariationAmplitude));
        float maxSlopeTan = baseSlopeTan;
        float slopeVariationSeedOffset = SlopeVariationSeedOffset(config.slopeVariationSeed);

        float trailLutStep = 1f;
        TrailConfig normalizedTrail = trailConfig;
        TrailPathConfig normalizedPath = TrailInfluenceBurst.NormalizeTrailPathSettings(trailPath);
        TrailInstanceConfig trailInst1 = default;
        TrailInstanceConfig trailInst2 = default;
        TrailInstanceConfig trailInst3 = default;
        byte activeTrailMask = 0;

        if (hasTrailConfig)
        {
            if (normalizedTrail.lutStepMeters <= 0f)
                normalizedTrail.lutStepMeters = 1f;
            trailLutStep = normalizedTrail.lutStepMeters;
            trailInst1 = normalizedTrail.trail1;
            trailInst2 = normalizedTrail.trail2;
            trailInst3 = normalizedTrail.trail3;
            activeTrailMask = TrailInfluenceBurst.GetActiveTrailMask(trailInst1, trailInst2, trailInst3);
        }

        float tileWorldX = math.floor(worldX / config.tileSize) * config.tileSize;
        float tileWorldZ = math.floor(worldZ / config.tileSize) * config.tileSize;

        byte tileTrailMask = 0;
        float maxSearchRange = 0f;
        int lutLength = 0;
        NativeArray<float> trailLuts = default;

        if (activeTrailMask != 0)
        {
            tileTrailMask = TrailInfluenceBurst.ComputeTileTrailMask(
                tileWorldX, tileWorldZ, config.tileSize, normalizedTrail, normalizedPath, trailPaths, activeTrailMask);

            if (tileTrailMask != 0)
            {
                maxSearchRange = TrailInfluenceBurst.GetMaxSearchRangeAcrossTrails(
                    trailInst1, trailInst2, trailInst3, activeTrailMask);
                lutLength = TrailInfluenceBurst.ComputeLutLength(config.tileSize, maxSearchRange, trailLutStep);
                float lutZOrigin = TrailInfluenceBurst.ComputeLutZOrigin(tileWorldZ, maxSearchRange);
                trailLuts = new NativeArray<float>(lutLength * 3, Allocator.Temp);

                if ((tileTrailMask & TrailMask.Trail1) != 0)
                {
                    TrailInfluenceBurst.BuildTrailCenterlineLUT(
                        trailLuts, 0, lutZOrigin, trailLutStep, lutLength, trailInst1, normalizedPath, trailPaths.trail1);
                }

                if ((tileTrailMask & TrailMask.Trail2) != 0)
                {
                    TrailInfluenceBurst.BuildTrailCenterlineLUT(
                        trailLuts, lutLength, lutZOrigin, trailLutStep, lutLength, trailInst2, normalizedPath, trailPaths.trail2);
                }

                if ((tileTrailMask & TrailMask.Trail3) != 0)
                {
                    TrailInfluenceBurst.BuildTrailCenterlineLUT(
                        trailLuts, lutLength * 2, lutZOrigin, trailLutStep, lutLength, trailInst3, normalizedPath, trailPaths.trail3);
                }
            }
        }

        if (!trailLuts.IsCreated)
            trailLuts = new NativeArray<float>(0, Allocator.Temp);

        float lutZOriginForData = TrailInfluenceBurst.ComputeLutZOrigin(tileWorldZ, maxSearchRange);
        var data = new TileMeshJobData
        {
            tileWorldPos = new double3(tileWorldX, 0, tileWorldZ),
            verticesPerSide = config.verticesPerSide,
            tileSize = config.tileSize,
            baseSlopeTan = baseSlopeTan,
            minSlopeTan = minSlopeTan,
            maxSlopeTan = maxSlopeTan,
            slopeVariationAmplitude = config.slopeVariationAmplitude,
            slopeVariationFrequency = config.slopeVariationFrequency,
            slopeVariationSeedOffset = slopeVariationSeedOffset,
            noiseFrequency = config.noiseFrequency,
            noiseAmplitude = config.noiseAmplitude,
            noiseOctaves = config.noiseOctaves,
            noiseLacunarity = config.noiseLacunarity,
            noisePersistence = config.noisePersistence,
            continentalFrequency = config.continentalFrequency,
            continentalExponent = config.continentalExponent,
            vertexOffset = 0,
            heightOffset = 0f,
            trailConfig = normalizedTrail,
            trailPath = normalizedPath,
            trailPaths = trailPaths,
            activeTrailMask = activeTrailMask,
            tileTrailMask = tileTrailMask,
            trail1Lut = new TrailCenterlineLUT
            {
                offset = 0,
                length = lutLength,
                zOrigin = lutZOriginForData,
                zStep = trailLutStep
            },
            trail2Lut = new TrailCenterlineLUT
            {
                offset = lutLength,
                length = lutLength,
                zOrigin = lutZOriginForData,
                zStep = trailLutStep
            },
            trail3Lut = new TrailCenterlineLUT
            {
                offset = lutLength * 2,
                length = lutLength,
                zOrigin = lutZOriginForData,
                zStep = trailLutStep
            }
        };

        float height = SampleHeight(worldX, worldZ, data, trailLuts);
        trailLuts.Dispose();
        return height;
    }

    /// <summary>
    /// Stable 1D domain offset so changing <paramref name="seed"/> selects a different slope-noise pattern along +Z.
    /// </summary>
    public static float SlopeVariationSeedOffset(int seed)
    {
        return seed * 17.31f;
    }

    private static float GetSlopeTanAt(float worldZ, in TileMeshJobData data)
    {
        if (data.slopeVariationAmplitude <= 0f)
            return data.baseSlopeTan;

        float t = noise.snoise(new float2(worldZ * data.slopeVariationFrequency + data.slopeVariationSeedOffset, 0f)) * 0.5f + 0.5f;
        return math.lerp(data.minSlopeTan, data.maxSlopeTan, t);
    }

    private static float SampleGradeHeight(float worldZ, in TileMeshJobData data)
    {
        if (data.slopeVariationAmplitude <= 0f)
            return worldZ * data.baseSlopeTan;

        if (worldZ == 0f)
            return 0f;

        float tileSize = data.tileSize;
        float height = 0f;

        if (worldZ > 0f)
        {
            int endTile = (int)math.floor(worldZ / tileSize);
            for (int t = 0; t <= endTile; t++)
            {
                float segStart = t * tileSize;
                float segEnd = t == endTile ? worldZ : (t + 1) * tileSize;
                height += IntegrateGradeSegment(segStart, segEnd, data);
            }
        }
        else
        {
            // Path-integrate from worldZ up to 0, then negate: ∫_0^worldZ = -∫_worldZ^0.
            // Keeps grade continuous through the origin and across negative-Z tile boundaries
            // (the old Z * localSlope shortcut jumped by |Z| * Δtan).
            int startTile = (int)math.floor(worldZ / tileSize);
            for (int t = startTile; t <= -1; t++)
            {
                float segStart = t == startTile ? worldZ : t * tileSize;
                float segEnd = (t + 1) * tileSize;
                height += IntegrateGradeSegment(segStart, segEnd, data);
            }
            return -height;
        }

        return height;
    }

    private static float IntegrateGradeSegment(float segStart, float segEnd, in TileMeshJobData data)
    {
        if (segEnd <= segStart)
            return 0f;

        const int steps = 4;
        float dz = (segEnd - segStart) / steps;
        float height = 0f;
        for (int i = 0; i < steps; i++)
        {
            float midZ = segStart + (i + 0.5f) * dz;
            height += GetSlopeTanAt(midZ, data) * dz;
        }

        return height;
    }

    private static float SampleBaseTerrainHeight(double worldX, double worldZ, in TileMeshJobData data)
    {
        float continentalMask = 1f;
        if (data.continentalFrequency > 0f && data.continentalExponent > 0f)
        {
            float2 continentalPos = new float2((float)worldX, (float)worldZ) * data.continentalFrequency;
            float rawContinent = noise.snoise(continentalPos) * 0.5f + 0.5f;
            continentalMask = math.pow(rawContinent, data.continentalExponent);
        }

        float total = 0f;
        float frequency = data.noiseFrequency;
        float amplitude = data.noiseAmplitude;
        float maxValue = 0f;

        for (int i = 0; i < data.noiseOctaves; i++)
        {
            float2 samplePos = new float2((float)worldX, (float)worldZ) * frequency;
            float noiseValue = noise.snoise(samplePos);

            total += noiseValue * amplitude;
            maxValue += amplitude;

            amplitude *= data.noisePersistence;
            frequency *= data.noiseLacunarity;
        }

        return total / maxValue * data.noiseAmplitude * continentalMask
            + SampleGradeHeight((float)worldZ, data);
    }
}

struct MeshTileWithPriority
{
    public Entity entity;
    public float priority;
}

struct TilePriorityComparer : IComparer<MeshTileWithPriority>
{
    public int Compare(MeshTileWithPriority a, MeshTileWithPriority b)
    {
        return a.priority.CompareTo(b.priority);
    }
}
