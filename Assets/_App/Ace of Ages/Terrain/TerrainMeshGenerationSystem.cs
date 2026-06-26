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
/// Runs after <see cref="CameraDataUpdateSystem"/> so it reads the freshly-written
/// <see cref="CameraDataSingleton"/> for camera-aware tile priority sorting.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(CameraDataUpdateSystem))]
public partial struct TerrainMeshScheduleSystem : ISystem
{
    private NativeQueue<Entity> _pendingTiles;
    public NativeHashSet<Entity> _queuedTiles;

    // In-flight job state — these are Persistent-allocated and survive the frame boundary.
    // They are disposed by TerrainMeshCompleteSystem after it calls Complete().
    public NativeArray<float3> _inFlightVertices;
    public NativeArray<float3> _inFlightNormals;
    public NativeArray<float2> _inFlightUVs;
    public NativeArray<int> _inFlightIndices;
    public NativeArray<TileMeshJobData> _inFlightTileData;
    public NativeArray<float> _inFlightTrailLuts;
    public NativeList<Entity> _inFlightEntities;
    public JobHandle _inFlightHandle;
    public bool _hasInFlight;
    public int _verticesPerTile;
    public int _indicesPerTile;
    public int _inFlightLutLength;

#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainMesh.Schedule");
    private static readonly ProfilerMarker s_PrioritySortMarker = new ProfilerMarker("TerrainMesh.PrioritySort");
#endif

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<CameraDataSingleton>();

