using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// Work item describing a batch of static objects to instantiate on a single tile this frame.
/// </summary>
public struct StaticObjectSpawnWorkItem
{
    public Entity tileEntity;
    public int startIndex;
    public int count;
}

struct SpawnTileCandidate
{
    public Entity tileEntity;
    public int startIndex;
    public int remaining;
    public float forwardPriority;
    public float tileDist;
}

/// <summary>
/// OPTIMIZED: Burst-compiled system that spawns tree entities on terrain tiles after mesh generation.
/// Uses parallel jobs for position calculation and EntityCommandBuffer for batched structural changes.
/// Designed for Quest 3 VR performance with scrolling terrain and high tree density.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(CameraDataUpdateSystem))]
[UpdateAfter(typeof(TileScrollPositionSystem))]
[UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
public partial struct TerrainTreeSpawningSystemOptimized : ISystem
{
    private bool _startupClearDone;

#if UNITY_EDITOR
    private static readonly ProfilerMarker s_PositionCalcMarker = new ProfilerMarker("TreeSpawner.PositionCalc");
    private static readonly ProfilerMarker s_InstantiationMarker = new ProfilerMarker("TreeSpawner.Instantiation");
#endif

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectSpawnerConfig>();
        state.RequireForUpdate<StaticObjectPrefabElement>();
        state.RequireForUpdate<StaticObjectLODMeshInfoReady>();
        state.RequireForUpdate<CameraDataSingleton>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<StaticObjectSpawnerConfig>();

        if (config.maxObjectsPerTile <= 0)
            return;

        if (!_startupClearDone)
        {
            _startupClearDone = true;
            if (SystemAPI.HasSingleton<TrailConfig>())
            {
                var trailCfg = SystemAPI.GetSingleton<TrailConfig>();
                if (trailCfg.trail1.enabled || trailCfg.trail2.enabled || trailCfg.trail3.enabled)
                {
                    var clearEcb = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>()
                        .CreateCommandBuffer(state.WorldUnmanaged);
                    foreach (var (_, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
                        .WithAll<StaticObjectsSpawned>()
                        .WithEntityAccess())
                    {
                        clearEcb.RemoveComponent<StaticObjectsSpawned>(entity);
                        clearEcb.RemoveComponent<StaticObjectPositionCalcProgress>(entity);
                        clearEcb.RemoveComponent<StaticObjectSpawnProgress>(entity);
                        clearEcb.SetBuffer<StaticObjectSpawnPosition>(entity).Clear();
                    }
                }
            }
        }

        var configEntity = SystemAPI.GetSingletonEntity<StaticObjectSpawnerConfig>();
        var objectPrefabsBuffer = state.EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity, true);

        if (objectPrefabsBuffer.Length == 0)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.LogWarning("[TreeSpawnerOptimized] No tree prefabs configured!");
#endif
            return;
        }

        var objectPrefabCount = objectPrefabsBuffer.Length;
        var treeTypeCount = objectPrefabCount / 3;

        if (treeTypeCount == 0)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.LogWarning($"[TreeSpawnerOptimized] Not enough prefabs for LOD system. Need at least 3, have {objectPrefabCount}");
#endif
            return;
        }

        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();

        StaticObjectLODConfig lodConfig = default;
        bool hasLODConfig = SystemAPI.HasSingleton<StaticObjectLODConfig>();
        if (hasLODConfig)
            lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();

        var ecbSystem = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecbForBuffers = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

        int tilesNeedingBuffer = 0;
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned, StaticObjectSpawnPosition>()
            .WithEntityAccess())
        {
            if (!tile.ValueRO.meshGenerated)
                continue;

            uint seed = (uint)(tile.ValueRO.gridCoordinate.GetHashCode() + 12345);
            var random = new Random(seed);
            int targetCount = random.NextInt(config.minObjectsPerTile, config.maxObjectsPerTile + 1);

            ecbForBuffers.AddBuffer<StaticObjectSpawnPosition>(entity);
            ecbForBuffers.AddComponent(entity, new StaticObjectPositionCalcProgress
            {
                targetCount = targetCount,
                acceptedCount = 0,
                attempts = 0,
                randomState = random.state
            });
            tilesNeedingBuffer++;
        }

        int tilesNeedingPositionCalc = 0;
        bool hasReadyToInstantiate = false;
        foreach (var (calcProgress, spawnPositions, _) in SystemAPI.Query<RefRO<StaticObjectPositionCalcProgress>, DynamicBuffer<StaticObjectSpawnPosition>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned>()
            .WithEntityAccess())
        {
            if (calcProgress.ValueRO.acceptedCount < calcProgress.ValueRO.targetCount)
                tilesNeedingPositionCalc++;
            else if (spawnPositions.Length > 0)
                hasReadyToInstantiate = true;
        }

        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference, StaticObjectSpawnPosition>()
            .WithNone<StaticObjectsSpawned, StaticObjectPositionCalcProgress>()
            .WithEntityAccess())
        {
            if (!tile.ValueRO.meshGenerated)
                continue;

            uint seed = (uint)(tile.ValueRO.gridCoordinate.GetHashCode() + 12345);
            var spawnPositions = state.EntityManager.GetBuffer<StaticObjectSpawnPosition>(entity, true);
            int targetCount;
            int acceptedCount;
            uint randomState;

            if (spawnPositions.Length > 0)
            {
                targetCount = spawnPositions.Length;
                acceptedCount = spawnPositions.Length;
                randomState = seed;
            }
            else
            {
                var random = new Random(seed);
                targetCount = random.NextInt(config.minObjectsPerTile, config.maxObjectsPerTile + 1);
                acceptedCount = 0;
                randomState = random.state;
            }

            ecbForBuffers.AddComponent(entity, new StaticObjectPositionCalcProgress
            {
                targetCount = targetCount,
                acceptedCount = acceptedCount,
                attempts = 0,
                randomState = randomState
            });

            if (acceptedCount >= targetCount && spawnPositions.Length > 0)
                hasReadyToInstantiate = true;
        }

        bool hasInstantiationInProgress = !SystemAPI.QueryBuilder()
            .WithAll<StaticObjectSpawnProgress, StaticObjectSpawnPosition>()
            .WithNone<StaticObjectsSpawned>()
            .Build()
            .IsEmpty;

        if (tilesNeedingPositionCalc == 0 && !hasInstantiationInProgress && !hasReadyToInstantiate)
        {
            if (config.enableSpawnerDebug && tilesNeedingBuffer > 0)
                UnityEngine.Debug.Log($"[TreeSpawnerOptimized] Adding StaticObjectSpawnPosition buffer to {tilesNeedingBuffer} tiles (will spawn next frame)");
            return;
        }

        var terrainConfig = SystemAPI.GetSingleton<TerrainTileConfig>();

        var objectPrefabs = new NativeArray<Entity>(objectPrefabCount, Allocator.TempJob);
        var objectPrefabRotations = new NativeArray<quaternion>(objectPrefabCount, Allocator.TempJob);
        for (int i = 0; i < objectPrefabCount; i++)
        {
            objectPrefabs[i] = objectPrefabsBuffer[i].prefabEntity;
            if (state.EntityManager.HasComponent<LocalTransform>(objectPrefabsBuffer[i].prefabEntity))
                objectPrefabRotations[i] = state.EntityManager.GetComponentData<LocalTransform>(objectPrefabsBuffer[i].prefabEntity).Rotation;
            else
                objectPrefabRotations[i] = quaternion.identity;
        }

        var typeSpawnWeightsBuffer = state.EntityManager.GetBuffer<StaticObjectTypeSpawnWeight>(configEntity, true);
        var objectTypeWeightPrefixSum = new NativeArray<float>(treeTypeCount, Allocator.TempJob);
        float equalTypeWeight = treeTypeCount > 0 ? 1f / treeTypeCount : 1f;
        float cumulativeWeight = 0f;
        for (int i = 0; i < treeTypeCount; i++)
        {
            cumulativeWeight += i < typeSpawnWeightsBuffer.Length
                ? typeSpawnWeightsBuffer[i].weight
                : equalTypeWeight;
            objectTypeWeightPrefixSum[i] = cumulativeWeight;
        }

        var billboardTypeBuffer = state.EntityManager.GetBuffer<StaticObjectBillboardTypeElement>(configEntity, true);
        var billboardTypes = new NativeArray<bool>(treeTypeCount, Allocator.TempJob);
        for (int i = 0; i < treeTypeCount; i++)
            billboardTypes[i] = i < billboardTypeBuffer.Length && billboardTypeBuffer[i].isBillboard;

        var typeScaleBuffer = state.EntityManager.GetBuffer<StaticObjectTypeScaleElement>(configEntity, true);
        var objectTypeScales = new NativeArray<StaticObjectTypeScaleElement>(treeTypeCount, Allocator.TempJob);
        var defaultTypeScale = new StaticObjectTypeScaleElement
        {
            baseScale = 1f,
            maxScaleDelta = 0f,
            lod1ScaleMultiplier = 1f,
            lod2ScaleMultiplier = 1f
        };
        for (int i = 0; i < treeTypeCount; i++)
            objectTypeScales[i] = i < typeScaleBuffer.Length ? typeScaleBuffer[i] : defaultTypeScale;

        TrailConfig trailConfig = default;
        bool hasTrailConfig = SystemAPI.HasSingleton<TrailConfig>();
        if (hasTrailConfig)
            trailConfig = SystemAPI.GetSingleton<TrailConfig>();

        int maxPositionCalcAttemptsPerFrame = config.maxPositionCalcAttemptsPerFrame > 0
            ? config.maxPositionCalcAttemptsPerFrame
            : 4000;

        int attemptBudgetPerTile = math.max(1,
            maxPositionCalcAttemptsPerFrame / math.max(1, tilesNeedingPositionCalc));

        if (tilesNeedingPositionCalc > 0)
        {
#if UNITY_EDITOR
            using (s_PositionCalcMarker.Auto())
#endif
            {
                var positionJob = new CalculateStaticObjectSpawnPositionsJob
                {
                    config = config,
                    lodConfig = lodConfig,
                    hasLODConfig = hasLODConfig,
                    terrainConfig = terrainConfig,
                    cameraPosition = cameraData.position,
                    treeTypeCount = treeTypeCount,
                    objectPrefabRotations = objectPrefabRotations,
                    objectTypeScales = objectTypeScales,
                    objectTypeWeightPrefixSum = objectTypeWeightPrefixSum,
                    trailConfig = trailConfig,
                    hasTrailConfig = hasTrailConfig,
                    attemptBudgetPerTile = attemptBudgetPerTile
                };

                state.Dependency = positionJob.ScheduleParallel(state.Dependency);
            }
        }

        // Gather reads StaticObjectPositionCalcProgress / spawn buffers written by the job above.
        if (tilesNeedingPositionCalc > 0)
            state.Dependency.Complete();

        float nearTileSpawnDistance = hasLODConfig ? lodConfig.lod2Distance : terrainConfig.viewDistance * 0.5f;
        float nearFieldSpawnDistance = hasLODConfig ? lodConfig.lod0Distance : 200f;
        float2 cameraPos2D = new float2(cameraData.position.x, cameraData.position.z);
        float2 cameraFwd2D = math.normalizesafe(new float2(cameraData.forward.x, cameraData.forward.z), new float2(0f, 1f));

        var workItems = new NativeList<StaticObjectSpawnWorkItem>(16, Allocator.TempJob);
        var spawnCandidates = new NativeList<SpawnTileCandidate>(16, Allocator.Temp);

        foreach (var (spawnProgress, spawnPositions, tileTransform, entity) in SystemAPI
            .Query<RefRO<StaticObjectSpawnProgress>, DynamicBuffer<StaticObjectSpawnPosition>, RefRO<LocalTransform>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned>()
            .WithEntityAccess())
        {
            int remaining = spawnPositions.Length - spawnProgress.ValueRO.nextSpawnIndex;
            if (remaining <= 0)
                continue;

            float2 tilePos2D = new float2(tileTransform.ValueRO.Position.x, tileTransform.ValueRO.Position.z);
            float tileDist = math.distance(tilePos2D, cameraPos2D);
            float2 toTile = math.normalizesafe(tilePos2D - cameraPos2D, float2.zero);
            spawnCandidates.Add(new SpawnTileCandidate
            {
                tileEntity = entity,
                startIndex = spawnProgress.ValueRO.nextSpawnIndex,
                remaining = remaining,
                forwardPriority = math.dot(toTile, cameraFwd2D),
                tileDist = tileDist
            });
        }

        foreach (var (calcProgress, spawnPositions, tileTransform, entity) in SystemAPI
            .Query<RefRO<StaticObjectPositionCalcProgress>, DynamicBuffer<StaticObjectSpawnPosition>, RefRO<LocalTransform>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned, StaticObjectSpawnProgress>()
            .WithEntityAccess())
        {
            if (calcProgress.ValueRO.acceptedCount < calcProgress.ValueRO.targetCount)
                continue;
            if (spawnPositions.Length == 0)
                continue;

            float2 tilePos2D = new float2(tileTransform.ValueRO.Position.x, tileTransform.ValueRO.Position.z);
            float tileDist = math.distance(tilePos2D, cameraPos2D);
            float2 toTile = math.normalizesafe(tilePos2D - cameraPos2D, float2.zero);
            spawnCandidates.Add(new SpawnTileCandidate
            {
                tileEntity = entity,
                startIndex = 0,
                remaining = spawnPositions.Length,
                forwardPriority = math.dot(toTile, cameraFwd2D),
                tileDist = tileDist
            });
        }

        for (int i = 1; i < spawnCandidates.Length; i++)
        {
            var key = spawnCandidates[i];
            int j = i - 1;
            while (j >= 0 &&
                   (spawnCandidates[j].forwardPriority < key.forwardPriority ||
                    (spawnCandidates[j].forwardPriority == key.forwardPriority && spawnCandidates[j].tileDist > key.tileDist)))
            {
                spawnCandidates[j + 1] = spawnCandidates[j];
                j--;
            }
            spawnCandidates[j + 1] = key;
        }

        int nearBudget = config.maxNearObjectsSpawnedPerFrame > 0
            ? config.maxNearObjectsSpawnedPerFrame
            : config.maxObjectsSpawnedPerFrame;
        int farBudget = config.maxObjectsSpawnedPerFrame;

        for (int i = 0; i < spawnCandidates.Length; i++)
        {
            var candidate = spawnCandidates[i];
            bool useNearBudget = candidate.tileDist <= nearFieldSpawnDistance;
            int availableBudget = useNearBudget ? nearBudget : farBudget;

            int count;
            if (candidate.tileDist <= nearTileSpawnDistance)
                count = candidate.remaining;
            else
            {
                if (availableBudget <= 0)
                    continue;
                count = math.min(candidate.remaining, availableBudget);
            }

            if (count <= 0)
                continue;

            workItems.Add(new StaticObjectSpawnWorkItem
            {
                tileEntity = candidate.tileEntity,
                startIndex = candidate.startIndex,
                count = count
            });

            if (useNearBudget)
                nearBudget -= count;
            else
                farBudget -= count;
        }

        spawnCandidates.Dispose();

        var lodInfoBuffer = state.EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity, true);
        var lodMeshInfos = new NativeArray<MaterialMeshInfo>(lodInfoBuffer.Length, Allocator.TempJob);
        for (int i = 0; i < lodInfoBuffer.Length; i++)
            lodMeshInfos[i] = lodInfoBuffer[i].materialMeshInfo;

        NativeArray<Unity.Mathematics.AABB> objectTypeMaxRenderBounds = default;
        NativeArray<Unity.Mathematics.AABB> lodRenderBounds = default;
        if (state.EntityManager.HasBuffer<StaticObjectTypeMaxRenderBoundsElement>(configEntity))
        {
            var maxBoundsBuffer = state.EntityManager.GetBuffer<StaticObjectTypeMaxRenderBoundsElement>(configEntity, true);
            objectTypeMaxRenderBounds = new NativeArray<Unity.Mathematics.AABB>(maxBoundsBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < maxBoundsBuffer.Length; i++)
                objectTypeMaxRenderBounds[i] = maxBoundsBuffer[i].bounds;
        }

        if (state.EntityManager.HasBuffer<StaticObjectLODRenderBoundsElement>(configEntity))
        {
            var boundsBuffer = state.EntityManager.GetBuffer<StaticObjectLODRenderBoundsElement>(configEntity, true);
            lodRenderBounds = new NativeArray<Unity.Mathematics.AABB>(boundsBuffer.Length, Allocator.TempJob);
            for (int i = 0; i < boundsBuffer.Length; i++)
                lodRenderBounds[i] = boundsBuffer[i].bounds;
        }

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        var spawnPositionLookup = SystemAPI.GetBufferLookup<StaticObjectSpawnPosition>(true);
        spawnPositionLookup.Update(ref state);

        var tileTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        tileTransformLookup.Update(ref state);

