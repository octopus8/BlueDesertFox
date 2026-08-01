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
/// OPTIMIZED: Burst-compiled system that spawns static object entities on terrain tiles after mesh generation.
/// Uses parallel jobs for position calculation and EntityCommandBuffer for batched structural changes.
/// Designed for Quest 3 VR performance with scrolling terrain and high object density.
/// Instantiation runs on the main thread after a Burst prepare job (no EndSimulation ECB).
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileScrollPositionSystem))]
public partial struct TerrainStaticObjectSpawningSystemOptimized : ISystem
{
    private bool _startupClearDone;
    /// <summary>
    /// Tracks SubScene-baked spawner config so reload resets spawn state even when
    /// OnStopRunning is skipped (AutoLoad SubScene never leaves RequireForUpdate empty).
    /// </summary>
    private Entity _trackedSpawnerConfigEntity;
#if UNITY_EDITOR
    private bool _loggedReloadSpawnStats;
    private static readonly ProfilerMarker s_PositionCalcMarker = new ProfilerMarker("StaticObjectSpawner.PositionCalc");
    private static readonly ProfilerMarker s_InstantiationMarker = new ProfilerMarker("StaticObjectSpawner.Instantiation");
#endif

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectSpawnerConfig>();
        state.RequireForUpdate<StaticObjectPrefabElement>();
        // Do not require StaticObjectLODMeshInfoReady — that tag is added in Presentation after BRG
        // registration. Waiting on it delayed/blocked spawn after SubScene reload. Instantiation uses
        // the matching LOD prefab's own RenderMeshArray/MaterialMeshInfo instead of the Ready cache.
        state.RequireForUpdate<CameraDataSingleton>();
        state.RequireForUpdate<TerrainTileConfig>();
        _trackedSpawnerConfigEntity = Entity.Null;
    }

    public void OnStopRunning(ref SystemState state)
    {
        _startupClearDone = false;
        _trackedSpawnerConfigEntity = Entity.Null;
#if UNITY_EDITOR
        _loggedReloadSpawnStats = false;
#endif
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<StaticObjectSpawnerConfig>();

        if (config.maxObjectsPerTile <= 0)
            return;

        var spawnerConfigEntity = SystemAPI.GetSingletonEntity<StaticObjectSpawnerConfig>();
        if (_trackedSpawnerConfigEntity != spawnerConfigEntity)
        {
            _startupClearDone = false;
            _trackedSpawnerConfigEntity = spawnerConfigEntity;
#if UNITY_EDITOR
            _loggedReloadSpawnStats = false;
#endif
        }

        if (!_startupClearDone)
        {
            _startupClearDone = true;
            // Unconditional: strip spawn tags / destroy leftover trees on any surviving tiles.
            // Previously trail-gated and only ran after OnStopRunning — both skipped on AutoLoad reload.
            ResetStaleTileSpawnState(ref state);
        }

        var configEntity = spawnerConfigEntity;
        var objectPrefabsBuffer = state.EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity, true);

        if (objectPrefabsBuffer.Length == 0)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.LogWarning("[StaticObjectSpawner] No object prefabs configured!");
#endif
            return;
        }

        var objectPrefabCount = objectPrefabsBuffer.Length;
        var treeTypeCount = objectPrefabCount / 3;

        if (treeTypeCount == 0)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.LogWarning($"[StaticObjectSpawner] Not enough prefabs for LOD system. Need at least 3, have {objectPrefabCount}");
