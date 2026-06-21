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
    public NativeList<Entity> _inFlightEntities;
    public JobHandle _inFlightHandle;
    public bool _hasInFlight;
    public int _verticesPerTile;
    public int _indicesPerTile;

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

        // Complete and clean up any job that TerrainMeshCompleteSystem didn't get to
        if (_hasInFlight)
        {
            _inFlightHandle.Complete();
            if (_inFlightVertices.IsCreated) _inFlightVertices.Dispose();
            if (_inFlightNormals.IsCreated) _inFlightNormals.Dispose();
            if (_inFlightUVs.IsCreated) _inFlightUVs.Dispose();
            if (_inFlightIndices.IsCreated) _inFlightIndices.Dispose();
            if (_inFlightTileData.IsCreated) _inFlightTileData.Dispose();
            if (_inFlightEntities.IsCreated) _inFlightEntities.Dispose();
        }
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

            // TerrainMeshCompleteSystem should have cleared this before we run again.
            // If it's still set, a previous job hasn't been consumed — skip this frame.
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

            // Enqueue tiles that need mesh generation
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

            // Allocate the entity list with Persistent so it survives the frame boundary
            _inFlightEntities = new NativeList<Entity>(tilesToProcessCount, Allocator.Persistent);
            for (int i = 0; i < tilesToProcessCount; i++)
                _inFlightEntities.Add(tilesWithPriority[i].entity);

            // Return remaining tiles to the queue for the next frame's schedule pass
            for (int i = tilesToProcessCount; i < tilesWithPriority.Length; i++)
                _pendingTiles.Enqueue(tilesWithPriority[i].entity);

            tilesWithPriority.Dispose();

            int verticesPerSide = config.verticesPerSide;
            int totalVertices = verticesPerSide * verticesPerSide;
            int totalTriangles = (verticesPerSide - 1) * (verticesPerSide - 1) * 2;
            int totalIndices = totalTriangles * 3;

            _verticesPerTile = totalVertices;
            _indicesPerTile = totalIndices;

            // Allocate flat arrays with Persistent — these must outlive this frame
            _inFlightVertices = new NativeArray<float3>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightNormals = new NativeArray<float3>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightUVs = new NativeArray<float2>(totalVertices * tilesToProcessCount, Allocator.Persistent);
            _inFlightIndices = new NativeArray<int>(totalIndices * tilesToProcessCount, Allocator.Persistent);
            _inFlightTileData = new NativeArray<TileMeshJobData>(tilesToProcessCount, Allocator.Persistent);

            float trailHeight = 0f;
            TrailInstanceConfig trailInst1 = default;
            TrailInstanceConfig trailInst2 = default;
            TrailInstanceConfig trailInst3 = default;
            if (SystemAPI.HasSingleton<TrailConfig>())
            {
                var trail = SystemAPI.GetSingleton<TrailConfig>();
                trailHeight = trail.height;
                trailInst1 = trail.trail1;
                trailInst2 = trail.trail2;
                trailInst3 = trail.trail3;
            }

            for (int i = 0; i < tilesToProcessCount; i++)
            {
                var entity = _inFlightEntities[i];
                var tile = SystemAPI.GetComponent<TerrainTile>(entity);

                double3 tileWorldPos = new double3(
                    tile.gridCoordinate.x * config.tileSize,
                    0,
                    tile.gridCoordinate.y * config.tileSize);

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
                    trail1 = trailInst1,
                    trail2 = trailInst2,
                    trail3 = trailInst3
                };
            }

            var meshGenJob = new GenerateTileMeshJob
            {
                tileData = _inFlightTileData,
                allVertices = _inFlightVertices,
                allNormals = _inFlightNormals,
                allUVs = _inFlightUVs,
                allIndices = _inFlightIndices
            };

            // Schedule without Complete() — workers run during next frame's EarlyUpdate.XRUpdate.
            // Intentionally NOT assigned to state.Dependency so the job runs outside DOTS
            // dependency tracking and keeps running after this system's update exits.
            _inFlightHandle = meshGenJob.Schedule(tilesToProcessCount, 1, state.Dependency);
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
/// the previous frame, then copies results into ECS DynamicBuffers so that
/// <see cref="_App.Ace_of_Ages.Terrain.TerrainPhysicsSystem"/> and
/// <see cref="TerrainStaticObjectSpawningSystemOptimized"/> (both in SimulationSystemGroup)
/// see fresh mesh data in the same frame.
/// Runs in InitializationSystemGroup — immediately after EarlyUpdate.XRUpdate finishes —
/// so worker threads overlap with the XR tracking wait at no extra wall-clock cost.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial struct TerrainMeshCompleteSystem : ISystem
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainMesh.Complete");
    private static readonly ProfilerMarker s_BufferCopyMarker = new ProfilerMarker("TerrainMesh.BufferCopy");
