using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
using UnityEngine;
#endif

/// <summary>
/// System that spawns tree entities on terrain tiles after mesh generation.
/// Uses frame budgeting to prevent performance spikes and deterministic random placement.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial class TerrainTreeSpawningSystem : SystemBase
{
    private NativeQueue<Entity> _pendingTiles;
    private NativeHashSet<Entity> _queuedEntities; // Track what's already queued to prevent duplicates
    private int _treesSpawnedThisFrame;
    
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainTrees.Spawning");
    private static readonly ProfilerMarker s_EnqueueMarker = new ProfilerMarker("TerrainTrees.Enqueue");
    private static readonly ProfilerMarker s_SpawnMarker = new ProfilerMarker("TerrainTrees.Spawn");
#endif

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
#if UNITY_EDITOR
        s_ProfilerMarker.Begin();
#endif

        var config = SystemAPI.GetSingleton<TreeSpawnerConfig>();
        
        // Validate configuration
        if (config.maxTreesPerTile <= 0)
        {
#if UNITY_EDITOR
            s_ProfilerMarker.End();
#endif
            return;
        }
        
        // Get tree prefabs buffer and mesh/material data
        var configEntity = SystemAPI.GetSingletonEntity<TreeSpawnerConfig>();
        var treePrefabsBuffer = EntityManager.GetBuffer<TreePrefabElement>(configEntity);
        
        if (treePrefabsBuffer.Length == 0)
        {
#if UNITY_EDITOR
            s_ProfilerMarker.End();
#endif
            return;
        }
        
        // Get mesh/material data from managed component
        TreePrefabMeshMaterialData meshMaterialData = null;
        if (EntityManager.HasComponent<TreePrefabMeshMaterialData>(configEntity))
        {
            meshMaterialData = EntityManager.GetComponentData<TreePrefabMeshMaterialData>(configEntity);
        }
        
        if (meshMaterialData == null || meshMaterialData.meshes == null || meshMaterialData.materials == null)
        {
#if UNITY_EDITOR
            Debug.LogError("[TreeSpawning] TreePrefabMeshMaterialData component missing or invalid!");
            s_ProfilerMarker.End();
#endif
            return;
        }
        
        // Copy tree prefab entities to native array to avoid buffer invalidation
        var treePrefabCount = treePrefabsBuffer.Length;
        var treePrefabs = new NativeArray<Entity>(treePrefabCount, Allocator.Temp);
        
        // Use baked mesh/material arrays
        var treeMeshes = meshMaterialData.meshes;
        var treeMaterials = meshMaterialData.materials;
        
        for (int i = 0; i < treePrefabCount; i++)
        {
            treePrefabs[i] = treePrefabsBuffer[i].prefabEntity;
            
#if UNITY_EDITOR
            if (i < treeMeshes.Length && i < treeMaterials.Length)
            {
                if (treeMeshes[i] == null || treeMaterials[i] == null)
                {
                    Debug.LogWarning($"[TreeSpawning] Tree prefab {i} missing mesh or material! Mesh: {treeMeshes[i]}, Material: {treeMaterials[i]}");
                }
            }
#endif
        }

#if UNITY_EDITOR
        s_EnqueueMarker.Begin();
#endif
        
        // Find tiles that need trees spawned (have mesh but no trees yet)
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference>()
            .WithNone<TreesSpawned>()
            .WithEntityAccess())
        {
            if (tile.ValueRO.meshGenerated)
            {
                // Only enqueue if not already in the queue (prevents duplicates across frames)
                if (_queuedEntities.Add(entity))
                {
                    _pendingTiles.Enqueue(entity);
#if UNITY_EDITOR
                    Debug.Log($"[TreeSpawning] Enqueued tile {tile.ValueRO.gridCoordinate}, Entity: {entity.Index}");
#endif
                }
#if UNITY_EDITOR
                else
                {
                    Debug.Log($"[TreeSpawning] Tile {tile.ValueRO.gridCoordinate}, Entity: {entity.Index} already queued, skipping");
                }
#endif
            }
        }