#endif
            return;
        }

        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();

        StaticObjectLODConfig lodConfig = default;
        bool hasLODConfig = SystemAPI.HasSingleton<StaticObjectLODConfig>();
        if (hasLODConfig)
            lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();

        var tilesNeedingSpawnSetup = new NativeList<Entity>(16, Allocator.Temp);
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned, StaticObjectSpawnPosition>()
            .WithEntityAccess())
        {
            if (!tile.ValueRO.meshGenerated)
                continue;
            tilesNeedingSpawnSetup.Add(entity);
        }

        for (int i = 0; i < tilesNeedingSpawnSetup.Length; i++)
        {
            Entity entity = tilesNeedingSpawnSetup[i];
            var tile = state.EntityManager.GetComponentData<TerrainTile>(entity);
            uint seed = (uint)(tile.gridCoordinate.GetHashCode() + config.randomSeed);
            var random = new Random(seed);
            int targetCount = random.NextInt(config.minObjectsPerTile, config.maxObjectsPerTile + 1);

            state.EntityManager.AddBuffer<StaticObjectSpawnPosition>(entity);
            state.EntityManager.AddComponentData(entity, new StaticObjectPositionCalcProgress
            {
                targetCount = targetCount,
                acceptedCount = 0,
                attempts = 0,
                randomState = random.state
            });
        }
        tilesNeedingSpawnSetup.Dispose();

        int tilesNeedingPositionCalc = 0;
        bool hasReadyToInstantiate = false;
        foreach (var (calcProgress, spawnPositions, _) in SystemAPI.Query<RefRO<StaticObjectPositionCalcProgress>, DynamicBuffer<StaticObjectSpawnPosition>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned>()
            .WithEntityAccess())
        {
            int objectCount = calcProgress.ValueRO.targetCount;
            int attemptMultiplier = 3;
            if (SystemAPI.HasSingleton<TrailConfig>())
            {
                var trailCfg = SystemAPI.GetSingleton<TrailConfig>();
                if ((trailCfg.trail1.enabled || trailCfg.trail2.enabled || trailCfg.trail3.enabled) &&
                    config.trailSpawnDensityMultiplier < 1f)
                    attemptMultiplier = 12;
            }
            int maxAttempts = math.max(1, objectCount * attemptMultiplier);
            bool calcExhausted = calcProgress.ValueRO.attempts >= maxAttempts;
            bool calcComplete = calcProgress.ValueRO.acceptedCount >= objectCount;

            if (!calcComplete && !calcExhausted)
                tilesNeedingPositionCalc++;
            else if (spawnPositions.Length > 0)
                hasReadyToInstantiate = true;
            else if (calcExhausted)
                hasReadyToInstantiate = true; // will mark StaticObjectsSpawned with no objects
        }

        var tilesNeedingCalcProgress = new NativeList<Entity>(16, Allocator.Temp);
        var calcProgressTargets = new NativeList<int>(16, Allocator.Temp);
        var calcProgressAccepted = new NativeList<int>(16, Allocator.Temp);
        var calcProgressRandom = new NativeList<uint>(16, Allocator.Temp);
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference, StaticObjectSpawnPosition>()
            .WithNone<StaticObjectsSpawned, StaticObjectPositionCalcProgress>()
            .WithEntityAccess())
        {
            if (!tile.ValueRO.meshGenerated)
                continue;

            uint seed = (uint)(tile.ValueRO.gridCoordinate.GetHashCode() + config.randomSeed);
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

            if (acceptedCount >= targetCount && spawnPositions.Length > 0)
                hasReadyToInstantiate = true;

            tilesNeedingCalcProgress.Add(entity);
            calcProgressTargets.Add(targetCount);
            calcProgressAccepted.Add(acceptedCount);
            calcProgressRandom.Add(randomState);
        }

        for (int i = 0; i < tilesNeedingCalcProgress.Length; i++)
        {
            state.EntityManager.AddComponentData(tilesNeedingCalcProgress[i], new StaticObjectPositionCalcProgress
            {
                targetCount = calcProgressTargets[i],
                acceptedCount = calcProgressAccepted[i],
                attempts = 0,
                randomState = calcProgressRandom[i]
            });
        }
        tilesNeedingCalcProgress.Dispose();
        calcProgressTargets.Dispose();
        calcProgressAccepted.Dispose();
        calcProgressRandom.Dispose();

        bool hasInstantiationInProgress = !SystemAPI.QueryBuilder()
            .WithAll<StaticObjectSpawnProgress, StaticObjectSpawnPosition>()
            .WithNone<StaticObjectsSpawned>()
            .Build()
            .IsEmpty;

        if (tilesNeedingPositionCalc == 0 && !hasInstantiationInProgress && !hasReadyToInstantiate)
        {
            return;
        }

        var terrainConfig = SystemAPI.GetSingleton<TerrainTileConfig>();

        // Re-acquire after tile structural changes above — DynamicBuffer handles are invalidated.
        objectPrefabsBuffer = state.EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity, true);
        var objectPrefabs = new NativeArray<Entity>(objectPrefabCount, Allocator.TempJob);
        var objectPrefabRotations = new NativeArray<quaternion>(objectPrefabCount, Allocator.TempJob);
        for (int i = 0; i < objectPrefabCount; i++)
        {
            objectPrefabs[i] = objectPrefabsBuffer[i].prefabEntity;
            if (state.EntityManager.HasComponent<LocalTransform>(objectPrefabs[i]))
                objectPrefabRotations[i] = state.EntityManager.GetComponentData<LocalTransform>(objectPrefabs[i]).Rotation;
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

        var typeSlopeBuffer = state.EntityManager.GetBuffer<StaticObjectTypeSlopeElement>(configEntity, true);
        var objectTypeSlopes = new NativeArray<StaticObjectTypeSlopeElement>(treeTypeCount, Allocator.TempJob);
        // Default matches prior global maxSlopeDegrees=45 (cos 45° ≈ 0.707), minSlope=0 (cos 0° = 1)
        var defaultTypeSlope = new StaticObjectTypeSlopeElement
        {
            minSlopeThreshold = 0.70710678f,
            maxSlopeThreshold = 1f
        };
        for (int i = 0; i < treeTypeCount; i++)
            objectTypeSlopes[i] = i < typeSlopeBuffer.Length ? typeSlopeBuffer[i] : defaultTypeSlope;

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
        bool hasTrailConfig = SystemAPI.HasSingleton<TrailConfig>();
        if (hasTrailConfig)
            trailConfig = SystemAPI.GetSingleton<TrailConfig>();
        if (SystemAPI.HasSingleton<TrailPathConfig>())
            trailPath = TrailInfluenceBurst.NormalizeTrailPathSettings(
                SystemAPI.GetSingleton<TrailPathConfig>());

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
                    objectTypeSlopes = objectTypeSlopes,
                    objectTypeWeightPrefixSum = objectTypeWeightPrefixSum,
                    trailConfig = trailConfig,
                    trailPath = trailPath,
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
        var giveUpTiles = new NativeList<Entity>(8, Allocator.Temp);

        int trailAttemptMultiplier = 3;
        if (hasTrailConfig && config.trailSpawnDensityMultiplier < 1f &&
            (trailConfig.trail1.enabled || trailConfig.trail2.enabled || trailConfig.trail3.enabled))
            trailAttemptMultiplier = 12;

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
            int objectCount = calcProgress.ValueRO.targetCount;
            int maxAttempts = math.max(1, objectCount * trailAttemptMultiplier);
            bool calcExhausted = calcProgress.ValueRO.attempts >= maxAttempts;
            bool calcComplete = calcProgress.ValueRO.acceptedCount >= objectCount;

            // Instantiate partial results when maxAttempts is hit; previously these tiles were stuck
            // forever with no trees (common on trail tiles after reload when the trail is already aligned).
            if (!calcComplete && !calcExhausted)
                continue;

            if (spawnPositions.Length == 0)
            {
                if (calcExhausted)
                    giveUpTiles.Add(entity);
                continue;
            }

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

        if (giveUpTiles.Length > 0)
        {
            // Main-thread structural changes — avoid deferred RemoveComponent races during reload/remesh.
            var em = state.EntityManager;
            for (int i = 0; i < giveUpTiles.Length; i++)
            {
                Entity tileEntity = giveUpTiles[i];
                if (!em.Exists(tileEntity))
                    continue;

                if (!em.HasComponent<StaticObjectsSpawned>(tileEntity))
                    em.AddComponent<StaticObjectsSpawned>(tileEntity);
                if (em.HasComponent<StaticObjectPositionCalcProgress>(tileEntity))
                    em.RemoveComponent<StaticObjectPositionCalcProgress>(tileEntity);
                if (em.HasComponent<StaticObjectSpawnProgress>(tileEntity))
                    em.RemoveComponent<StaticObjectSpawnProgress>(tileEntity);
            }
        }
        giveUpTiles.Dispose();

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

#if UNITY_EDITOR
        if (!_loggedReloadSpawnStats)
        {
            int meshRefCount = 0;
            int spawnedTagCount = 0;
            int readyPosCount = 0;
            foreach (var _ in SystemAPI.Query<RefRO<TerrainTile>>().WithAll<MeshReference>())
                meshRefCount++;
            foreach (var _ in SystemAPI.Query<RefRO<TerrainTile>>().WithAll<StaticObjectsSpawned>())
                spawnedTagCount++;
            foreach (var (calc, positions) in SystemAPI
                         .Query<RefRO<StaticObjectPositionCalcProgress>, DynamicBuffer<StaticObjectSpawnPosition>>()
                         .WithNone<StaticObjectsSpawned>())
            {
                if (positions.Length > 0 || calc.ValueRO.attempts > 0)
                    readyPosCount++;
            }

            if (meshRefCount > 0)
            {
                int treeCount = 0;
                foreach (var _ in SystemAPI.Query<RefRO<GlobalStaticObjectInstance>>())
                    treeCount++;

                UnityEngine.Debug.Log(
                    $"[StaticObjectSpawner] meshRefTiles={meshRefCount} spawnedTags={spawnedTagCount} " +
                    $"calcInProgress={readyPosCount} workItems={workItems.Length} trees={treeCount}");

                if (spawnedTagCount > 0 || treeCount > 0 || workItems.Length > 0)
                    _loggedReloadSpawnStats = true;
            }
        }
#endif

        if (workItems.Length == 0)
        {
            objectPrefabs.Dispose();
            objectPrefabRotations.Dispose();
            objectTypeScales.Dispose();
            objectTypeSlopes.Dispose();
            objectTypeWeightPrefixSum.Dispose();
            billboardTypes.Dispose();
            workItems.Dispose();
            return;
        }

        var spawnPositionLookup = SystemAPI.GetBufferLookup<StaticObjectSpawnPosition>(true);
        spawnPositionLookup.Update(ref state);

        var tileTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        tileTransformLookup.Update(ref state);

        var hierarchicalPrefabs = new NativeArray<bool>(objectPrefabCount, Allocator.TempJob);
        for (int i = 0; i < objectPrefabCount; i++)
        {
            hierarchicalPrefabs[i] = state.EntityManager.HasComponent<PendingStaticObjectRendererStrip>(
                objectPrefabs[i]);
        }

        var spawnRequests = new NativeQueue<StaticObjectSpawnInstanceRequest>(Allocator.TempJob);
        var tileSpawnResults = new NativeQueue<TileSpawnJobResult>(Allocator.TempJob);

#if UNITY_EDITOR
        using (s_InstantiationMarker.Auto())
#endif
        {
            var prepareJob = new PrepareStaticObjectSpawnsJob
            {
                workItems = workItems.AsDeferredJobArray(),
                objectPrefabs = objectPrefabs,
                hierarchicalPrefabs = hierarchicalPrefabs,
                objectTypeScales = objectTypeScales,
                billboardTypes = billboardTypes,
                spawnPositionLookup = spawnPositionLookup,
                tileTransformLookup = tileTransformLookup,
                cameraPosition = cameraData.position,
                hasLODConfig = hasLODConfig,
                lod0Distance = hasLODConfig ? lodConfig.lod0Distance : 0f,
                lod1Distance = hasLODConfig ? lodConfig.lod1Distance : 0f,
                spawnRequests = spawnRequests.AsParallelWriter(),
                results = tileSpawnResults.AsParallelWriter()
            };

            state.Dependency = prepareJob.Schedule(workItems, 1, state.Dependency);
            state.Dependency.Complete();
        }

        // Instantiate on the main thread — EndSimulation ECB SetComponent/AppendToBuffer
        // aborts after reload when prefab archetypes or tile buffers differ from bake-time assumptions.
        ApplySpawnInstanceRequests(ref state, spawnRequests);
        spawnRequests.Dispose();

        ApplyTileSpawnResults(ref state, tileSpawnResults);
        tileSpawnResults.Dispose();

        objectPrefabs.Dispose();
        hierarchicalPrefabs.Dispose();
        objectPrefabRotations.Dispose();
        objectTypeScales.Dispose();
        objectTypeSlopes.Dispose();
        objectTypeWeightPrefixSum.Dispose();
        billboardTypes.Dispose();
        workItems.Dispose();
    }

    /// <summary>
    /// Clears spawn completion tags and destroys leftover trees on surviving runtime tiles.
    /// Used when SubScene reload skips OnStopRunning but tiles remain in the Default World.
    /// </summary>
    private void ResetStaleTileSpawnState(ref SystemState state)
    {
        var em = state.EntityManager;
        using var query = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainTile>());
        var tiles = query.ToEntityArray(Allocator.Temp);