#if UNITY_EDITOR
        using (s_InstantiationMarker.Auto())
#endif
        {
            var instantiateJob = new InstantiateStaticObjectsJob
            {
                ecb = ecb,
                workItems = workItems.AsDeferredJobArray(),
                objectPrefabs = objectPrefabs,
                objectTypeScales = objectTypeScales,
                billboardTypes = billboardTypes,
                lodMeshInfos = lodMeshInfos,
                lodRenderBounds = lodRenderBounds,
                objectTypeMaxRenderBounds = objectTypeMaxRenderBounds,
                spawnPositionLookup = spawnPositionLookup,
                tileTransformLookup = tileTransformLookup,
                cameraPosition = cameraData.position,
                hasLODConfig = hasLODConfig,
                lod0Distance = hasLODConfig ? lodConfig.lod0Distance : 0f,
                lod1Distance = hasLODConfig ? lodConfig.lod1Distance : 0f
            };

            state.Dependency = instantiateJob.Schedule(workItems, 1, state.Dependency);
        }

        objectPrefabs.Dispose(state.Dependency);
        objectPrefabRotations.Dispose(state.Dependency);
        objectTypeScales.Dispose(state.Dependency);
        objectTypeWeightPrefixSum.Dispose(state.Dependency);
        billboardTypes.Dispose(state.Dependency);
        lodMeshInfos.Dispose(state.Dependency);
        if (lodRenderBounds.IsCreated)
            lodRenderBounds.Dispose(state.Dependency);
        if (objectTypeMaxRenderBounds.IsCreated)
            objectTypeMaxRenderBounds.Dispose(state.Dependency);
        workItems.Dispose(state.Dependency);
    }
}