#if UNITY_EDITOR
        s_EnqueueMarker.End();
        s_SpawnMarker.Begin();
#endif

        // Reset frame counter
        _treesSpawnedThisFrame = 0;
        
        // Process pending tiles up to frame budget
        while (_pendingTiles.Count > 0 && _treesSpawnedThisFrame < config.maxTreesSpawnedPerFrame)
        {
            Entity tileEntity = _pendingTiles.Dequeue();
            
            // Remove from queued set immediately (tile is being processed now)
            _queuedEntities.Remove(tileEntity);
            
            // Check if tile still exists (could have been despawned)
            if (!EntityManager.Exists(tileEntity))
                continue;
            
            // Check if tile already has trees (race condition prevention)
            if (EntityManager.HasComponent<TreesSpawned>(tileEntity))
            {
#if UNITY_EDITOR
                Debug.Log($"[TreeSpawning] Tile {tileEntity.Index} already has TreesSpawned tag, skipping");
#endif
                continue;
            }
            
            // Spawn trees on this tile
            int treesSpawned = SpawnTreesOnTile(tileEntity, config, treePrefabs, treeMeshes, treeMaterials);
            _treesSpawnedThisFrame += treesSpawned;
            
            // Mark tile as having trees spawned
            EntityManager.AddComponent<TreesSpawned>(tileEntity);
            
            // If we've hit the frame budget, re-enqueue this tile's remaining trees
            // (Not needed since we spawn all trees for a tile at once, but good practice)
            if (_treesSpawnedThisFrame >= config.maxTreesSpawnedPerFrame)
                break;
        }

#if UNITY_EDITOR
        s_SpawnMarker.End();
        s_ProfilerMarker.End();
#endif

        // Dispose the tree prefabs array
        treePrefabs.Dispose();
    }

    /// <summary>
    /// Spawns trees on a single terrain tile.
    /// Returns the number of trees spawned.
    /// </summary>
    private int SpawnTreesOnTile(Entity tileEntity, TreeSpawnerConfig config, NativeArray<Entity> treePrefabs, Mesh[] treeMeshes, Material[] treeMaterials)
    {
        // Tree prefabs already copied in OnUpdate() - safe to use directly
        var prefabCount = treePrefabs.Length;
        if (prefabCount == 0)
            return 0;
        
        // Get tile data
        var tile = EntityManager.GetComponentData<TerrainTile>(tileEntity);
        var tileTransform = EntityManager.GetComponentData<LocalTransform>(tileEntity);
        var terrainConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
        
#if UNITY_EDITOR
        Debug.Log($"[TreeSpawning] Starting spawn for tile {tile.gridCoordinate}, Entity: {tileEntity.Index}");
#endif
        
        // Ensure we have the spawned tree reference buffer FIRST (before getting other buffers)
        // This prevents buffer invalidation from the structural change
        if (!EntityManager.HasBuffer<SpawnedTreeReference>(tileEntity))
        {
#if UNITY_EDITOR
            Debug.Log($"[TreeSpawning] Adding SpawnedTreeReference buffer to tile {tile.gridCoordinate}");
#endif
            EntityManager.AddBuffer<SpawnedTreeReference>(tileEntity);
        }
        
        // NOW get the vertex and normal buffers (after structural change is done)
        var vertices = EntityManager.GetBuffer<VertexElement>(tileEntity);
        var normals = EntityManager.GetBuffer<NormalElement>(tileEntity);
        
        if (vertices.Length == 0 || normals.Length == 0)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[TreeSpawning] Tile {tile.gridCoordinate} has no mesh data! Vertices: {vertices.Length}, Normals: {normals.Length}");
#endif
            return 0;
        }
        
        // Copy vertex and normal data to native arrays to avoid buffer invalidation issues
        // during tree spawning (when we add components)
        var vertexCount = vertices.Length;
        var vertexPositions = new NativeArray<float3>(vertexCount, Allocator.Temp);
        var vertexNormals = new NativeArray<float3>(vertexCount, Allocator.Temp);
        
        for (int i = 0; i < vertexCount; i++)
        {
            vertexPositions[i] = vertices[i].value;
            vertexNormals[i] = normals[i].value;
        }
        
        // Initialize random number generator with deterministic seed based on tile coordinate
        var random = new Unity.Mathematics.Random((uint)(tile.gridCoordinate.GetHashCode() + 12345));
        
        // Determine how many trees to spawn
        int treeCount = random.NextInt(config.minTreesPerTile, config.maxTreesPerTile + 1);
        