#if UNITY_EDITOR
        if (tiles.Length > 0)
            UnityEngine.Debug.Log($"[StaticObjectSpawner] Resetting spawn state on {tiles.Length} surviving tile(s)");
#endif

        for (int i = 0; i < tiles.Length; i++)
        {
            Entity tileEntity = tiles[i];
            if (!em.Exists(tileEntity))
                continue;

            if (em.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
            {
                var spawnedObjects = em.GetBuffer<SpawnedStaticObjectReference>(tileEntity);
                var objectEntities = new NativeArray<Entity>(spawnedObjects.Length, Allocator.Temp);
                for (int objIdx = 0; objIdx < spawnedObjects.Length; objIdx++)
                    objectEntities[objIdx] = spawnedObjects[objIdx].objectEntity;

                for (int objIdx = 0; objIdx < objectEntities.Length; objIdx++)
                {
                    StaticObjectHierarchyDestroyUtility.DestroyHierarchyImmediate(
                        objectEntities[objIdx], em);
                }
                objectEntities.Dispose();
                em.GetBuffer<SpawnedStaticObjectReference>(tileEntity).Clear();
            }

            if (em.HasComponent<StaticObjectsSpawned>(tileEntity))
                em.RemoveComponent<StaticObjectsSpawned>(tileEntity);
            if (em.HasComponent<StaticObjectPositionCalcProgress>(tileEntity))
                em.RemoveComponent<StaticObjectPositionCalcProgress>(tileEntity);
            if (em.HasComponent<StaticObjectSpawnProgress>(tileEntity))
                em.RemoveComponent<StaticObjectSpawnProgress>(tileEntity);
            if (em.HasBuffer<StaticObjectSpawnPosition>(tileEntity))
                em.GetBuffer<StaticObjectSpawnPosition>(tileEntity).Clear();
        }

        tiles.Dispose();
    }

    private static void ApplySpawnInstanceRequests(
        ref SystemState state,
        NativeQueue<StaticObjectSpawnInstanceRequest> requests)
    {
        var em = state.EntityManager;
        while (requests.TryDequeue(out var req))
        {
            if (!em.Exists(req.prefab) || !em.Exists(req.tileEntity))
                continue;

            Entity objectEntity = em.Instantiate(req.prefab);

            if (em.HasComponent<LocalTransform>(objectEntity))
                em.SetComponentData(objectEntity, req.transform);
            else
                em.AddComponentData(objectEntity, req.transform);

            if (em.HasComponent<LocalToWorld>(objectEntity))
                em.SetComponentData(objectEntity, StaticObjectHierarchyFlattenUtility.LocalToWorldFromLocalTransform(req.transform));
            else
                em.AddComponentData(objectEntity, StaticObjectHierarchyFlattenUtility.LocalToWorldFromLocalTransform(req.transform));

            if (req.addDisableRendering && !em.HasComponent<DisableRendering>(objectEntity))
                em.AddComponent<DisableRendering>(objectEntity);

            if (em.HasComponent<GlobalStaticObjectInstanceData>(objectEntity))
                em.SetComponentData(objectEntity, req.instanceData);
            else
                em.AddComponentData(objectEntity, req.instanceData);

            if (!em.HasComponent<GlobalStaticObjectInstance>(objectEntity))
                em.AddComponent<GlobalStaticObjectInstance>(objectEntity);

            if (!em.HasComponent<StaticObjectTileOwnership>(objectEntity))
            {
                em.AddComponentData(objectEntity, new StaticObjectTileOwnership
                {
                    tileEntity = req.tileEntity,
                    localOffset = req.localOffset,
                    localRotation = req.localRotation
                });
            }
            else
            {
                em.SetComponentData(objectEntity, new StaticObjectTileOwnership
                {
                    tileEntity = req.tileEntity,
                    localOffset = req.localOffset,
                    localRotation = req.localRotation
                });
            }

            if (!em.HasComponent<StaticObjectChunkMembership>(objectEntity))
            {
                em.AddComponentData(objectEntity, new StaticObjectChunkMembership
                {
                    chunkCoord = StaticObjectSpatialChunkUtility.GetChunkCoord(req.transform.Position)
                });
            }
            else
            {
                em.SetComponentData(objectEntity, new StaticObjectChunkMembership
                {
                    chunkCoord = StaticObjectSpatialChunkUtility.GetChunkCoord(req.transform.Position)
                });
            }

            if (em.HasBuffer<SpawnedStaticObjectReference>(req.tileEntity))
                em.GetBuffer<SpawnedStaticObjectReference>(req.tileEntity).Add(
                    new SpawnedStaticObjectReference { objectEntity = objectEntity });
        }
    }

    private static void ApplyTileSpawnResults(ref SystemState state, NativeQueue<TileSpawnJobResult> results)
    {
        var em = state.EntityManager;
        while (results.TryDequeue(out var result))
        {
            if (!em.Exists(result.tileEntity))
                continue;

            if ((result.flags & TileSpawnJobResult.Complete) != 0)
            {
                if (!em.HasComponent<StaticObjectsSpawned>(result.tileEntity))
                    em.AddComponent<StaticObjectsSpawned>(result.tileEntity);
                if (em.HasComponent<StaticObjectPositionCalcProgress>(result.tileEntity))
                    em.RemoveComponent<StaticObjectPositionCalcProgress>(result.tileEntity);
                if (em.HasComponent<StaticObjectSpawnProgress>(result.tileEntity))
                    em.RemoveComponent<StaticObjectSpawnProgress>(result.tileEntity);
                if (em.HasBuffer<StaticObjectSpawnPosition>(result.tileEntity))
                    em.GetBuffer<StaticObjectSpawnPosition>(result.tileEntity).Clear();
            }
            else if ((result.flags & TileSpawnJobResult.PartialStarted) != 0)
            {
                if (em.HasComponent<StaticObjectPositionCalcProgress>(result.tileEntity))
                    em.RemoveComponent<StaticObjectPositionCalcProgress>(result.tileEntity);
                if (em.HasComponent<StaticObjectSpawnProgress>(result.tileEntity))
                    em.SetComponentData(result.tileEntity, new StaticObjectSpawnProgress { nextSpawnIndex = result.nextSpawnIndex });
                else
                    em.AddComponentData(result.tileEntity, new StaticObjectSpawnProgress { nextSpawnIndex = result.nextSpawnIndex });
            }
            else if ((result.flags & TileSpawnJobResult.PartialContinued) != 0)
            {
                if (em.HasComponent<StaticObjectSpawnProgress>(result.tileEntity))
                    em.SetComponentData(result.tileEntity, new StaticObjectSpawnProgress { nextSpawnIndex = result.nextSpawnIndex });
                else
                    em.AddComponentData(result.tileEntity, new StaticObjectSpawnProgress { nextSpawnIndex = result.nextSpawnIndex });
            }
        }
    }
}

