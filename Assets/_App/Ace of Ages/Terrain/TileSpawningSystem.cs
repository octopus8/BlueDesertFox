using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// System that spawns and despawns terrain tiles based on player position.
/// Uses a NativeParallelHashMap to track active tiles efficiently.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TileSpawningSystem : ISystem
{
    private NativeParallelHashMap<int2, Entity> _activeTiles;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<WorldOriginOffset>();
        
        _activeTiles = new NativeParallelHashMap<int2, Entity>(256, Allocator.Persistent);
    }

    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_activeTiles.IsCreated)
            _activeTiles.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Get the player transform reference (managed component, cannot use Burst)
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        
        // Check if player transform is valid
        if (playerRef == null || playerRef.playerTransform == null)
        {
            return;
        }

        // Get player position from GameObject Transform
        float3 playerPosition = playerRef.playerTransform.position;
        
        // Calculate player's grid coordinate
        int2 playerGridCoord = new int2(
            (int)math.floor(playerPosition.x / config.tileSize),
            (int)math.floor(playerPosition.z / config.tileSize)
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
                float2 tileCenter = new float2(
                    gridCoord.x * config.tileSize + config.tileSize * 0.5f,
                    gridCoord.y * config.tileSize + config.tileSize * 0.5f
                );
                float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
                float distanceToTile = math.distance(tileCenter, playerPos2D);
                
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
            float2 tileCenter = new float2(
                gridCoord.x * config.tileSize + config.tileSize * 0.5f,
                gridCoord.y * config.tileSize + config.tileSize * 0.5f
            );
            float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
            float distanceToTile = math.distance(tileCenter, playerPos2D);
            
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
            
            // Calculate world position for this tile
            float3 tilePosition = new float3(
                gridCoord.x * config.tileSize,
                0,
                gridCoord.y * config.tileSize
            );
            
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
            
            ecb.AddComponent(tileEntity, new FloatingOriginEnabled());
            
            // Add buffers for mesh data
            ecb.AddBuffer<VertexElement>(tileEntity);
            ecb.AddBuffer<NormalElement>(tileEntity);
            ecb.AddBuffer<UVElement>(tileEntity);
            ecb.AddBuffer<IndexElement>(tileEntity);
        }
        
        // Despawn old tiles
        foreach (var gridCoord in tilesToDespawn)
        {
            if (_activeTiles.TryGetValue(gridCoord, out Entity tileEntity))
            {
                ecb.DestroyEntity(tileEntity);
                _activeTiles.Remove(gridCoord);
            }
        }
        
        // Play back ECB to actually create entities
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
        
        // Now add the newly created entities to _activeTiles using a query
        if (tilesToSpawn.Length > 0)
        {
            var query = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainTile>());
            var allTiles = query.ToEntityArray(Allocator.Temp);
            
            // Find newly created tiles and add them to _activeTiles
            foreach (var entity in allTiles)
            {
                var tile = state.EntityManager.GetComponentData<TerrainTile>(entity);
                
                // Check if this tile should be in our active set but isn't yet
                if (tilesToSpawn.Contains(tile.gridCoordinate) && !_activeTiles.ContainsKey(tile.gridCoordinate))
                {
                    _activeTiles.Add(tile.gridCoordinate, entity);
                }
            }
            
            allTiles.Dispose();
        }
        
        tilesToSpawn.Dispose();
        tilesToDespawn.Dispose();
    }
}




