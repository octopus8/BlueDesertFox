using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that spawns tree entities on terrain tiles after mesh generation.
/// Uses frame budgeting to prevent performance spikes and deterministic random placement.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainRenderingSystem))]
public partial class TerrainTreeSpawningSystem : SystemBase
{
    private NativeQueue<Entity> _pendingTiles;
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
    }

    protected override void OnDestroy()
    {
        if (_pendingTiles.IsCreated)
            _pendingTiles.Dispose();
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
        
        // Get tree prefabs buffer and COPY to native array immediately
        // This buffer will be invalidated by structural changes in SpawnTreesOnTile
        var configEntity = SystemAPI.GetSingletonEntity<TreeSpawnerConfig>();
        var treePrefabsBuffer = EntityManager.GetBuffer<TreePrefabElement>(configEntity);
        
        if (treePrefabsBuffer.Length == 0)
        {
#if UNITY_EDITOR
            s_ProfilerMarker.End();
#endif
            return;
        }
        
        // Copy tree prefab entities to native array to avoid buffer invalidation
        var treePrefabCount = treePrefabsBuffer.Length;
        var treePrefabs = new NativeArray<Entity>(treePrefabCount, Allocator.Temp);
        for (int i = 0; i < treePrefabCount; i++)
        {
            treePrefabs[i] = treePrefabsBuffer[i].prefabEntity;
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
                _pendingTiles.Enqueue(entity);
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
            
            // Check if tile still exists (could have been despawned)
            if (!EntityManager.Exists(tileEntity))
                continue;
            
            // Spawn trees on this tile
            int treesSpawned = SpawnTreesOnTile(tileEntity, config, treePrefabs);
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
    private int SpawnTreesOnTile(Entity tileEntity, TreeSpawnerConfig config, NativeArray<Entity> treePrefabs)
    {
        // Tree prefabs already copied in OnUpdate() - safe to use directly
        var prefabCount = treePrefabs.Length;
        if (prefabCount == 0)
            return 0;
        
        // Get tile data
        var tile = EntityManager.GetComponentData<TerrainTile>(tileEntity);
        var tileTransform = EntityManager.GetComponentData<LocalTransform>(tileEntity);
        
        // Ensure we have the spawned tree reference buffer FIRST (before getting other buffers)
        // This prevents buffer invalidation from the structural change
        if (!EntityManager.HasBuffer<SpawnedTreeReference>(tileEntity))
        {
            EntityManager.AddBuffer<SpawnedTreeReference>(tileEntity);
        }
        
        // NOW get the vertex and normal buffers (after structural change is done)
        var vertices = EntityManager.GetBuffer<VertexElement>(tileEntity);
        var normals = EntityManager.GetBuffer<NormalElement>(tileEntity);
        
        if (vertices.Length == 0 || normals.Length == 0)
        {
            return 0;
        }
        
        // Copy vertex and normal data to native arrays to avoid buffer invalidation issues
        // during tree spawning (when we add Parent components)
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
        
        // Use a temporary list to collect spawned tree entities
        // This avoids the buffer invalidation issue from structural changes
        var tempSpawnedTrees = new NativeList<Entity>(treeCount, Allocator.Temp);
        
        int actualTreesSpawned = 0;
        int maxAttempts = treeCount * 3; // Allow multiple attempts per tree to find valid spots
        int attempts = 0;
        
        while (actualTreesSpawned < treeCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Pick a random vertex (use copied data, not buffer)
            int vertexIndex = random.NextInt(0, vertexCount);
            float3 localPosition = vertexPositions[vertexIndex];
            float3 normal = vertexNormals[vertexIndex];
            
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
            
            // Random rotation around Y axis
            quaternion rotation = quaternion.RotateY(random.NextFloat(0f, math.PI * 2f));
            
            // Random scale
            float scale = random.NextFloat(config.minTreeScale, config.maxTreeScale);
            
            // Set transform (position is now local to parent tile)
            EntityManager.SetComponentData(treeEntity, new LocalTransform
            {
                Position = localPosition,  // Local to tile, not world position
                Rotation = rotation,
                Scale = scale
            });
            
            // Parent the tree to the tile (ECS will auto-destroy when tile is destroyed)
            EntityManager.AddComponentData(treeEntity, new Parent
            {
                Value = tileEntity
            });
            
            // Store tree entity in temporary list
            tempSpawnedTrees.Add(treeEntity);
            
            actualTreesSpawned++;
        }
        
        // After all structural changes are done, add all trees to the buffer at once
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
        
        // Dispose native arrays (treePrefabs array is disposed in OnUpdate)
        vertexPositions.Dispose();
        vertexNormals.Dispose();
        tempSpawnedTrees.Dispose();
        
        return actualTreesSpawned;
    }
}