#if UNITY_EDITOR
        Debug.Log($"[TreeSpawning] Tile {tile.gridCoordinate} will spawn {treeCount} trees (min: {config.minTreesPerTile}, max: {config.maxTreesPerTile})");
#endif
        
        // Use a temporary list to collect spawned tree entities
        // This avoids the buffer invalidation issue from structural changes
        var tempSpawnedTrees = new NativeList<Entity>(treeCount, Allocator.Temp);
        
        int actualTreesSpawned = 0;
        int maxAttempts = treeCount * 3; // Allow multiple attempts per tree to find valid spots
        int attempts = 0;
        
        // Calculate grid dimensions for mesh sampling
        int vPerSide = terrainConfig.verticesPerSide;
        float tileSize = terrainConfig.tileSize;
        
        while (actualTreesSpawned < treeCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Generate random XZ position within tile bounds (truly random, not grid-based)
            float randomX = random.NextFloat(0f, tileSize);
            float randomZ = random.NextFloat(0f, tileSize);
            
            // Convert world position to grid coordinates for mesh sampling
            // The mesh is generated with vertices at regular intervals
            float gridX = (randomX / tileSize) * (vPerSide - 1);
            float gridZ = (randomZ / tileSize) * (vPerSide - 1);
            
            // Get the four surrounding vertices for interpolation
            int x0 = (int)math.floor(gridX);
            int z0 = (int)math.floor(gridZ);
            int x1 = math.min(x0 + 1, vPerSide - 1);
            int z1 = math.min(z0 + 1, vPerSide - 1);
            
            // Calculate interpolation factors
            float tx = gridX - x0;
            float tz = gridZ - z0;
            
            // Get vertex indices (vertices are stored in row-major order)
            int idx00 = z0 * vPerSide + x0;
            int idx10 = z0 * vPerSide + x1;
            int idx01 = z1 * vPerSide + x0;
            int idx11 = z1 * vPerSide + x1;
            
            // Bilinear interpolation of height
            float3 v00 = vertexPositions[idx00];
            float3 v10 = vertexPositions[idx10];
            float3 v01 = vertexPositions[idx01];
            float3 v11 = vertexPositions[idx11];
            
            // Interpolate along X axis first, then Z axis
            float3 vX0 = math.lerp(v00, v10, tx);
            float3 vX1 = math.lerp(v01, v11, tx);
            float3 interpolatedPosition = math.lerp(vX0, vX1, tz);
            
            // Use the random X and Z, but interpolated Y
            float3 localPosition = new float3(randomX, interpolatedPosition.y, randomZ);
            
            // Interpolate normals the same way
            float3 n00 = vertexNormals[idx00];
            float3 n10 = vertexNormals[idx10];
            float3 n01 = vertexNormals[idx01];
            float3 n11 = vertexNormals[idx11];
            
            float3 nX0 = math.lerp(n00, n10, tx);
            float3 nX1 = math.lerp(n01, n11, tx);
            float3 normal = math.normalize(math.lerp(nX0, nX1, tz));
            
            // Calculate world position
            float3 worldPosition = tileTransform.Position + localPosition;
            
            // Check height filter
            if (worldPosition.y < config.minSpawnHeight || worldPosition.y > config.maxSpawnHeight)
                continue;
            
            // Check slope filter (using pre-calculated threshold)
            // normal.y is the cosine of the angle from vertical
            if (normal.y < config.slopeThreshold)
                continue; // Too steep
            
            // Randomly select a tree prefab (use passed array, already copied in OnUpdate)
            int prefabIndex = random.NextInt(0, prefabCount);
            Entity treePrefab = treePrefabs[prefabIndex];
            
            // Instantiate the tree
            Entity treeEntity = EntityManager.Instantiate(treePrefab);
            
            // Remove ECS rendering components to prevent double rendering
            // Trees will only render via GlobalTreeInstanceSystem using Graphics.DrawMeshInstanced
            if (EntityManager.HasComponent<Unity.Rendering.MaterialMeshInfo>(treeEntity))
            {
                EntityManager.RemoveComponent<Unity.Rendering.MaterialMeshInfo>(treeEntity);
            }
            if (EntityManager.HasComponent<Unity.Rendering.RenderBounds>(treeEntity))
            {
                EntityManager.RemoveComponent<Unity.Rendering.RenderBounds>(treeEntity);
            }
            
            // Also remove from any linked entities (children)
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
            
            // Random rotation around Y axis
            quaternion rotation = quaternion.RotateY(random.NextFloat(0f, math.PI * 2f));
            
            // Set transform (position is WORLD position, not local)
            EntityManager.SetComponentData(treeEntity, new LocalTransform
            {
                Position = tileTransform.Position + localPosition,  // World position
                Rotation = rotation,
                Scale = 1f
            });
            
            // Track tile ownership without parent-child hierarchy (for performance)
            EntityManager.AddComponentData(treeEntity, new TreeTileOwnership
            {
                tileEntity = tileEntity,
                localOffset = localPosition  // Store local offset for position updates
            });
            
            // Add global instance rendering components
            EntityManager.AddComponent<GlobalTreeInstance>(treeEntity);
            
            // Add mesh and material data for batch rendering
            EntityManager.AddComponentData(treeEntity, new GlobalTreeInstanceData
            {
                mesh = treeMeshes[prefabIndex],
                material = treeMaterials[prefabIndex],
                prefabIndex = prefabIndex
            });
            
#if UNITY_EDITOR
            // Debug: First tree spawned on this tile
            if (actualTreesSpawned == 0)
            {
                Debug.Log($"[TreeSpawning] First tree on tile {tile.gridCoordinate}: Entity {treeEntity.Index}, Mesh: {treeMeshes[prefabIndex]?.name}, Material: {treeMaterials[prefabIndex]?.name}");
            }
#endif
            
            // Store tree entity in temporary list
            tempSpawnedTrees.Add(treeEntity);
            
            actualTreesSpawned++;
        }
        
        // After all structural changes are done, add all trees to the buffer at once
