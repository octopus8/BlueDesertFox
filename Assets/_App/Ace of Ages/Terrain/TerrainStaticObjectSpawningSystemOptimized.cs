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
    private NativeQueue<Entity> _pendingTiles;
    private NativeHashSet<Entity> _queuedEntities;

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

        _pendingTiles = new NativeQueue<Entity>(Allocator.Persistent);
        _queuedEntities = new NativeHashSet<Entity>(64, Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_pendingTiles.IsCreated)
            _pendingTiles.Dispose();
        if (_queuedEntities.IsCreated)
            _queuedEntities.Dispose();
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
                    }
                    _pendingTiles.Clear();
                    _queuedEntities.Clear();
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

        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference, StaticObjectSpawnPosition>()
            .WithNone<StaticObjectsSpawned, StaticObjectSpawnProgress>()
            .WithEntityAccess())
        {
            if (tile.ValueRO.meshGenerated && _queuedEntities.Add(entity))
                _pendingTiles.Enqueue(entity);
        }

        var ecbSystem = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecbForBuffers = ecbSystem.CreateCommandBuffer(state.WorldUnmanaged);

        int tilesNeedingBuffer = 0;
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned, StaticObjectSpawnPosition>()
            .WithEntityAccess())
        {
            if (tile.ValueRO.meshGenerated)
            {
                ecbForBuffers.AddBuffer<StaticObjectSpawnPosition>(entity);
                tilesNeedingBuffer++;
            }
        }

        bool hasInProgress = !SystemAPI.QueryBuilder()
            .WithAll<StaticObjectSpawnProgress, StaticObjectSpawnPosition>()
            .WithNone<StaticObjectsSpawned>()
            .Build()
            .IsEmpty;

        bool hasPending = _pendingTiles.Count > 0;

        if (!hasInProgress && !hasPending)
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
        var objectTypeSpawnWeights = new NativeArray<float>(treeTypeCount, Allocator.TempJob);
        float equalTypeWeight = treeTypeCount > 0 ? 1f / treeTypeCount : 1f;
        for (int i = 0; i < treeTypeCount; i++)
        {
            objectTypeSpawnWeights[i] = i < typeSpawnWeightsBuffer.Length
                ? typeSpawnWeightsBuffer[i].weight
                : equalTypeWeight;
        }

        var billboardTypeBuffer = state.EntityManager.GetBuffer<StaticObjectBillboardTypeElement>(configEntity, true);
        var billboardTypes = new NativeArray<bool>(treeTypeCount, Allocator.TempJob);
        for (int i = 0; i < treeTypeCount; i++)
            billboardTypes[i] = i < billboardTypeBuffer.Length && billboardTypeBuffer[i].isBillboard;

#if UNITY_EDITOR
        using (s_PositionCalcMarker.Auto())
