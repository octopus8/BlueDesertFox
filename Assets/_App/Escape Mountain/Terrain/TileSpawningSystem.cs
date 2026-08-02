using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that spawns and despawns terrain tiles based on player position.
/// Uses a NativeParallelHashMap to track active tiles efficiently.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TileSpawningSystem : ISystem
{
    private NativeParallelHashMap<int2, Entity> _activeTiles;
    private NativeHashSet<int2> _despawningGridCoords;
    /// <summary>
    /// Tracks the SubScene-baked <see cref="TerrainTileConfig"/> singleton. When it changes
    /// (scene reload with AutoLoad SubScene), OnStopRunning may never run — wipe Default-World
    /// tiles here so stale <see cref="StaticObjectsSpawned"/> entities cannot block respawn.
    /// </summary>
    private Entity _trackedConfigEntity;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<CameraDataSingleton>();
        state.RequireForUpdate<TerrainHeightAlignState>();

        _activeTiles = new NativeParallelHashMap<int2, Entity>(256, Allocator.Persistent);
        _despawningGridCoords = new NativeHashSet<int2>(64, Allocator.Persistent);
        _trackedConfigEntity = Entity.Null;
    }

    /// <summary>
    /// Clears Persistent tile maps and destroys Default-World tile/static-object entities when
    /// TerrainTileConfig disappears (SubScene unload / scene reload). Tiles are created at runtime
    /// in the Default World, so SubScene.OnDisable does not destroy them — without this, stale tiles
    /// marked StaticObjectsSpawned block respawn and Quest can show empty terrain after reload.
    /// </summary>
    public void OnStopRunning(ref SystemState state)
    {
        // Complete in-flight mesh/physics jobs before destroying the entities they reference.
        TerrainRuntimeReloadUtility.ScrubBeforeDestroyingRuntimeTiles(ref state);
        DestroyAllRuntimeTiles(ref state);

        if (_activeTiles.IsCreated)
            _activeTiles.Clear();
        if (_despawningGridCoords.IsCreated)
            _despawningGridCoords.Clear();
        _trackedConfigEntity = Entity.Null;
    }

    private static void DestroyAllRuntimeTiles(ref SystemState state)
    {
        var em = state.EntityManager;
        using var query = em.CreateEntityQuery(ComponentType.ReadOnly<TerrainTile>());
        var tiles = query.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < tiles.Length; i++)
        {
            Entity tileEntity = tiles[i];
            if (!em.Exists(tileEntity))
                continue;

            if (em.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
            {
                var spawnedObjects = em.GetBuffer<SpawnedStaticObjectReference>(tileEntity);
                // Copy entities out — DestroyHierarchyImmediate may invalidate the buffer.
                var objectEntities = new NativeArray<Entity>(spawnedObjects.Length, Allocator.Temp);
                for (int objIdx = 0; objIdx < spawnedObjects.Length; objIdx++)
                    objectEntities[objIdx] = spawnedObjects[objIdx].objectEntity;

                for (int objIdx = 0; objIdx < objectEntities.Length; objIdx++)
                {
                    StaticObjectHierarchyDestroyUtility.DestroyHierarchyImmediate(
                        objectEntities[objIdx], em);
                }
                objectEntities.Dispose();
            }

            Mesh meshToDestroy = null;
            if (em.HasComponent<MeshReference>(tileEntity))
            {
                var meshRef = em.GetComponentObject<MeshReference>(tileEntity);
                if (meshRef != null)
                {
                    meshToDestroy = meshRef.mesh;
                    meshRef.mesh = null;
                }
            }

            // Destroy entity first so Entities Graphics drops RenderMeshArray / BRG registrations
            // before the Mesh asset is destroyed (avoids null MeshID BatchDrawCommand errors).
            em.DestroyEntity(tileEntity);

            if (meshToDestroy != null)
                Object.Destroy(meshToDestroy);
        }

        tiles.Dispose();

        // Catch any static objects that lost their tile ownership link during an unclean unload.
        using var ownedQuery = em.CreateEntityQuery(ComponentType.ReadOnly<StaticObjectTileOwnership>());
        var owned = ownedQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < owned.Length; i++)
        {
            if (em.Exists(owned[i]))
                StaticObjectHierarchyDestroyUtility.DestroyHierarchyImmediate(owned[i], em);
        }
        owned.Dispose();
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_activeTiles.IsCreated)
            _activeTiles.Dispose();
        if (_despawningGridCoords.IsCreated)
            _despawningGridCoords.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        var configEntity = SystemAPI.GetSingletonEntity<TerrainTileConfig>();
        if (_trackedConfigEntity != configEntity)
        {
            // AutoLoad SubScene reload can replace config without an empty RequireForUpdate window,
            // so OnStopRunning never runs. Cancel sibling in-flight work, then destroy surviving tiles.
            if (_trackedConfigEntity != Entity.Null || !_activeTiles.IsEmpty)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.Log(
                    $"[TileSpawning] TerrainTileConfig changed ({_trackedConfigEntity.Index}→{configEntity.Index}); " +
                    "scrubbing in-flight terrain work and destroying cached runtime tiles");
#endif
                TerrainRuntimeReloadUtility.ScrubBeforeDestroyingRuntimeTiles(ref state);
                DestroyAllRuntimeTiles(ref state);
                if (_activeTiles.IsCreated)
                    _activeTiles.Clear();
                if (_despawningGridCoords.IsCreated)
                    _despawningGridCoords.Clear();
            }
            _trackedConfigEntity = configEntity;
        }

        // Always prune before the height-align gate so dead map entries cannot block respawn
        // while TerrainHeightAlignState.aligned is still 0 after reload.
        PruneDestroyedTileEntries(ref state);

        var config = SystemAPI.GetSingleton<TerrainTileConfig>();

        if (!config.renderTerrain && !config.enablePhysicsColliders)
            return;

        if (SystemAPI.GetSingleton<TerrainHeightAlignState>().aligned == 0)
            return;

        bool useBudgetedDespawn = SystemAPI.HasSingleton<StaticObjectSpawnerConfig>();
        int destroyBudget = useBudgetedDespawn
            ? SystemAPI.GetSingleton<StaticObjectSpawnerConfig>().maxObjectsSpawnedPerFrame
            : int.MaxValue;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        if (useBudgetedDespawn)
            ProcessBudgetedDespawns(ref state, ecb, destroyBudget);

        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
        float3 playerPosition = cameraData.position;
        float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);

        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        float3 effectivePlayerPosition = playerPosition + scrollOffset.accumulatedOffset;

        int2 playerGridCoord = new int2(
            (int)math.floor(effectivePlayerPosition.x / config.tileSize),
            (int)math.floor(effectivePlayerPosition.z / config.tileSize)
        );

        int viewDistanceInTiles = (int)math.ceil(config.viewDistance / config.tileSize);

        var tilesToSpawn = new NativeList<int2>(Allocator.Temp);
        var tilesToDespawn = new NativeList<int2>(Allocator.Temp);

        for (int x = -viewDistanceInTiles; x <= viewDistanceInTiles; x++)
        {
            for (int z = -viewDistanceInTiles; z <= viewDistanceInTiles; z++)
            {
                int2 gridCoord = playerGridCoord + new int2(x, z);

                float3 tileCenterBase = new float3(
                    gridCoord.x * config.tileSize + config.tileSize * 0.5f,
                    0,
                    gridCoord.y * config.tileSize + config.tileSize * 0.5f
                );
                float3 tileCenterScrolled = tileCenterBase - scrollOffset.accumulatedOffset;
                float2 tileCenter2D = new float2(tileCenterScrolled.x, tileCenterScrolled.z);
                float distanceToTile = math.distance(tileCenter2D, playerPos2D);

                if (distanceToTile <= config.viewDistance)
                {
                    if (!_activeTiles.ContainsKey(gridCoord) && !_despawningGridCoords.Contains(gridCoord))
                        tilesToSpawn.Add(gridCoord);
                }
            }
        }

        var tileKeys = _activeTiles.GetKeyArray(Allocator.Temp);
        foreach (var gridCoord in tileKeys)
        {
            float3 tileCenterBase = new float3(
                gridCoord.x * config.tileSize + config.tileSize * 0.5f,
                0,
                gridCoord.y * config.tileSize + config.tileSize * 0.5f
            );
            float3 tileCenterScrolled = tileCenterBase - scrollOffset.accumulatedOffset;
            float2 tileCenter2D = new float2(tileCenterScrolled.x, tileCenterScrolled.z);
            float distanceToTile = math.distance(tileCenter2D, playerPos2D);

            if (distanceToTile > config.viewDistance)
                tilesToDespawn.Add(gridCoord);
        }
        tileKeys.Dispose();

        foreach (var gridCoord in tilesToSpawn)
        {
            Entity tileEntity = ecb.CreateEntity();

            float3 basePosition = new float3(
                gridCoord.x * config.tileSize + config.tileSize * 0.5f,
                0,
                gridCoord.y * config.tileSize + config.tileSize * 0.5f
            );
            float3 tilePosition = basePosition - scrollOffset.accumulatedOffset;

            ecb.AddComponent(tileEntity, new LocalTransform
            {
                Position = tilePosition,
                Rotation = quaternion.identity,
                Scale = 1f
            });

            ecb.AddComponent(tileEntity, new LocalToWorld
            {
                Value = float4x4.TRS(tilePosition, quaternion.identity, new float3(1f))
            });

            ecb.AddComponent(tileEntity, new TerrainTile
            {
                gridCoordinate = gridCoord,
                meshGenerated = false,
                needsRegeneration = false
            });

            ecb.AddBuffer<VertexElement>(tileEntity);
            ecb.AddBuffer<NormalElement>(tileEntity);
            ecb.AddBuffer<UVElement>(tileEntity);
            ecb.AddBuffer<IndexElement>(tileEntity);
            ecb.AddBuffer<SpawnedStaticObjectReference>(tileEntity);
        }

        foreach (var gridCoord in tilesToDespawn)
        {
            if (!_activeTiles.TryGetValue(gridCoord, out Entity tileEntity))
                continue;

            if (useBudgetedDespawn)
            {
                ecb.AddComponent<PendingTileDespawn>(tileEntity);
                _despawningGridCoords.Add(gridCoord);
                _activeTiles.Remove(gridCoord);
            }
            else
            {
                DestroyTileStaticObjectsImmediate(ref state, ecb, tileEntity);
                ecb.DestroyEntity(tileEntity);
                _activeTiles.Remove(gridCoord);
            }
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();

        if (tilesToSpawn.Length > 0)
        {
            var spawnedCoords = new NativeHashSet<int2>(tilesToSpawn.Length, Allocator.Temp);
            for (int i = 0; i < tilesToSpawn.Length; i++)
                spawnedCoords.Add(tilesToSpawn[i]);

            foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>().WithEntityAccess())
            {
                if (spawnedCoords.Contains(tile.ValueRO.gridCoordinate) && !_activeTiles.ContainsKey(tile.ValueRO.gridCoordinate))
                    _activeTiles.Add(tile.ValueRO.gridCoordinate, entity);
            }

            spawnedCoords.Dispose();
        }

        tilesToSpawn.Dispose();
        tilesToDespawn.Dispose();
    }

    /// <summary>
    /// Removes map entries whose tile entities were destroyed without going through despawn
    /// (e.g. SubScene unload race where OnStopRunning did not run first).
    /// </summary>
    private void PruneDestroyedTileEntries(ref SystemState state)
    {
        if (_activeTiles.IsEmpty)
            return;

        var em = state.EntityManager;
        var keys = _activeTiles.GetKeyArray(Allocator.Temp);
        bool foundDestroyed = false;
        for (int i = 0; i < keys.Length; i++)
        {
            if (_activeTiles.TryGetValue(keys[i], out Entity tileEntity) && !em.Exists(tileEntity))
            {
                foundDestroyed = true;
                break;
            }
        }
        keys.Dispose();

        // SubScene unload left stale keys — drop the whole cache so tiles can respawn.
        if (foundDestroyed)
        {
            _activeTiles.Clear();
            _despawningGridCoords.Clear();
        }
    }

    private static void DestroyTileStaticObjectsImmediate(ref SystemState state, EntityCommandBuffer ecb, Entity tileEntity)
    {
        if (!state.EntityManager.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
            return;

        var spawnedObjects = state.EntityManager.GetBuffer<SpawnedStaticObjectReference>(tileEntity);
        foreach (var objectRef in spawnedObjects)
        {
            StaticObjectHierarchyDestroyUtility.DestroyHierarchy(
                objectRef.objectEntity, ecb, state.EntityManager);
        }
    }

    private void ProcessBudgetedDespawns(ref SystemState state, EntityCommandBuffer ecb, int budget)
    {
        if (budget <= 0)
            return;

        int destroyed = 0;
        var em = state.EntityManager;
        var tilesToDestroy = new NativeList<Entity>(4, Allocator.Temp);

        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<PendingTileDespawn>()
            .WithEntityAccess())
        {
            if (!em.HasBuffer<SpawnedStaticObjectReference>(entity))
            {
                tilesToDestroy.Add(entity);
                _despawningGridCoords.Remove(tile.ValueRO.gridCoordinate);
                continue;
            }

            var buffer = em.GetBuffer<SpawnedStaticObjectReference>(entity);

            if (buffer.Length == 0)
            {
                tilesToDestroy.Add(entity);
                _despawningGridCoords.Remove(tile.ValueRO.gridCoordinate);
                continue;
            }

            if (destroyed >= budget)
                continue;

            for (int i = buffer.Length - 1; i >= 0; i--)
            {
                if (destroyed >= budget)
                    break;

                var objectEntity = buffer[i].objectEntity;
                if (!em.Exists(objectEntity))
                {
                    buffer.RemoveAt(i);
                    continue;
                }

                int groupCount = StaticObjectHierarchyDestroyUtility.CountLinkedEntities(objectEntity, em);
                if (groupCount == 0)
                {
                    buffer.RemoveAt(i);
                    continue;
                }

                if (destroyed + groupCount > budget)
                    continue;

                destroyed += StaticObjectHierarchyDestroyUtility.DestroyHierarchy(objectEntity, ecb, em);
                buffer.RemoveAt(i);
            }

            if (buffer.Length == 0)
            {
                tilesToDestroy.Add(entity);
                _despawningGridCoords.Remove(tile.ValueRO.gridCoordinate);
            }
        }

        for (int i = 0; i < tilesToDestroy.Length; i++)
            ecb.DestroyEntity(tilesToDestroy[i]);

        tilesToDestroy.Dispose();
    }
}