        _pendingTiles = new NativeQueue<Entity>(Allocator.Persistent);
        _queuedTiles = new NativeHashSet<Entity>(256, Allocator.Persistent);
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
    }

    internal void DisposeInFlightArrays()
    {
        if (_inFlightVertices.IsCreated) _inFlightVertices.Dispose();
        if (_inFlightNormals.IsCreated) _inFlightNormals.Dispose();
        if (_inFlightUVs.IsCreated) _inFlightUVs.Dispose();
        if (_inFlightIndices.IsCreated) _inFlightIndices.Dispose();
        if (_inFlightTileData.IsCreated) _inFlightTileData.Dispose();
        if (_inFlightTrailLuts.IsCreated) _inFlightTrailLuts.Dispose();
        if (_inFlightEntities.IsCreated) _inFlightEntities.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.renderTerrain)
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
                    float priority = CalculateTilePriority(tile, config, cameraPosition, cameraForward);
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
            int totalTriangles = (verticesPerSide - 1) * (verticesPerSide - 1) * 2;
            int totalIndices = totalTriangles * 3;

            _verticesPerTile = totalVertices;
            _indicesPerTile = totalIndices;

            _inFlightVertices = new NativeArray<float3>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightNormals = new NativeArray<float3>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightUVs = new NativeArray<float2>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightIndices = new NativeArray<int>(totalIndices * tilesToProcessCount, Allocator.Persistent);
            _inFlightTileData = new NativeArray<TileMeshJobData>(tilesToProcessCount, Allocator.Persistent);

            float trailHeight = 0f;
            float trailLutStep = 1f;
            TrailInstanceConfig trailInst1 = default;
            TrailInstanceConfig trailInst2 = default;
            TrailInstanceConfig trailInst3 = default;
            byte activeTrailMask = 0;

            if (SystemAPI.HasSingleton<TrailConfig>())
            {
                var trail = SystemAPI.GetSingleton<TrailConfig>();
                trailHeight = trail.height;
                trailLutStep = trail.lutStepMeters > 0f ? trail.lutStepMeters : 1f;
                trailInst1 = trail.trail1;
                trailInst2 = trail.trail2;
                trailInst3 = trail.trail3;
                activeTrailMask = TrailInfluenceBurst.GetActiveTrailMask(trailInst1, trailInst2, trailInst3);
            }

            float maxSearchRange = TrailInfluenceBurst.GetMaxSearchRangeAcrossTrails(
                trailInst1, trailInst2, trailInst3, activeTrailMask);
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
                        tileWorldX, tileWorldZ, config.tileSize,
                        trailInst1, trailInst2, trailInst3, activeTrailMask)
                    : (byte)0;

                float lutZOrigin = TrailInfluenceBurst.ComputeLutZOrigin(tileWorldZ, maxSearchRange);
                int baseLutOffset = i * 3 * _inFlightLutLength;

                _inFlightTileData[i] = new TileMeshJobData
                {
                    tileWorldPos = tileWorldPos,
                    verticesPerSide = verticesPerSide,
                    tileSize = config.tileSize,
                    noiseFrequency = config.noiseFrequency,
                    noiseAmplitude = config.noiseAmplitude,
                    noiseOctaves = config.noiseOctaves,
                    noiseLacunarity = config.noiseLacunarity,
                    noisePersistence = config.noisePersistence,
                    continentalFrequency = config.continentalFrequency,
                    continentalExponent = config.continentalExponent,
                    vertexOffset = i * totalVertices,
                    indexOffset = i * totalIndices,
                    trailHeight = trailHeight,
                    trailLutStep = trailLutStep,
                    activeTrailMask = activeTrailMask,
                    tileTrailMask = tileTrailMask,
                    trail1 = trailInst1,
                    trail2 = trailInst2,
                    trail3 = trailInst3,
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

            var buildLutsJob = new BuildTileTrailLutsJob
            {
                tileData = _inFlightTileData,
                trailLuts = _inFlightTrailLuts
            };

            var verticesJob = new GenerateTileVerticesJob
            {
                tileData = _inFlightTileData,
                trailLuts = _inFlightTrailLuts,
                verticesPerTile = totalVertices,
                allVertices = _inFlightVertices,
                allUVs = _inFlightUVs
            };

            var normalsJob = new GenerateTileNormalsAndIndicesJob
            {
                tileData = _inFlightTileData,
                allVertices = _inFlightVertices,
                allNormals = _inFlightNormals,
                allIndices = _inFlightIndices
            };

            JobHandle lutHandle = state.Dependency;
            if (_inFlightLutLength > 0)
            {
                lutHandle = buildLutsJob.Schedule(tilesToProcessCount, 1, state.Dependency);
            }

            JobHandle vertexHandle = verticesJob.Schedule(totalVertices * tilesToProcessCount, 64, lutHandle);
            _inFlightHandle = normalsJob.Schedule(tilesToProcessCount, 1, vertexHandle);
            _hasInFlight = true;
        }
    }

    private static float CalculateTilePriority(TerrainTile tile, TerrainTileConfig config, float3 cameraPosition, float3 cameraForward)
    {
        float2 tileCenter = new float2(
            tile.gridCoordinate.x * config.tileSize + config.tileSize * 0.5f,
            tile.gridCoordinate.y * config.tileSize + config.tileSize * 0.5f);

        float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);
        float2 toTile = tileCenter - cameraPos2D;
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
            int totalIndices = sched._indicesPerTile;

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
                    int indexOffset = i * totalIndices;

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
                    NativeArray<int>.Copy(sched._inFlightIndices, indexOffset,
                        indexBuffer.Reinterpret<int>().AsNativeArray(), 0, totalIndices);

                    tile.meshGenerated = true;
                    tile.needsRegeneration = false;
                    state.EntityManager.SetComponentData(entity, tile);

                    sched._queuedTiles.Remove(entity);

                    if (state.EntityManager.HasComponent<StaticObjectsSpawned>(entity))
                    {
                        state.EntityManager.RemoveComponent<StaticObjectsSpawned>(entity);
#if UNITY_EDITOR
                        UnityEngine.Debug.Log($"[TerrainMesh] Removed StaticObjectsSpawned tag from regenerated tile {tile.gridCoordinate}");
#endif
                    }
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
    public float noiseFrequency;
    public float noiseAmplitude;
    public int noiseOctaves;
    public float noiseLacunarity;
    public float noisePersistence;
    public float continentalFrequency;
    public float continentalExponent;
    public int vertexOffset;
    public int indexOffset;

    public float trailHeight;
    public float trailLutStep;
    public byte activeTrailMask;
    public byte tileTrailMask;
    public TrailInstanceConfig trail1;
    public TrailInstanceConfig trail2;
    public TrailInstanceConfig trail3;
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
/// Builds per-tile trail centerline LUTs once before vertex generation.
/// </summary>
[BurstCompile]
public struct BuildTileTrailLutsJob : IJobParallelFor
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

        if ((data.tileTrailMask & TrailMask.Trail1) != 0)
        {
            TrailInfluenceBurst.BuildTrailCenterlineLUT(
                trailLuts, data.trail1Lut.offset, data.trail1Lut.zOrigin, data.trail1Lut.zStep,
                data.trail1Lut.length, data.trail1);
        }

        if ((data.tileTrailMask & TrailMask.Trail2) != 0)
        {
            TrailInfluenceBurst.BuildTrailCenterlineLUT(
                trailLuts, data.trail2Lut.offset, data.trail2Lut.zOrigin, data.trail2Lut.zStep,
                data.trail2Lut.length, data.trail2);
        }

        if ((data.tileTrailMask & TrailMask.Trail3) != 0)
        {
            TrailInfluenceBurst.BuildTrailCenterlineLUT(
                trailLuts, data.trail3Lut.offset, data.trail3Lut.zOrigin, data.trail3Lut.zStep,
                data.trail3Lut.length, data.trail3);
        }