#endif
        {
            TrailConfig trailConfig = default;
            bool hasTrailConfig = SystemAPI.HasSingleton<TrailConfig>();
            if (hasTrailConfig)
                trailConfig = SystemAPI.GetSingleton<TrailConfig>();

            var positionJob = new CalculateStaticObjectSpawnPositionsJob
            {
                config = config,
                lodConfig = lodConfig,
                hasLODConfig = hasLODConfig,
                terrainConfig = terrainConfig,
                cameraPosition = cameraData.position,
                treeTypeCount = treeTypeCount,
                objectPrefabRotations = objectPrefabRotations,
                objectTypeSpawnWeights = objectTypeSpawnWeights,
                trailConfig = trailConfig,
                hasTrailConfig = hasTrailConfig
            };

            state.Dependency = positionJob.ScheduleParallel(state.Dependency);
        }

        state.Dependency.Complete();

        int budget = config.maxObjectsSpawnedPerFrame;
        var workItems = new NativeList<StaticObjectSpawnWorkItem>(16, Allocator.TempJob);
        var em = state.EntityManager;
        float nearTileSpawnDistance = hasLODConfig ? lodConfig.lod2Distance : terrainConfig.viewDistance * 0.5f;
        float2 cameraPos2D = new float2(cameraData.position.x, cameraData.position.z);

        foreach (var (progress, spawnPositions, entity) in SystemAPI.Query<RefRO<StaticObjectSpawnProgress>, DynamicBuffer<StaticObjectSpawnPosition>>()
            .WithNone<StaticObjectsSpawned>()
            .WithEntityAccess())
        {
            if (budget <= 0)
                break;

            int remaining = spawnPositions.Length - progress.ValueRO.nextSpawnIndex;
            if (remaining <= 0)
                continue;

            int count = GetSpawnCountForTile(em, entity, remaining, budget, nearTileSpawnDistance, cameraPos2D);
            if (count <= 0)
                continue;

            workItems.Add(new StaticObjectSpawnWorkItem
            {
                tileEntity = entity,
                startIndex = progress.ValueRO.nextSpawnIndex,
                count = count
            });
            budget -= count;
        }

        int pendingCount = _pendingTiles.Count;
        for (int p = 0; p < pendingCount && budget > 0; p++)
        {
            Entity tileEntity = _pendingTiles.Dequeue();

            if (!em.Exists(tileEntity) || em.HasComponent<StaticObjectsSpawned>(tileEntity))
            {
                _queuedEntities.Remove(tileEntity);
                continue;
            }

            if (em.HasComponent<StaticObjectSpawnProgress>(tileEntity))
                continue;

            if (!em.HasBuffer<StaticObjectSpawnPosition>(tileEntity))
                continue;

            var spawnPositions = em.GetBuffer<StaticObjectSpawnPosition>(tileEntity, true);
            if (spawnPositions.Length == 0)
            {
                _pendingTiles.Enqueue(tileEntity);
                continue;
            }

            int count = GetSpawnCountForTile(em, tileEntity, spawnPositions.Length, budget, nearTileSpawnDistance, cameraPos2D);
            if (count <= 0)
            {
                _pendingTiles.Enqueue(tileEntity);
                continue;
            }

            workItems.Add(new StaticObjectSpawnWorkItem
            {
                tileEntity = tileEntity,
                startIndex = 0,
                count = count
            });
            budget -= count;
            _queuedEntities.Remove(tileEntity);
        }

        if (workItems.Length == 0)
        {
            objectPrefabs.Dispose();
            objectPrefabRotations.Dispose();
            objectTypeSpawnWeights.Dispose();
            billboardTypes.Dispose();
            workItems.Dispose();
            return;
        }

        if (config.enableSpawnerDebug)
        {
            int totalInstances = 0;
            for (int i = 0; i < workItems.Length; i++)
                totalInstances += workItems[i].count;
            UnityEngine.Debug.Log($"[TreeSpawnerOptimized] Instantiating {totalInstances} objects across {workItems.Length} tile batches (budget: {config.maxObjectsSpawnedPerFrame})");
        }

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var spawnPositionLookup = SystemAPI.GetBufferLookup<StaticObjectSpawnPosition>(true);
        spawnPositionLookup.Update(ref state);

        var tileTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        tileTransformLookup.Update(ref state);

        var lodInfoBuffer = state.EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity, true);
        var lodMeshInfos = new NativeArray<MaterialMeshInfo>(lodInfoBuffer.Length, Allocator.TempJob);
        for (int i = 0; i < lodInfoBuffer.Length; i++)
            lodMeshInfos[i] = lodInfoBuffer[i].materialMeshInfo;

#if UNITY_EDITOR
        using (s_InstantiationMarker.Auto())
