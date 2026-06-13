using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that spawns tree entities on terrain tiles after mesh generation.
/// Uses frame budgeting to prevent performance spikes and deterministic random placement.
/// 
/// DISABLED: Replaced by TerrainTreeSpawningSystemOptimized for Quest 3 performance.
/// Remove [DisableAutoCreation] to re-enable original system.
/// </summary>
[DisableAutoCreation]
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class TerrainTreeSpawningSystem : SystemBase
{
    private NativeQueue<Entity> _pendingTiles;
    private NativeHashSet<Entity> _queuedEntities;
    private int _StaticObjectsSpawnedThisFrame;

    /// <summary>Registers required config singletons and allocates persistent pending-tile queue and de-duplication set.</summary>
    protected override void OnCreate()
    {
        RequireForUpdate<StaticObjectSpawnerConfig>();
        RequireForUpdate<StaticObjectPrefabElement>();
        
        _pendingTiles = new NativeQueue<Entity>(Allocator.Persistent);
        _queuedEntities = new NativeHashSet<Entity>(64, Allocator.Persistent);
    }

    /// <summary>Disposes the persistent tile queue and de-duplication set.</summary>
    protected override void OnDestroy()
    {
        if (_pendingTiles.IsCreated)
            _pendingTiles.Dispose();
        if (_queuedEntities.IsCreated)
            _queuedEntities.Dispose();
    }

    /// <summary>
    /// Queues tiles that have generated meshes but not yet spawned trees, then processes up to
    /// <see cref="StaticObjectSpawnerConfig.frameBudget"/> tiles per frame using deterministic random
    /// placement on the tile mesh via <see cref="SpawnTreesOnTile"/>.
    /// </summary>
    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<StaticObjectSpawnerConfig>();
/*        
        // Early exit if rendering is disabled - no need to spawn trees
        var terrainConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
        if (!terrainConfig.renderTerrain)
        {
            return;
        }
*/        
        if (config.maxObjectsPerTile <= 0)
        {
            return;
        }
        
        var configEntity = SystemAPI.GetSingletonEntity<StaticObjectSpawnerConfig>();
        var objectPrefabsBuffer = EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity);
        
        if (objectPrefabsBuffer.Length == 0)
        {
            return;
        }
        
        // Calculate number of tree types (3 LODs per type)
        var objectPrefabCount = objectPrefabsBuffer.Length;
        var treeTypeCount = objectPrefabCount / 3; // 3 LODs per tree type
        
        if (treeTypeCount == 0)
        {
            return;
        }
        
        var objectPrefabs = new NativeArray<Entity>(objectPrefabCount, Allocator.Temp);
        
        for (int i = 0; i < objectPrefabCount; i++)
        {
            objectPrefabs[i] = objectPrefabsBuffer[i].prefabEntity;
        }
        
        // Get LOD config if available
        StaticObjectLODConfig lodConfig = default;
        bool hasLODConfig = SystemAPI.HasSingleton<StaticObjectLODConfig>();
        if (hasLODConfig)
        {
            lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();
        }
        
        // Get player position for initial LOD calculation
        float3 playerPosition = float3.zero;
        bool hasPlayerRef = false;
        if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
            playerRef != null && playerRef.playerTransform != null)
        {
            playerPosition = playerRef.playerTransform.position;
            hasPlayerRef = true;
        }
        
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference>()
            .WithNone<StaticObjectsSpawned>()
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

        _StaticObjectsSpawnedThisFrame = 0;
        
        while (_pendingTiles.Count > 0 && _StaticObjectsSpawnedThisFrame < config.maxObjectsSpawnedPerFrame)
        {
            Entity tileEntity = _pendingTiles.Dequeue();
            
            _queuedEntities.Remove(tileEntity);
            
            if (!EntityManager.Exists(tileEntity))
                continue;
            
            if (EntityManager.HasComponent<StaticObjectsSpawned>(tileEntity))
            {
                continue;
            }
            
            int StaticObjectsSpawned = SpawnTreesOnTile(tileEntity, config, objectPrefabs);
            _StaticObjectsSpawnedThisFrame += StaticObjectsSpawned;
            
            EntityManager.AddComponent<StaticObjectsSpawned>(tileEntity);
            
            if (_StaticObjectsSpawnedThisFrame >= config.maxObjectsSpawnedPerFrame)
                break;
        }

        objectPrefabs.Dispose();
    }

    /// <summary>
    /// Instantiates static objects on the given tile using deterministic random placement —
    /// sampling the tile's vertex/normal buffers for height and slope — and returns the number
    /// of objects successfully spawned. Skips tiles with invalid mesh data.
    /// </summary>
    private int SpawnTreesOnTile(Entity tileEntity, StaticObjectSpawnerConfig config, NativeArray<Entity> objectPrefabs)
    {
        var prefabCount = objectPrefabs.Length;
        if (prefabCount == 0)
            return 0;
        
        // Calculate tree type count (3 LODs per type)
        int treeTypeCount = prefabCount / 3;
        if (treeTypeCount == 0)
            return 0;
        
        var tile = EntityManager.GetComponentData<TerrainTile>(tileEntity);
        var tileTransform = EntityManager.GetComponentData<LocalTransform>(tileEntity);
        var terrainConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Get LOD config and player position for initial LOD calculation
        StaticObjectLODConfig lodConfig = default;
        float3 playerPosition = float3.zero;
        bool hasLODConfig = SystemAPI.HasSingleton<StaticObjectLODConfig>();
        bool hasPlayerRef = false;
        
        if (hasLODConfig)
        {
            lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();
        }
        
        if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
            playerRef != null && playerRef.playerTransform != null)
        {
            playerPosition = playerRef.playerTransform.position;
            hasPlayerRef = true;
        }
        
        if (!EntityManager.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
        {
            EntityManager.AddBuffer<SpawnedStaticObjectReference>(tileEntity);
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
        
        int objectCount = random.NextInt(config.minObjectsPerTile, config.maxObjectsPerTile + 1);
        
        var tempSpawnedTrees = new NativeList<Entity>(objectCount, Allocator.Temp);
        
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
            
            float3 v00 = vertexPositions[idx00];
            float3 v10 = vertexPositions[idx10];
            float3 v01 = vertexPositions[idx01];
            float3 v11 = vertexPositions[idx11];
            
            float3 vX0 = math.lerp(v00, v10, tx);
            float3 vX1 = math.lerp(v01, v11, tx);
            float3 interpolatedPosition = math.lerp(vX0, vX1, tz);
            
            // Local position relative to tile center (vertices are now centered around origin)
            // randomX/randomZ are in range [0, tileSize], offset by -halfTileSize to match vertex space
            float3 localPosition = new float3(randomX - halfTileSize, interpolatedPosition.y, randomZ - halfTileSize);
            
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
            
            // Select tree type (not individual LOD prefab)
            int objectTypeIndex = random.NextInt(0, treeTypeCount);
            
            // Always spawn with LOD0 prefab (highest detail)
            int prefabIndexLOD0 = objectTypeIndex * 3 + 0;
            Entity objectPrefab = objectPrefabs[prefabIndexLOD0];
            
            // Get rotation from prefab
            quaternion prefabRotation = quaternion.identity;
            if (EntityManager.HasComponent<LocalTransform>(objectPrefab))
            {
                prefabRotation = EntityManager.GetComponentData<LocalTransform>(objectPrefab).Rotation;
            }
            
            // Apply random Y-axis rotation on top of prefab rotation for variation
            quaternion randomYRotation = quaternion.RotateY(random.NextFloat(0f, math.PI * 2f));
            quaternion finalRotation = math.mul(randomYRotation, prefabRotation);
            
            Entity objectEntity = EntityManager.Instantiate(objectPrefab);
            
            if (EntityManager.HasComponent<Unity.Rendering.MaterialMeshInfo>(objectEntity))
            {
                EntityManager.RemoveComponent<Unity.Rendering.MaterialMeshInfo>(objectEntity);
            }
            if (EntityManager.HasComponent<Unity.Rendering.RenderBounds>(objectEntity))
            {
                EntityManager.RemoveComponent<Unity.Rendering.RenderBounds>(objectEntity);
            }
            
            if (EntityManager.HasBuffer<LinkedEntityGroup>(objectEntity))
            {
                var linkedGroup = EntityManager.GetBuffer<LinkedEntityGroup>(objectEntity);
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
            
            EntityManager.SetComponentData(objectEntity, new LocalTransform
            {
                Position = tileTransform.Position + localPosition,
                Rotation = finalRotation,
                Scale = 1f
            });
            
            EntityManager.AddComponentData(objectEntity, new StaticObjectTileOwnership
            {
                tileEntity = tileEntity,
                localOffset = localPosition
            });
            
            EntityManager.AddComponent<GlobalStaticObjectInstance>(objectEntity);
            
            // Calculate initial LOD based on distance to player
            byte initialLODLevel = 0; // Default to highest detail
            float initialDistance = 0f;
            
            if (hasPlayerRef && hasLODConfig)
            {
                // Calculate 2D distance from tree to player
                float2 objectPos2D = new float2(worldPosition.x, worldPosition.z);
                float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
                initialDistance = math.distance(objectPos2D, playerPos2D);
                
                // Determine initial LOD level based on distance
                if (initialDistance >= lodConfig.lod1Distance)
                    initialLODLevel = 2; // Farthest: LOD2
                else if (initialDistance >= lodConfig.lod0Distance)
                    initialLODLevel = 1; // Medium: LOD1
                else
                    initialLODLevel = 0; // Closest: LOD0
            }
            
            // Calculate mesh index based on tree type and initial LOD
            int initialMeshIndex = (objectTypeIndex * 3) + initialLODLevel;
            
            EntityManager.AddComponentData(objectEntity, new GlobalStaticObjectInstanceData
            {
                prefabIndex = prefabIndexLOD0,
                objectTypeIndex = objectTypeIndex,
                currentLODLevel = initialLODLevel,
                lastDistanceToPlayer = initialDistance
            });
            
            tempSpawnedTrees.Add(objectEntity);
            
            actualStaticObjectsSpawned++;
        }
        
        if (tempSpawnedTrees.Length > 0)
        {
            var spawnedTreesBuffer = EntityManager.GetBuffer<SpawnedStaticObjectReference>(tileEntity);
            foreach (var objectEntity in tempSpawnedTrees)
            {
                spawnedTreesBuffer.Add(new SpawnedStaticObjectReference
                {
                    objectEntity = objectEntity
                });
            }
        }
        
        vertexPositions.Dispose();
        vertexNormals.Dispose();
        tempSpawnedTrees.Dispose();
        
        // Log tree spawning results if debug logging is enabled
        if (hasLODConfig && lodConfig.enableObjectLODDebug)
        {
            int failedAttempts = attempts - actualStaticObjectsSpawned;
            UnityEngine.Debug.Log($"[TreeSpawner] Tile {tile.gridCoordinate}: Spawned {actualStaticObjectsSpawned}/{objectCount} trees ({failedAttempts} filtered by height/slope)");
        }
        
        return actualStaticObjectsSpawned;
    }
}