#endif

    public void OnCreate(ref SystemState state)
    {
        // RequireForUpdate intentionally omitted: we must always run to complete any in-flight
        // job even if the world is in a transitional state where singletons are absent.
    }

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
            // This Complete() call should return almost immediately — workers ran during XRUpdate.
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

            if (sched._inFlightVertices.IsCreated) sched._inFlightVertices.Dispose();
            if (sched._inFlightNormals.IsCreated) sched._inFlightNormals.Dispose();
            if (sched._inFlightUVs.IsCreated) sched._inFlightUVs.Dispose();
            if (sched._inFlightIndices.IsCreated) sched._inFlightIndices.Dispose();
            if (sched._inFlightTileData.IsCreated) sched._inFlightTileData.Dispose();
            if (sched._inFlightEntities.IsCreated) sched._inFlightEntities.Dispose();

            sched._hasInFlight = false;
        }
    }
}

/// <summary>
/// Data passed to each job for mesh generation.
/// </summary>
[BurstCompile]
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

    // Trail parameters (height shared; individual shapes via TrailInstanceConfig)
    public float trailHeight;
    public TrailInstanceConfig trail1;
    public TrailInstanceConfig trail2;
    public TrailInstanceConfig trail3;
}

/// <summary>
/// Burst-compiled parallel job that generates mesh data for terrain tiles.
/// Each job processes one tile independently using flat arrays with offsets.
/// </summary>
[BurstCompile]
public struct GenerateTileMeshJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;
    [NativeDisableParallelForRestriction] public NativeArray<float3> allVertices;
    [NativeDisableParallelForRestriction] public NativeArray<float3> allNormals;
    [NativeDisableParallelForRestriction] public NativeArray<float2> allUVs;
    [NativeDisableParallelForRestriction] public NativeArray<int> allIndices;

    /// <summary>
    /// Generates vertices, normals, UVs, and triangle indices for one terrain tile at <paramref name="index"/>
    /// using multi-octave Perlin noise, writing output into pre-allocated shared native arrays at the
    /// tile's pre-computed vertex and index offsets.
    /// </summary>
    public void Execute(int index)
    {
        var data = tileData[index];
        int vertexOffset = data.vertexOffset;
        int indexOffset = data.indexOffset;

        float stepSize = data.tileSize / (data.verticesPerSide - 1);
        float halfTileSize = data.tileSize * 0.5f;

        // Generate vertices and UVs
        for (int z = 0; z < data.verticesPerSide; z++)
        {
            for (int x = 0; x < data.verticesPerSide; x++)
            {
                int vertexIndex = z * data.verticesPerSide + x;
                int flatIndex = vertexOffset + vertexIndex;

                float localX = x * stepSize;
                float localZ = z * stepSize;

                double worldX = data.tileWorldPos.x + localX;
                double worldZ = data.tileWorldPos.z + localZ;

                float height = SampleNoise(worldX, worldZ, data);

                allVertices[flatIndex] = new float3(localX - halfTileSize, height, localZ - halfTileSize);

                allUVs[flatIndex] = new float2(
                    (float)x / (data.verticesPerSide - 1),
                    (float)z / (data.verticesPerSide - 1));
            }
        }

        // Normals from cached heights (one noise sample per vertex; no trail re-evaluation)
        for (int z = 0; z < data.verticesPerSide; z++)
        {
            for (int x = 0; x < data.verticesPerSide; x++)
            {
                int flatIndex = vertexOffset + z * data.verticesPerSide + x;

                float heightLeft  = GetCachedHeight(x - 1, z, data.verticesPerSide, vertexOffset, allVertices);
                float heightRight = GetCachedHeight(x + 1, z, data.verticesPerSide, vertexOffset, allVertices);
                float heightDown  = GetCachedHeight(x, z - 1, data.verticesPerSide, vertexOffset, allVertices);
                float heightUp    = GetCachedHeight(x, z + 1, data.verticesPerSide, vertexOffset, allVertices);

                float3 tangentX = new float3(2.0f * stepSize, heightRight - heightLeft, 0);
                float3 tangentZ = new float3(0, heightUp - heightDown, 2.0f * stepSize);
                allNormals[flatIndex] = math.normalize(math.cross(tangentZ, tangentX));
            }
        }

        // Generate indices (triangles)
        int currentIndexOffset = 0;
        for (int z = 0; z < data.verticesPerSide - 1; z++)
        {
            for (int x = 0; x < data.verticesPerSide - 1; x++)
            {
                int baseIndex = z * data.verticesPerSide + x;

                allIndices[indexOffset + currentIndexOffset++] = baseIndex;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + data.verticesPerSide;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + 1;

                allIndices[indexOffset + currentIndexOffset++] = baseIndex + 1;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + data.verticesPerSide;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + data.verticesPerSide + 1;
            }
        }
    }

    /// <summary>
    /// Samples multi-octave noise at the given world position.
    /// A continental mask (very low-frequency noise raised to a power) scales the amplitude
    /// so that flat plains and tall mountains coexist naturally.
    /// </summary>
    private static float SampleNoise(double worldX, double worldZ, in TileMeshJobData data)
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

        float terrainHeight = total / maxValue * data.noiseAmplitude * continentalMask;

        float maxInfluence = 0f;
        if (data.trail1.enabled) maxInfluence = math.max(maxInfluence, ComputeTrailInfluence((float)worldX, (float)worldZ, data.trail1));
        if (data.trail2.enabled) maxInfluence = math.max(maxInfluence, ComputeTrailInfluence((float)worldX, (float)worldZ, data.trail2));
        if (data.trail3.enabled) maxInfluence = math.max(maxInfluence, ComputeTrailInfluence((float)worldX, (float)worldZ, data.trail3));

        if (maxInfluence > 0f)
            return math.lerp(terrainHeight, data.trailHeight, maxInfluence);

        return terrainHeight;
    }

    /// <summary>
    /// Returns a 0–1 influence value for a single trail at world position (fX, fZ).
    /// 1.0 = fully inside the flat zone; 0–1 = inside the blend zone; 0.0 = outside.
    /// Uses a two-stage minimum-distance search along the trail centerline so that
    /// high-amplitude/high-frequency trails remain accurate at sharp bends.
    ///
    /// Stage 1 – coarse pass: 32 uniform samples across ±searchRange in Z.
    /// Stage 2 – refine pass: 16 samples in a ±2-step window around the coarse best,
    ///           bringing worst-case distance error to well under 1 m.
    /// </summary>
    private static float ComputeTrailInfluence(float fX, float fZ, in TrailInstanceConfig trail)
    {
        float halfWidth   = trail.width * 0.5f;
        float searchRange = halfWidth + trail.blendWidth;
        float minDist2D   = float.MaxValue;
        float bestSz      = fZ;

        const int kCoarseSamples = 32;
        float coarseStep = (2f * searchRange) / (kCoarseSamples - 1);
        for (int si = 0; si < kCoarseSamples; si++)
        {
            float sz  = fZ - searchRange + si * coarseStep;
            float scx = trail.amplitude * noise.snoise(new float2(sz * trail.frequency + trail.seed, 0f));
            float dx  = fX - scx;
            float dz  = fZ - sz;
            float d2  = dx * dx + dz * dz;
            if (d2 < minDist2D) { minDist2D = d2; bestSz = sz; }
        }

        const int kRefineSamples = 16;
        float refineRange = coarseStep * 2f;
        float refineStep  = (2f * refineRange) / (kRefineSamples - 1);
        for (int si = 0; si < kRefineSamples; si++)
        {
            float sz  = bestSz - refineRange + si * refineStep;
            float scx = trail.amplitude * noise.snoise(new float2(sz * trail.frequency + trail.seed, 0f));
            float dx  = fX - scx;
            float dz  = fZ - sz;
            float d2  = dx * dx + dz * dz;
            if (d2 < minDist2D) minDist2D = d2;
        }

        float minDist = math.sqrt(minDist2D);

        if (minDist < halfWidth)
            return 1f;

        if (minDist < halfWidth + trail.blendWidth)
            return 1f - math.smoothstep(halfWidth, halfWidth + trail.blendWidth, minDist);

        return 0f;
    }

    private static float GetCachedHeight(int x, int z, int verticesPerSide, int vertexOffset, NativeArray<float3> vertices)
    {
        x = math.clamp(x, 0, verticesPerSide - 1);
        z = math.clamp(z, 0, verticesPerSide - 1);
        return vertices[vertexOffset + z * verticesPerSide + x].y;
    }
}

/// <summary>
/// Helper struct for storing entity with its calculated priority for mesh generation.
/// </summary>
struct MeshTileWithPriority
{
    public Entity entity;
    public float priority;
}

/// <summary>
/// Comparer for sorting tiles by priority (ascending - lower = higher priority).
/// </summary>
struct TilePriorityComparer : IComparer<MeshTileWithPriority>
{
    public int Compare(MeshTileWithPriority a, MeshTileWithPriority b)
    {
        return a.priority.CompareTo(b.priority);
    }
}