#endif
        {
            var instantiateJob = new InstantiateStaticObjectsJob
            {
                ecb = ecb.AsParallelWriter(),
                workItems = workItems.AsArray(),
                objectPrefabs = objectPrefabs,
                billboardTypes = billboardTypes,
                lodMeshInfos = lodMeshInfos,
                spawnPositionLookup = spawnPositionLookup,
                tileTransformLookup = tileTransformLookup
            };

            state.Dependency = instantiateJob.Schedule(workItems.Length, 1, state.Dependency);
        }

        objectPrefabs.Dispose(state.Dependency);
        objectPrefabRotations.Dispose();
        objectTypeSpawnWeights.Dispose();
        billboardTypes.Dispose(state.Dependency);
        lodMeshInfos.Dispose(state.Dependency);
        workItems.Dispose(state.Dependency);
    }

    private static int GetSpawnCountForTile(
        EntityManager em,
        Entity tileEntity,
        int remaining,
        int budget,
        float nearTileSpawnDistance,
        float2 cameraPos2D)
    {
        if (remaining <= 0 || budget <= 0)
            return 0;

        if (em.HasComponent<LocalTransform>(tileEntity))
        {
            var tilePos = em.GetComponentData<LocalTransform>(tileEntity).Position;
            float tileDist = math.distance(cameraPos2D, new float2(tilePos.x, tilePos.z));
            if (tileDist <= nearTileSpawnDistance)
                return remaining;
        }

        return math.min(remaining, budget);
    }
}

[BurstCompile]
[WithAll(typeof(MeshReference))]
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
    [ReadOnly] public NativeArray<float> objectTypeSpawnWeights;
    [ReadOnly] public TrailConfig trailConfig;
    [ReadOnly] public bool hasTrailConfig;

    private void Execute(
        in TerrainTile tile,
        in LocalTransform tileTransform,
        in DynamicBuffer<VertexElement> vertices,
        in DynamicBuffer<NormalElement> normals,
        ref DynamicBuffer<StaticObjectSpawnPosition> spawnPositions)
    {
        if (vertices.Length == 0 || normals.Length == 0)
            return;

        if (spawnPositions.Length > 0)
            return;

        var random = new Random((uint)(tile.gridCoordinate.GetHashCode() + 12345));

        int objectCount = random.NextInt(config.minObjectsPerTile, config.maxObjectsPerTile + 1);

        int actualStaticObjectsSpawned = 0;
        int maxAttempts = objectCount * 3;
        int attempts = 0;

        int vPerSide = terrainConfig.verticesPerSide;
        float tileSize = terrainConfig.tileSize;
        float halfTileSize = tileSize * 0.5f;

        while (actualStaticObjectsSpawned < objectCount && attempts < maxAttempts)
        {
            attempts++;

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

            float3 v00 = vertices[idx00].value;
            float3 v10 = vertices[idx10].value;
            float3 v01 = vertices[idx01].value;
            float3 v11 = vertices[idx11].value;

            float3 vX0 = math.lerp(v00, v10, tx);
            float3 vX1 = math.lerp(v01, v11, tx);
            float3 interpolatedPosition = math.lerp(vX0, vX1, tz);

            float3 localPosition = new float3(randomX - halfTileSize, interpolatedPosition.y, randomZ - halfTileSize);

            float3 n00 = normals[idx00].value;
            float3 n10 = normals[idx10].value;
            float3 n01 = normals[idx01].value;
            float3 n11 = normals[idx11].value;

            float3 nX0 = math.lerp(n00, n10, tx);
            float3 nX1 = math.lerp(n01, n11, tx);
            float3 normal = math.normalize(math.lerp(nX0, nX1, tz));

            float3 worldPosition = tileTransform.Position + localPosition;

            if (worldPosition.y < config.minSpawnHeight || worldPosition.y > config.maxSpawnHeight)
                continue;

            if (normal.y < config.slopeThreshold)
                continue;

            if (hasTrailConfig)
            {
                float noiseX = tile.gridCoordinate.x * terrainConfig.tileSize + randomX;
                float noiseZ = tile.gridCoordinate.y * terrainConfig.tileSize + randomZ;

                if (IsInsideTrailExclusionZone(noiseX, noiseZ, trailConfig.trail1) ||
                    IsInsideTrailExclusionZone(noiseX, noiseZ, trailConfig.trail2) ||
                    IsInsideTrailExclusionZone(noiseX, noiseZ, trailConfig.trail3))
                    continue;
            }

            float typeRoll = random.NextFloat(0f, 1f);
            float cumulativeTypeWeight = 0f;
            int objectTypeIndex = treeTypeCount - 1;
            for (int typeIndex = 0; typeIndex < treeTypeCount; typeIndex++)
            {
                cumulativeTypeWeight += objectTypeSpawnWeights[typeIndex];
                if (typeRoll < cumulativeTypeWeight)
                {
                    objectTypeIndex = typeIndex;
                    break;
                }
            }

            int prefabIndexLOD0 = objectTypeIndex * 3 + 0;
            quaternion prefabRotation = objectPrefabRotations[prefabIndexLOD0];

            quaternion randomYRotation = quaternion.RotateY(random.NextFloat(0f, math.PI * 2f));
            quaternion rotation = math.mul(randomYRotation, prefabRotation);

            byte initialLODLevel = 0;
            float initialDistance = 0f;

            if (hasLODConfig)
            {
                float2 objectPos2D = new float2(worldPosition.x, worldPosition.z);
                float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);
                initialDistance = math.distance(objectPos2D, cameraPos2D);

                if (initialDistance >= lodConfig.lod1Distance)
                    initialLODLevel = 2;
                else if (initialDistance >= lodConfig.lod0Distance)
                    initialLODLevel = 1;
                else
                    initialLODLevel = 0;
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
                initialMeshIndex = initialMeshIndex
            });

            actualStaticObjectsSpawned++;
        }
    }

    private static bool IsInsideTrailExclusionZone(float noiseX, float noiseZ, in TrailInstanceConfig trail)
    {
        if (!trail.enabled)
            return false;

        float exclusionRadius = trail.width * 0.5f + trail.blendWidth;
        float minDist2D = float.MaxValue;

        const int kSearchSamples = 9;
        for (int si = 0; si < kSearchSamples; si++)
        {
            float t = si / (float)(kSearchSamples - 1);
            float sz = noiseZ + math.lerp(-exclusionRadius, exclusionRadius, t);
            float scx = trail.amplitude * noise.snoise(new float2(sz * trail.frequency + trail.seed, 0f));
            float dx = noiseX - scx;
            float dz = noiseZ - sz;
            float d2 = dx * dx + dz * dz;
            if (d2 < minDist2D) minDist2D = d2;
        }

        return math.sqrt(minDist2D) < exclusionRadius;
    }
}

