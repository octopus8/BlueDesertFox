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

    /// <summary>
    /// Allocates the active-tile map and registers required singletons
    /// (<see cref="PlayerTransformReference"/>, <see cref="TerrainTileConfig"/>, <see cref="ScrollOffset"/>).
    /// </summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<ScrollOffset>();
        state.RequireForUpdate<CameraDataSingleton>();
        
        _activeTiles = new NativeParallelHashMap<int2, Entity>(256, Allocator.Persistent);
    }

    /// <summary>Disposes the active-tile hash map and frees all native memory.</summary>
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_activeTiles.IsCreated)
            _activeTiles.Dispose();
    }

    /// <summary>
    /// Computes the set of grid tiles required around the player's current position (offset by
    /// <see cref="ScrollOffset"/>), spawns any missing tiles as new ECS entities with empty mesh
    /// buffers, and destroys tiles that have moved outside the view distance ring.
    /// Also explicitly destroys spawned trees on despawned tiles.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Early exit if both rendering and physics are disabled - no need to spawn tiles
        if (!config.renderTerrain && !config.enablePhysicsColliders)
        {
            return;
        }
        
        // Read cached player position from the blittable singleton (written end of previous frame).
        // One frame of latency is imperceptible at the 500m spawn ring scale.
        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
        float3 playerPosition = cameraData.position;
        
        // Get scroll offset (used for tile position offsetting, not player position)
        var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
        
        // Calculate "effective" player position for grid determination
        // This accounts for scroll so we check the right grid tiles
        float3 effectivePlayerPosition = playerPosition + scrollOffset.accumulatedOffset;
        
        // Calculate player's grid coordinate (based on effective position with scroll)
        int2 playerGridCoord = new int2(
            (int)math.floor(effectivePlayerPosition.x / config.tileSize),
            (int)math.floor(effectivePlayerPosition.z / config.tileSize)
        );
        
        // Calculate view distance in tiles
        int viewDistanceInTiles = (int)math.ceil(config.viewDistance / config.tileSize);
        
        // Create lists to track which tiles to spawn and despawn
        var tilesToSpawn = new NativeList<int2>(Allocator.Temp);
        var tilesToDespawn = new NativeList<int2>(Allocator.Temp);
        
        // Determine which tiles should be active
        for (int x = -viewDistanceInTiles; x <= viewDistanceInTiles; x++)
        {
            for (int z = -viewDistanceInTiles; z <= viewDistanceInTiles; z++)
            {
                int2 gridCoord = playerGridCoord + new int2(x, z);
                
                // Check if tile is within view distance (circular)
                // Calculate actual scrolled tile center position (apply directional offset)
                float3 tileCenterBase = new float3(
                    gridCoord.x * config.tileSize + config.tileSize * 0.5f,
                    0,
                    gridCoord.y * config.tileSize + config.tileSize * 0.5f
                );
                float3 tileCenterScrolled = tileCenterBase - scrollOffset.accumulatedOffset;
                float distanceToTile = math.distance(tileCenterScrolled, playerPosition);
                
                if (distanceToTile <= config.viewDistance)
                {
                    // This tile should be active
                    if (!_activeTiles.ContainsKey(gridCoord))
                    {
                        tilesToSpawn.Add(gridCoord);
                    }
                }
            }
        }
        
        // Find tiles that are too far away
        var tileKeys = _activeTiles.GetKeyArray(Allocator.Temp);
        foreach (var gridCoord in tileKeys)
        {
            // Calculate actual scrolled tile center position (apply directional offset)
            float3 tileCenterBase = new float3(
                gridCoord.x * config.tileSize + config.tileSize * 0.5f,
                0,
                gridCoord.y * config.tileSize + config.tileSize * 0.5f
            );
            float3 tileCenterScrolled = tileCenterBase - scrollOffset.accumulatedOffset;
            float distanceToTile = math.distance(tileCenterScrolled, playerPosition);
            
            if (distanceToTile > config.viewDistance)
            {
                tilesToDespawn.Add(gridCoord);
            }
        }
        tileKeys.Dispose();
        
        // Spawn new tiles
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        
        // Create entities via ECB
        foreach (var gridCoord in tilesToSpawn)
        {
            Entity tileEntity = ecb.CreateEntity();
            
            // Calculate world position for this tile (centered, subtract directional scroll offset)
            // Tile transform is placed at the CENTER of the tile for accurate LOD distance calculations
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
            
            // Add LocalToWorld explicitly for rendering
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
            
            // Add buffers for mesh data
            ecb.AddBuffer<VertexElement>(tileEntity);
            ecb.AddBuffer<NormalElement>(tileEntity);
            ecb.AddBuffer<UVElement>(tileEntity);
            ecb.AddBuffer<IndexElement>(tileEntity);
            
            // Add buffer for tracking spawned trees (for cleanup)
            ecb.AddBuffer<SpawnedStaticObjectReference>(tileEntity);
        }
        
        // Despawn old tiles
        foreach (var gridCoord in tilesToDespawn)
        {
            if (_activeTiles.TryGetValue(gridCoord, out Entity tileEntity))
            {
                // Explicitly destroy child static objects BEFORE destroying tile
                // While Parent component should auto-destroy children, explicit cleanup
                // ensures objects are removed even if transform hierarchy isn't fully updated
                if (state.EntityManager.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
                {
                    var spawnedObjects = state.EntityManager.GetBuffer<SpawnedStaticObjectReference>(tileEntity);
                    foreach (var objectRef in spawnedObjects)
                    {
                        if (state.EntityManager.Exists(objectRef.objectEntity))
                        {
                            ecb.DestroyEntity(objectRef.objectEntity);
                        }
                    }
                }
                
                ecb.DestroyEntity(tileEntity);
                _activeTiles.Remove(gridCoord);
            }
        }
        
        // Play back ECB to actually create entities
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        
        // Now add the newly created entities to _activeTiles using zero-GC iteration
        if (tilesToSpawn.Length > 0)
        {
            // Convert tilesToSpawn to a HashSet for O(1) lookups (avoid O(n²) complexity from Contains())
            var spawnedCoords = new NativeHashSet<int2>(tilesToSpawn.Length, Allocator.Temp);
            for (int i = 0; i < tilesToSpawn.Length; i++)
            {
                spawnedCoords.Add(tilesToSpawn[i]);
            }
            
            // Use direct iteration instead of ToEntityArray() to avoid GC allocations
            foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>().WithEntityAccess())
            {
                // Check if this tile should be in our active set but isn't yet
                // O(1) lookup instead of O(n) Contains() on NativeList
                if (spawnedCoords.Contains(tile.ValueRO.gridCoordinate) && !_activeTiles.ContainsKey(tile.ValueRO.gridCoordinate))
                {
                    _activeTiles.Add(tile.ValueRO.gridCoordinate, entity);
                }
            }
            
            spawnedCoords.Dispose();
        }        
        
        tilesToSpawn.Dispose();
        tilesToDespawn.Dispose();
    }
}


