[BurstCompile]
[WithAll(typeof(MeshReference), typeof(StaticObjectPositionCalcProgress))]
[WithNone(typeof(StaticObjectsSpawned))]
partial struct CalculateStaticObjectSpawnPositionsJob : IJobEntity
{
    [ReadOnly] public StaticObjectSpawnerConfig config;
    [ReadOnly] public StaticObjectLODConfig lodConfig;
    [ReadOnly] public bool hasLODConfig;
    [ReadOnly] public TerrainTileConfig terrainConfig;
    [ReadOnly] public float3 cameraPosition;
    [ReadOnly] public int treeTypeCount;
    [ReadOnly] public NativeArray<quaternion> objectPrefabRotations;
    [ReadOnly] public NativeArray<StaticObjectTypeScaleElement> objectTypeScales;
    [ReadOnly] public NativeArray<float> objectTypeWeightPrefixSum;
    [ReadOnly] public TrailConfig trailConfig;
    [ReadOnly] public bool hasTrailConfig;
    [ReadOnly] public int attemptBudgetPerTile;

    private void Execute(
        in TerrainTile tile,
        in LocalTransform tileTransform,
        ref StaticObjectPositionCalcProgress calcProgress,
        in DynamicBuffer<VertexElement> vertices,
        in DynamicBuffer<NormalElement> normals,
        ref DynamicBuffer<StaticObjectSpawnPosition> spawnPositions)
    {
        if (vertices.Length == 0 || normals.Length == 0)
            return;

        if (calcProgress.acceptedCount >= calcProgress.targetCount)
            return;

        int vPerSide = terrainConfig.verticesPerSide;
        int vertexCount = vPerSide * vPerSide;
        if (vertices.Length < vertexCount || normals.Length < vertexCount)
            return;

        float tileSize = terrainConfig.tileSize;
        float halfTileSize = tileSize * 0.5f;
        float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);