struct TileSpawnJobResult
{
    public const byte Complete = 1;
    public const byte PartialStarted = 2;
    public const byte PartialContinued = 4;

    public Entity tileEntity;
    public int nextSpawnIndex;
    public byte flags;
}

struct StaticObjectSpawnInstanceRequest
{
    public Entity tileEntity;
    public Entity prefab;
    public LocalTransform transform;
    public GlobalStaticObjectInstanceData instanceData;
    public float3 localOffset;
    public quaternion localRotation;
    public bool addDisableRendering;
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
    [ReadOnly] public NativeArray<StaticObjectTypeSlopeElement> objectTypeSlopes;
    [ReadOnly] public NativeArray<float> objectTypeWeightPrefixSum;
    [ReadOnly] public TrailConfig trailConfig;
    [ReadOnly] public TrailPathConfig trailPath;
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
            : new Random((uint)(tile.gridCoordinate.GetHashCode() + config.randomSeed));

        int objectCount = calcProgress.targetCount;
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
                    tileWorldX, tileWorldZ, tileSize, trailConfig, trailPath, activeTrailMask);

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
                            trailCenterlineLuts, 0, lutZOrigin, lutStep, lutLength,
                            trailConfig.trail1, trailPath);
                    }

                    if ((tileTrailMask & TrailMask.Trail2) != 0)
                    {
                        TrailInfluenceBurst.BuildTrailCenterlineLUT(
                            trailCenterlineLuts, lutLength, lutZOrigin, lutStep, lutLength,
                            trailConfig.trail2, trailPath);
                    }

                    if ((tileTrailMask & TrailMask.Trail3) != 0)
                    {
                        TrailInfluenceBurst.BuildTrailCenterlineLUT(
                            trailCenterlineLuts, lutLength * 2, lutZOrigin, lutStep, lutLength,
                            trailConfig.trail3, trailPath);
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

        int attemptMultiplier = 3;
        if (tileTrailMask != 0 && config.trailSpawnDensityMultiplier < 1f)
            attemptMultiplier = 12;
        int maxAttempts = objectCount * attemptMultiplier;

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

            if (tileTrailMask != 0 && config.trailSpawnDensityMultiplier < 1f)
            {
                float noiseX = tileWorldX + randomX;
                float noiseZ = tileWorldZ + randomZ;
                float maxInfluence = 0f;

                if ((tileTrailMask & TrailMask.Trail1) != 0)
                {
                    maxInfluence = math.max(maxInfluence,
                        TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                            noiseX, noiseZ, trailConfig.trail1, trail1Lut, trailCenterlineLuts).influence);
                }

                if ((tileTrailMask & TrailMask.Trail2) != 0)
                {
                    maxInfluence = math.max(maxInfluence,
                        TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                            noiseX, noiseZ, trailConfig.trail2, trail2Lut, trailCenterlineLuts).influence);
                }

                if ((tileTrailMask & TrailMask.Trail3) != 0)
                {
                    maxInfluence = math.max(maxInfluence,
                        TrailInfluenceBurst.ComputeTrailInfluenceFromLUT(
                            noiseX, noiseZ, trailConfig.trail3, trail3Lut, trailCenterlineLuts).influence);
                }

                if (maxInfluence > 0f)
                {
                    float acceptChance = math.lerp(1f, config.trailSpawnDensityMultiplier, maxInfluence);
                    if (random.NextFloat() > acceptChance)
                        continue;
                }
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

            var slope = objectTypeSlopes[objectTypeIndex];
            if (normal.y < slope.minSlopeThreshold || normal.y > slope.maxSlopeThreshold)
                continue;

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
struct PrepareStaticObjectSpawnsJob : IJobParallelForDefer
{
    public NativeQueue<StaticObjectSpawnInstanceRequest>.ParallelWriter spawnRequests;
    public NativeQueue<TileSpawnJobResult>.ParallelWriter results;