#if UNITY_EDITOR
        TerrainMeshProfiler.End(TerrainMeshProfiler.TrailLUTBuild);
#endif
    }
}

/// <summary>
/// Generates vertex heights and UVs in parallel across all vertices.
/// </summary>
[BurstCompile]
public struct GenerateTileVerticesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;
    [ReadOnly] public NativeArray<float> trailLuts;
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
        int flatIndex = data.vertexOffset + localVertexIndex;

        float localX = x * stepSize;
        float localZ = z * stepSize;

        double worldX = data.tileWorldPos.x + localX;
        double worldZ = data.tileWorldPos.z + localZ;

        float height = TerrainMeshNoise.SampleHeight(
            worldX, worldZ, data, trailLuts);

        allVertices[flatIndex] = new float3(localX - halfTileSize, height, localZ - halfTileSize);
        allUVs[flatIndex] = new float2(
            (float)x / (verticesPerSide - 1),
            (float)z / (verticesPerSide - 1));
    }
}

/// <summary>
/// Computes normals and triangle indices per tile after all vertices are written.
/// </summary>
[BurstCompile]
public struct GenerateTileNormalsAndIndicesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;

    [ReadOnly] public NativeArray<float3> allVertices;
    [NativeDisableParallelForRestriction] public NativeArray<float3> allNormals;
    [NativeDisableParallelForRestriction] public NativeArray<int> allIndices;

    public void Execute(int tileIndex)
    {
        var data = tileData[tileIndex];
        int vertexOffset = data.vertexOffset;
        int indexOffset = data.indexOffset;
        int verticesPerSide = data.verticesPerSide;
        float stepSize = data.tileSize / (verticesPerSide - 1);

        for (int z = 0; z < verticesPerSide; z++)
        {
            for (int x = 0; x < verticesPerSide; x++)
            {
                int flatIndex = vertexOffset + z * verticesPerSide + x;

                float heightLeft = GetCachedHeight(x - 1, z, verticesPerSide, vertexOffset, allVertices);
                float heightRight = GetCachedHeight(x + 1, z, verticesPerSide, vertexOffset, allVertices);
                float heightDown = GetCachedHeight(x, z - 1, verticesPerSide, vertexOffset, allVertices);
                float heightUp = GetCachedHeight(x, z + 1, verticesPerSide, vertexOffset, allVertices);

                float3 tangentX = new float3(2.0f * stepSize, heightRight - heightLeft, 0);
                float3 tangentZ = new float3(0, heightUp - heightDown, 2.0f * stepSize);
                allNormals[flatIndex] = math.normalize(math.cross(tangentZ, tangentX));
            }
        }

        int currentIndexOffset = 0;
        for (int z = 0; z < verticesPerSide - 1; z++)
        {
            for (int x = 0; x < verticesPerSide - 1; x++)
            {
                int baseIndex = z * verticesPerSide + x;

                allIndices[indexOffset + currentIndexOffset++] = baseIndex;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + verticesPerSide;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + 1;

                allIndices[indexOffset + currentIndexOffset++] = baseIndex + 1;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + verticesPerSide;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + verticesPerSide + 1;
            }
        }
    }

    private static float GetCachedHeight(int x, int z, int verticesPerSide, int vertexOffset, NativeArray<float3> vertices)
    {
        x = math.clamp(x, 0, verticesPerSide - 1);
        z = math.clamp(z, 0, verticesPerSide - 1);
        return vertices[vertexOffset + z * verticesPerSide + x].y;
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
            return terrainHeight;

#if UNITY_EDITOR
        TerrainMeshProfiler.Begin(TerrainMeshProfiler.TrailInfluence);
#endif
        float maxInfluence = 0f;

        if ((data.tileTrailMask & TrailMask.Trail1) != 0)
        {
            maxInfluence = math.max(maxInfluence,
                TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                    (float)worldX, (float)worldZ, data.trail1, data.trail1Lut, trailLuts));
        }

        if ((data.tileTrailMask & TrailMask.Trail2) != 0)
        {
            maxInfluence = math.max(maxInfluence,
                TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                    (float)worldX, (float)worldZ, data.trail2, data.trail2Lut, trailLuts));
        }

        if ((data.tileTrailMask & TrailMask.Trail3) != 0)
        {
            maxInfluence = math.max(maxInfluence,
                TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                    (float)worldX, (float)worldZ, data.trail3, data.trail3Lut, trailLuts));
        }
#if UNITY_EDITOR
        TerrainMeshProfiler.End(TerrainMeshProfiler.TrailInfluence);
#endif

        if (maxInfluence > 0f)
            return math.lerp(terrainHeight, data.trailHeight, maxInfluence);

        return terrainHeight;
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

        return total / maxValue * data.noiseAmplitude * continentalMask;
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