        var random = calcProgress.randomState != 0
            ? new Random(calcProgress.randomState)
            : new Random((uint)(tile.gridCoordinate.GetHashCode() + 12345));

        int objectCount = calcProgress.targetCount;
        int maxAttempts = objectCount * 3;
        int attempts = calcProgress.attempts;
        int acceptedCount = calcProgress.acceptedCount;
        int attemptsThisFrame = 0;

        if (spawnPositions.Capacity < objectCount)
            spawnPositions.Capacity = objectCount;

        var vertexPositions = new NativeArray<float3>(vertexCount, Allocator.Temp);
        var vertexNormals = new NativeArray<float3>(vertexCount, Allocator.Temp);
        for (int i = 0; i < vertexCount; i++)
        {
            vertexPositions[i] = vertices[i].value;
            vertexNormals[i] = normals[i].value;
        }

        byte activeTrailMask = 0;
        byte tileTrailMask = 0;
        float lutStep = 1f;
        float maxSearchRange = 0f;
        int lutLength = 0;
        float tileWorldX = tile.gridCoordinate.x * tileSize;
        float tileWorldZ = tile.gridCoordinate.y * tileSize;
        NativeArray<float> trailCenterlineLuts = default;

        if (hasTrailConfig)
        {
            lutStep = trailConfig.lutStepMeters > 0f ? trailConfig.lutStepMeters : 1f;
            activeTrailMask = TrailInfluenceBurst.GetActiveTrailMask(
                trailConfig.trail1, trailConfig.trail2, trailConfig.trail3);

            if (activeTrailMask != 0)
            {
                tileTrailMask = TrailInfluenceBurst.ComputeTileTrailMask(
                    tileWorldX, tileWorldZ, tileSize,
                    trailConfig.trail1, trailConfig.trail2, trailConfig.trail3,
                    activeTrailMask);

                if (tileTrailMask != 0)
                {
                    maxSearchRange = TrailInfluenceBurst.GetMaxSearchRangeAcrossTrails(
                        trailConfig.trail1, trailConfig.trail2, trailConfig.trail3, activeTrailMask);
                    lutLength = TrailInfluenceBurst.ComputeLutLength(tileSize, maxSearchRange, lutStep);
                    float lutZOrigin = TrailInfluenceBurst.ComputeLutZOrigin(tileWorldZ, maxSearchRange);

                    trailCenterlineLuts = new NativeArray<float>(lutLength * 3, Allocator.Temp);

                    if ((tileTrailMask & TrailMask.Trail1) != 0)
                    {
                        TrailInfluenceBurst.BuildTrailCenterlineLUT(
                            trailCenterlineLuts, 0, lutZOrigin, lutStep, lutLength, trailConfig.trail1);
                    }

                    if ((tileTrailMask & TrailMask.Trail2) != 0)
                    {
                        TrailInfluenceBurst.BuildTrailCenterlineLUT(
                            trailCenterlineLuts, lutLength, lutZOrigin, lutStep, lutLength, trailConfig.trail2);
                    }

                    if ((tileTrailMask & TrailMask.Trail3) != 0)
                    {
                        TrailInfluenceBurst.BuildTrailCenterlineLUT(
                            trailCenterlineLuts, lutLength * 2, lutZOrigin, lutStep, lutLength, trailConfig.trail3);
                    }
                }
            }
        }