[BurstCompile]
struct InstantiateStaticObjectsJob : IJobParallelFor
{
    public EntityCommandBuffer.ParallelWriter ecb;

    [ReadOnly] public NativeArray<StaticObjectSpawnWorkItem> workItems;
    [ReadOnly] public NativeArray<Entity> objectPrefabs;
    [ReadOnly] public NativeArray<bool> billboardTypes;
    [ReadOnly] public NativeArray<MaterialMeshInfo> lodMeshInfos;

    [ReadOnly] public BufferLookup<StaticObjectSpawnPosition> spawnPositionLookup;
    [ReadOnly] public ComponentLookup<LocalTransform> tileTransformLookup;

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

            int prefabIndexLOD0 = spawnData.objectTypeIndex * 3 + 0;
            if (prefabIndexLOD0 >= objectPrefabs.Length)
                continue;

            Entity objectPrefab = objectPrefabs[prefabIndexLOD0];
            Entity objectEntity = ecb.Instantiate(index, objectPrefab);

            float3 worldPosition = tilePosition + spawnData.localPosition;

            ecb.SetComponent(index, objectEntity, new LocalTransform
            {
                Position = worldPosition,
                Rotation = spawnData.rotation,
                Scale = 1f
            });

            if (lodMeshInfos.Length > spawnData.initialMeshIndex)
                ecb.SetComponent(index, objectEntity, lodMeshInfos[spawnData.initialMeshIndex]);

            bool isBillboard = spawnData.objectTypeIndex < billboardTypes.Length
                && billboardTypes[spawnData.objectTypeIndex];

            ecb.SetComponent(index, objectEntity, new GlobalStaticObjectInstanceData
            {
                prefabIndex = prefabIndexLOD0,
                objectTypeIndex = spawnData.objectTypeIndex,
                currentLODLevel = spawnData.initialLODLevel,
                lastDistanceToPlayer = spawnData.initialDistance,
                isBillboardType = isBillboard
            });

            ecb.AddComponent(index, objectEntity, new StaticObjectTileOwnership
            {
                tileEntity = work.tileEntity,
                localOffset = spawnData.localPosition
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