#if UNITY_EDITOR
        Debug.Log($"[TreeSpawning] Tile {tile.gridCoordinate} spawned {actualTreesSpawned} trees (attempted {attempts}), adding to buffer...");
#endif
        
        if (tempSpawnedTrees.Length > 0)
        {
            var spawnedTreesBuffer = EntityManager.GetBuffer<SpawnedTreeReference>(tileEntity);
#if UNITY_EDITOR
            Debug.Log($"[TreeSpawning] Buffer capacity before: {spawnedTreesBuffer.Capacity}, length: {spawnedTreesBuffer.Length}");
#endif
            foreach (var treeEntity in tempSpawnedTrees)
            {
                spawnedTreesBuffer.Add(new SpawnedTreeReference
                {
                    treeEntity = treeEntity
                });
            }
#if UNITY_EDITOR
            Debug.Log($"[TreeSpawning] Buffer after adding trees - length: {spawnedTreesBuffer.Length}, added {tempSpawnedTrees.Length} trees");
#endif
        }
        else
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[TreeSpawning] Tile {tile.gridCoordinate} - NO TREES SPAWNED! All filtered out.");
#endif
        }
        
        // Dispose native arrays (treePrefabs array is disposed in OnUpdate)
        vertexPositions.Dispose();
        vertexNormals.Dispose();
        tempSpawnedTrees.Dispose();
        
        return actualTreesSpawned;
    }
}