        var trail1Lut = new TrailCenterlineLUT
        {
            offset = 0,
            length = lutLength,
            zOrigin = TrailInfluenceBurst.ComputeLutZOrigin(tileWorldZ, maxSearchRange),
            zStep = lutStep
        };
        var trail2Lut = new TrailCenterlineLUT
        {
            offset = lutLength,
            length = lutLength,
            zOrigin = trail1Lut.zOrigin,
            zStep = lutStep
        };
        var trail3Lut = new TrailCenterlineLUT
        {
            offset = lutLength * 2,
            length = lutLength,
            zOrigin = trail1Lut.zOrigin,
            zStep = lutStep
        };

        while (acceptedCount < objectCount && attempts < maxAttempts && attemptsThisFrame < attemptBudgetPerTile)
        {
            attempts++;
            attemptsThisFrame++;

            float randomX = random.NextFloat(0f, tileSize);
            float randomZ = random.NextFloat(0f, tileSize);

            float gridX = (randomX / tileSize) * (vPerSide - 1);
            float gridZ = (randomZ / tileSize) * (vPerSide - 1);

            int x0 = (int)math.floor(gridX);
            int z0 = (int)math.floor(gridZ);
            int x1 = math.min(x0 + 1, vPerSide - 1);
            int z1 = math.min(z0 + 1, vPerSide - 1);

            float tx = gridX - x0;
            float tz = gridZ - z0;

            int idx00 = z0 * vPerSide + x0;
            int idx10 = z0 * vPerSide + x1;
            int idx01 = z1 * vPerSide + x0;
            int idx11 = z1 * vPerSide + x1;

            float3 v00 = vertexPositions[idx00];
            float3 v10 = vertexPositions[idx10];
            float3 v01 = vertexPositions[idx01];
            float3 v11 = vertexPositions[idx11];

            float3 vX0 = math.lerp(v00, v10, tx);
            float3 vX1 = math.lerp(v01, v11, tx);
            float3 interpolatedPosition = math.lerp(vX0, vX1, tz);

            float3 localPosition = new float3(randomX - halfTileSize, interpolatedPosition.y, randomZ - halfTileSize);

            float3 n00 = vertexNormals[idx00];
            float3 n10 = vertexNormals[idx10];
            float3 n01 = vertexNormals[idx01];
            float3 n11 = vertexNormals[idx11];

            float3 nX0 = math.lerp(n00, n10, tx);
            float3 nX1 = math.lerp(n01, n11, tx);
            float3 normal = math.normalize(math.lerp(nX0, nX1, tz));

            float3 worldPosition = tileTransform.Position + localPosition;

            if (normal.y < config.slopeThreshold)
                continue;

            if (tileTrailMask != 0)
            {
                float noiseX = tileWorldX + randomX;
                float noiseZ = tileWorldZ + randomZ;

                if ((tileTrailMask & TrailMask.Trail1) != 0 &&
                    TrailInfluenceBurst.IsInsideTrailExclusionZoneFromLUT(
                        noiseX, noiseZ, trailConfig.trail1, trail1Lut, trailCenterlineLuts))
                    continue;

                if ((tileTrailMask & TrailMask.Trail2) != 0 &&
                    TrailInfluenceBurst.IsInsideTrailExclusionZoneFromLUT(
                        noiseX, noiseZ, trailConfig.trail2, trail2Lut, trailCenterlineLuts))
                    continue;

                if ((tileTrailMask & TrailMask.Trail3) != 0 &&
                    TrailInfluenceBurst.IsInsideTrailExclusionZoneFromLUT(
                        noiseX, noiseZ, trailConfig.trail3, trail3Lut, trailCenterlineLuts))
                    continue;
            }

            float typeRoll = random.NextFloat(0f, 1f);
            int objectTypeIndex = treeTypeCount - 1;
            for (int typeIndex = 0; typeIndex < treeTypeCount; typeIndex++)
            {
                if (typeRoll < objectTypeWeightPrefixSum[typeIndex])
                {
                    objectTypeIndex = typeIndex;
                    break;
                }
            }

            int prefabIndexLOD0 = objectTypeIndex * 3;
            quaternion prefabRotation = objectPrefabRotations[prefabIndexLOD0];

            quaternion randomYRotation = quaternion.RotateY(random.NextFloat(0f, math.PI * 2f));
            quaternion rotation = math.mul(randomYRotation, prefabRotation);

            var typeScale = objectTypeScales[objectTypeIndex];
            float scale = typeScale.baseScale;
            if (typeScale.maxScaleDelta > 0f)
                scale += random.NextFloat(-typeScale.maxScaleDelta, typeScale.maxScaleDelta) * typeScale.baseScale;
            scale = math.max(scale, 0.001f);

            byte initialLODLevel = 0;
            float initialDistance = 0f;

            if (hasLODConfig)
            {
                float2 objectPos2D = new float2(worldPosition.x, worldPosition.z);
                initialDistance = math.distance(objectPos2D, cameraPos2D);

                if (initialDistance >= lodConfig.lod1Distance)
                    initialLODLevel = 2;
                else if (initialDistance >= lodConfig.lod0Distance)
                    initialLODLevel = 1;
            }

            int initialMeshIndex = (objectTypeIndex * 3) + initialLODLevel;

            spawnPositions.Add(new StaticObjectSpawnPosition
            {
                localPosition = localPosition,
                worldPosition = worldPosition,
                rotation = rotation,
                objectTypeIndex = objectTypeIndex,
                initialLODLevel = initialLODLevel,
                initialDistance = initialDistance,
                initialMeshIndex = initialMeshIndex,
                scale = scale
            });

            acceptedCount++;
        }

