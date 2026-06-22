using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if UNITY_EDITOR
using UnityEngine;
#endif

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

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<CameraDataSingleton>();

        _activeTiles = new NativeParallelHashMap<int2, Entity>(256, Allocator.Persistent);
        _despawningGridCoords = new NativeHashSet<int2>(64, Allocator.Persistent);
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
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();

        if (!config.renderTerrain && !config.enablePhysicsColliders)
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
                float distanceToTile = math.distance(tileCenterScrolled, playerPosition);

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
            float distanceToTile = math.distance(tileCenterScrolled, playerPosition);

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

    private static void DestroyTileStaticObjectsImmediate(ref SystemState state, EntityCommandBuffer ecb, Entity tileEntity)
    {
        if (!state.EntityManager.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
            return;

        var spawnedObjects = state.EntityManager.GetBuffer<SpawnedStaticObjectReference>(tileEntity);
        foreach (var objectRef in spawnedObjects)
        {
            if (state.EntityManager.Exists(objectRef.objectEntity))
                ecb.DestroyEntity(objectRef.objectEntity);
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

            for (int i = buffer.Length - 1; i >= 0 && destroyed < budget; i--)
            {
                var objectEntity = buffer[i].objectEntity;
                if (em.Exists(objectEntity))
                {
                    ecb.DestroyEntity(objectEntity);
                    destroyed++;
                }
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