    [ReadOnly] public NativeArray<StaticObjectSpawnWorkItem> workItems;
    [ReadOnly] public NativeArray<Entity> objectPrefabs;
    [ReadOnly] public NativeArray<bool> hierarchicalPrefabs;
    [ReadOnly] public NativeArray<StaticObjectTypeScaleElement> objectTypeScales;
    [ReadOnly] public NativeArray<bool> billboardTypes;

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

            int meshIndex = prefabIndexLOD0 + spawnLod;
            if (meshIndex >= objectPrefabs.Length)
                meshIndex = prefabIndexLOD0;

            Entity objectPrefab = objectPrefabs[meshIndex];
            float displayScale = spawnScale * typeScale.GetLodScaleMultiplier(spawnLod);

            bool isBillboard = spawnData.objectTypeIndex < billboardTypes.Length
                && billboardTypes[spawnData.objectTypeIndex];

            spawnRequests.Enqueue(new StaticObjectSpawnInstanceRequest
            {
                tileEntity = work.tileEntity,
                prefab = objectPrefab,
                transform = new LocalTransform
                {
                    Position = worldPosition,
                    Rotation = spawnData.rotation,
                    Scale = displayScale
                },
                instanceData = new GlobalStaticObjectInstanceData
                {
                    prefabIndex = prefabIndexLOD0,
                    objectTypeIndex = spawnData.objectTypeIndex,
                    currentLODLevel = spawnLod,
                    lastDistanceToPlayer = spawnDistance,
                    isBillboardType = isBillboard,
                    spawnScale = spawnScale
                },
                localOffset = spawnData.localPosition,
                localRotation = spawnData.rotation,
                addDisableRendering = meshIndex < hierarchicalPrefabs.Length && hierarchicalPrefabs[meshIndex]
            });
        }

        int nextIndex = endIndex;
        bool spawnComplete = nextIndex >= spawnPositions.Length;

        if (spawnComplete)
        {
            results.Enqueue(new TileSpawnJobResult
            {
                tileEntity = work.tileEntity,
                nextSpawnIndex = nextIndex,
                flags = TileSpawnJobResult.Complete
            });
        }
        else if (work.startIndex == 0)
        {
            results.Enqueue(new TileSpawnJobResult
            {
                tileEntity = work.tileEntity,
                nextSpawnIndex = nextIndex,
                flags = TileSpawnJobResult.PartialStarted
            });
        }
        else
        {
            results.Enqueue(new TileSpawnJobResult
            {
                tileEntity = work.tileEntity,
                nextSpawnIndex = nextIndex,
                flags = TileSpawnJobResult.PartialContinued
            });
        }
    }
}