        if (trailCenterlineLuts.IsCreated)
            trailCenterlineLuts.Dispose();

        vertexPositions.Dispose();
        vertexNormals.Dispose();

        calcProgress.acceptedCount = acceptedCount;
        calcProgress.attempts = attempts;
        calcProgress.randomState = random.state;
    }
}

[BurstCompile]
struct InstantiateStaticObjectsJob : IJobParallelForDefer
{
    public EntityCommandBuffer.ParallelWriter ecb;

    [ReadOnly] public NativeArray<StaticObjectSpawnWorkItem> workItems;
    [ReadOnly] public NativeArray<Entity> objectPrefabs;
    [ReadOnly] public NativeArray<StaticObjectTypeScaleElement> objectTypeScales;
    [ReadOnly] public NativeArray<bool> billboardTypes;
    [ReadOnly] public NativeArray<MaterialMeshInfo> lodMeshInfos;
    [ReadOnly] public NativeArray<Unity.Mathematics.AABB> lodRenderBounds;
    [ReadOnly] public NativeArray<Unity.Mathematics.AABB> objectTypeMaxRenderBounds;

    [ReadOnly] public BufferLookup<StaticObjectSpawnPosition> spawnPositionLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> tileTransformLookup;
    [ReadOnly] public float3 cameraPosition;
    [ReadOnly] public bool hasLODConfig;
    [ReadOnly] public float lod0Distance;
    [ReadOnly] public float lod1Distance;

