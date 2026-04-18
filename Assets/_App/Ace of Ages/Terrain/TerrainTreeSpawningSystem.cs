using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that spawns tree entities on terrain tiles after mesh generation.
/// Uses frame budgeting to prevent performance spikes and deterministic random placement.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class TerrainTreeSpawningSystem : SystemBase
{
    private NativeQueue<Entity> _pendingTiles;
    private NativeHashSet<Entity> _queuedEntities;
    private int _treesSpawnedThisFrame;

    protected override void OnCreate()
    {
        RequireForUpdate<TreeSpawnerConfig>();
        RequireForUpdate<TreePrefabElement>();
        
        _pendingTiles = new NativeQueue<Entity>(Allocator.Persistent);
        _queuedEntities = new NativeHashSet<Entity>(64, Allocator.Persistent);
    }

    protected override void OnDestroy()
    {
        if (_pendingTiles.IsCreated)
            _pendingTiles.Dispose();
        if (_queuedEntities.IsCreated)
            _queuedEntities.Dispose();
    }

    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<TreeSpawnerConfig>();
        
        if (config.maxTreesPerTile <= 0)
        {
            return;
        }
        
        var configEntity = SystemAPI.GetSingletonEntity<TreeSpawnerConfig>();
        var treePrefabsBuffer = EntityManager.GetBuffer<TreePrefabElement>(configEntity);
        
        if (treePrefabsBuffer.Length == 0)
        {
            return;
        }
        
        TreePrefabMeshMaterialData meshMaterialData = null;
        if (EntityManager.HasComponent<TreePrefabMeshMaterialData>(configEntity))
        {
            meshMaterialData = EntityManager.GetComponentData<TreePrefabMeshMaterialData>(configEntity);
        }
        
        if (meshMaterialData == null || meshMaterialData.meshes == null || meshMaterialData.materials == null)
        {
            return;
        }
        
        var treePrefabCount = treePrefabsBuffer.Length;
        var treePrefabs = new NativeArray<Entity>(treePrefabCount, Allocator.Temp);
        
        var treeMeshes = meshMaterialData.meshes;
        var treeMaterials = meshMaterialData.materials;
        
        for (int i = 0; i < treePrefabCount; i++)
        {
            treePrefabs[i] = treePrefabsBuffer[i].prefabEntity;
        }
        
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference>()
            .WithNone<TreesSpawned>()
            .WithEntityAccess())
        {
            if (tile.ValueRO.meshGenerated)
            {
                if (_queuedEntities.Add(entity))
                {
                    _pendingTiles.Enqueue(entity);
                }
            }
        }

        _treesSpawnedThisFrame = 0;
        
        while (_pendingTiles.Count > 0 && _treesSpawnedThisFrame < config.maxTreesSpawnedPerFrame)
        {
            Entity tileEntity = _pendingTiles.Dequeue();
            
            _queuedEntities.Remove(tileEntity);
            
            if (!EntityManager.Exists(tileEntity))
                continue;
            
            if (EntityManager.HasComponent<TreesSpawned>(tileEntity))
            {
                continue;
            }
            
            int treesSpawned = SpawnTreesOnTile(tileEntity, config, treePrefabs, treeMeshes, treeMaterials);
            _treesSpawnedThisFrame += treesSpawned;
            
            EntityManager.AddComponent<TreesSpawned>(tileEntity);
            
            if (_treesSpawnedThisFrame >= config.maxTreesSpawnedPerFrame)
                break;
        }

        treePrefabs.Dispose();
    }

    private int SpawnTreesOnTile(Entity tileEntity, TreeSpawnerConfig config, NativeArray<Entity> treePrefabs, Mesh[] treeMeshes, Material[] treeMaterials)
    {
        var prefabCount = treePrefabs.Length;
        if (prefabCount == 0)
            return 0;
        
        var tile = EntityManager.GetComponentData<TerrainTile>(tileEntity);
        var tileTransform = EntityManager.GetComponentData<LocalTransform>(tileEntity);
        var terrainConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        if (!EntityManager.HasBuffer<SpawnedTreeReference>(tileEntity))
        {
            EntityManager.AddBuffer<SpawnedTreeReference>(tileEntity);
        }
        
        var vertices = EntityManager.GetBuffer<VertexElement>(tileEntity);
        var normals = EntityManager.GetBuffer<NormalElement>(tileEntity);
        
        if (vertices.Length == 0 || normals.Length == 0)
        {
            return 0;
        }
        
        var vertexCount = vertices.Length;
        var vertexPositions = new NativeArray<float3>(vertexCount, Allocator.Temp);
        var vertexNormals = new NativeArray<float3>(vertexCount, Allocator.Temp);
        
        for (int i = 0; i < vertexCount; i++)
        {
            vertexPositions[i] = vertices[i].value;
            vertexNormals[i] = normals[i].value;
        }
        
        var random = new Unity.Mathematics.Random((uint)(tile.gridCoordinate.GetHashCode() + 12345));
        
        int treeCount = random.NextInt(config.minTreesPerTile, config.maxTreesPerTile + 1);
        
        var tempSpawnedTrees = new NativeList<Entity>(treeCount, Allocator.Temp);
        
        int actualTreesSpawned = 0;
        int maxAttempts = treeCount * 3;
        int attempts = 0;
        
        int vPerSide = terrainConfig.verticesPerSide;
        float tileSize = terrainConfig.tileSize;
        
        while (actualTreesSpawned < treeCount && attempts < maxAttempts)
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
            
            float3 v00 = vertexPositions[idx00];
            float3 v10 = vertexPositions[idx10];
            float3 v01 = vertexPositions[idx01];
            float3 v11 = vertexPositions[idx11];
            
            float3 vX0 = math.lerp(v00, v10, tx);
            float3 vX1 = math.lerp(v01, v11, tx);
            float3 interpolatedPosition = math.lerp(vX0, vX1, tz);
            
            float3 localPosition = new float3(randomX, interpolatedPosition.y, randomZ);
            
            float3 n00 = vertexNormals[idx00];
            float3 n10 = vertexNormals[idx10];
            float3 n01 = vertexNormals[idx01];
            float3 n11 = vertexNormals[idx11];
            
            float3 nX0 = math.lerp(n00, n10, tx);
            float3 nX1 = math.lerp(n01, n11, tx);
            float3 normal = math.normalize(math.lerp(nX0, nX1, tz));
            
            float3 worldPosition = tileTransform.Position + localPosition;
            
            if (worldPosition.y < config.minSpawnHeight || worldPosition.y > config.maxSpawnHeight)
                continue;
            
            if (normal.y < config.slopeThreshold)
                continue;
            
            int prefabIndex = random.NextInt(0, prefabCount);
            Entity treePrefab = treePrefabs[prefabIndex];
            
            Entity treeEntity = EntityManager.Instantiate(treePrefab);
            
            if (EntityManager.HasComponent<Unity.Rendering.MaterialMeshInfo>(treeEntity))
            {
                EntityManager.RemoveComponent<Unity.Rendering.MaterialMeshInfo>(treeEntity);
            }
            if (EntityManager.HasComponent<Unity.Rendering.RenderBounds>(treeEntity))
            {
                EntityManager.RemoveComponent<Unity.Rendering.RenderBounds>(treeEntity);
            }
            
            if (EntityManager.HasBuffer<LinkedEntityGroup>(treeEntity))
            {
                var linkedGroup = EntityManager.GetBuffer<LinkedEntityGroup>(treeEntity);
                foreach (var linkedEntity in linkedGroup)
                {
                    if (EntityManager.HasComponent<Unity.Rendering.MaterialMeshInfo>(linkedEntity.Value))
                    {
                        EntityManager.RemoveComponent<Unity.Rendering.MaterialMeshInfo>(linkedEntity.Value);
                    }
                    if (EntityManager.HasComponent<Unity.Rendering.RenderBounds>(linkedEntity.Value))
                    {
                        EntityManager.RemoveComponent<Unity.Rendering.RenderBounds>(linkedEntity.Value);
                    }
                }
            }
            
            quaternion rotation = quaternion.RotateY(random.NextFloat(0f, math.PI * 2f));
            
            EntityManager.SetComponentData(treeEntity, new LocalTransform
            {
                Position = tileTransform.Position + localPosition,
                Rotation = rotation,
                Scale = 1f
            });
            
            EntityManager.AddComponentData(treeEntity, new TreeTileOwnership
            {
                tileEntity = tileEntity,
                localOffset = localPosition
            });
            
            EntityManager.AddComponent<GlobalTreeInstance>(treeEntity);
            
            EntityManager.AddComponentData(treeEntity, new GlobalTreeInstanceData
            {
                mesh = treeMeshes[prefabIndex],
                material = treeMaterials[prefabIndex],
                prefabIndex = prefabIndex
            });
            
            tempSpawnedTrees.Add(treeEntity);
            
            actualTreesSpawned++;
        }
        
        if (tempSpawnedTrees.Length > 0)
        {
            var spawnedTreesBuffer = EntityManager.GetBuffer<SpawnedTreeReference>(tileEntity);
            foreach (var treeEntity in tempSpawnedTrees)
            {
                spawnedTreesBuffer.Add(new SpawnedTreeReference
                {
                    treeEntity = treeEntity
                });
            }
        }
        
        vertexPositions.Dispose();
        vertexNormals.Dispose();
        tempSpawnedTrees.Dispose();
        
        return actualTreesSpawned;
    }
}