    public void Execute(int index)
    {
        var work = workItems[index];
        if (!spawnPositionLookup.HasBuffer(work.tileEntity))
            return;

        if (!tileTransformLookup.HasComponent(work.tileEntity))
            return;

        float3 tilePosition = tileTransformLookup[work.tileEntity].Position;

        var spawnPositions = spawnPositionLookup[work.tileEntity];
        int endIndex = math.min(work.startIndex + work.count, spawnPositions.Length);
        if (endIndex <= work.startIndex)
            return;

        for (int i = work.startIndex; i < endIndex; i++)
        {
            var spawnData = spawnPositions[i];

            int prefabIndexLOD0 = spawnData.objectTypeIndex * 3;
            if (prefabIndexLOD0 >= objectPrefabs.Length)
                continue;

            Entity objectPrefab = objectPrefabs[prefabIndexLOD0];
            Entity objectEntity = ecb.Instantiate(index, objectPrefab);

            float3 worldPosition = tilePosition + spawnData.localPosition;

            var typeScale = spawnData.objectTypeIndex < objectTypeScales.Length
                ? objectTypeScales[spawnData.objectTypeIndex]
                : new StaticObjectTypeScaleElement { baseScale = 1f, lod1ScaleMultiplier = 1f, lod2ScaleMultiplier = 1f };
            float spawnScale = spawnData.scale;

            byte spawnLod = spawnData.initialLODLevel;
            float spawnDistance = spawnData.initialDistance;
            if (hasLODConfig)
            {
                float2 objectPos2D = new float2(worldPosition.x, worldPosition.z);
                float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);
                spawnDistance = math.distance(objectPos2D, cameraPos2D);

                if (spawnDistance >= lod1Distance)
                    spawnLod = 2;
                else if (spawnDistance >= lod0Distance)
                    spawnLod = 1;
                else
                    spawnLod = 0;
            }

            int meshIndex = (spawnData.objectTypeIndex * 3) + spawnLod;
            float displayScale = spawnScale * typeScale.GetLodScaleMultiplier(spawnLod);

            ecb.SetComponent(index, objectEntity, new LocalTransform
            {
                Position = worldPosition,
                Rotation = spawnData.rotation,
                Scale = displayScale
            });

            if (lodMeshInfos.Length > meshIndex)
                ecb.SetComponent(index, objectEntity, lodMeshInfos[meshIndex]);

            if (lodRenderBounds.IsCreated && lodRenderBounds.Length > meshIndex)
            {
                ecb.SetComponent(index, objectEntity, new RenderBounds
                {
                    Value = lodRenderBounds[meshIndex]
                });
            }
            else if (objectTypeMaxRenderBounds.IsCreated && spawnData.objectTypeIndex < objectTypeMaxRenderBounds.Length)
            {
                ecb.SetComponent(index, objectEntity, new RenderBounds
                {
                    Value = objectTypeMaxRenderBounds[spawnData.objectTypeIndex]
                });
            }

            bool isBillboard = spawnData.objectTypeIndex < billboardTypes.Length
                && billboardTypes[spawnData.objectTypeIndex];

            ecb.SetComponent(index, objectEntity, new GlobalStaticObjectInstanceData
            {
                prefabIndex = prefabIndexLOD0,
                objectTypeIndex = spawnData.objectTypeIndex,
                currentLODLevel = spawnLod,
                lastDistanceToPlayer = spawnDistance,
                isBillboardType = isBillboard,
                spawnScale = spawnScale
            });

            ecb.AddComponent(index, objectEntity, new StaticObjectTileOwnership
            {
                tileEntity = work.tileEntity,
                localOffset = spawnData.localPosition,
                localRotation = spawnData.rotation
            });

            ecb.AddComponent(index, objectEntity, new StaticObjectChunkMembership
            {
                chunkCoord = StaticObjectSpatialChunkUtility.GetChunkCoord(worldPosition)
            });

            ecb.AppendToBuffer(index, work.tileEntity, new SpawnedStaticObjectReference { objectEntity = objectEntity });
        }

        int nextIndex = endIndex;
        bool spawnComplete = nextIndex >= spawnPositions.Length;

        if (spawnComplete)
        {
            ecb.AddComponent<StaticObjectsSpawned>(index, work.tileEntity);
            ecb.RemoveComponent<StaticObjectPositionCalcProgress>(index, work.tileEntity);
            if (work.startIndex > 0)
                ecb.RemoveComponent<StaticObjectSpawnProgress>(index, work.tileEntity);
            ecb.SetBuffer<StaticObjectSpawnPosition>(index, work.tileEntity).Clear();
        }
        else if (work.startIndex == 0)
        {
            ecb.AddComponent(index, work.tileEntity, new StaticObjectSpawnProgress { nextSpawnIndex = nextIndex });
        }
        else
        {
            ecb.SetComponent(index, work.tileEntity, new StaticObjectSpawnProgress { nextSpawnIndex = nextIndex });
        }
    }
}
