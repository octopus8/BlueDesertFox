using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that manages a pool of reusable dirt explosion entities.
/// Pre-spawns explosions at initialization and provides GetFromPool/ReturnToPool helpers.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct DirtExplosionPoolSystem : ISystem
{
    private NativeQueue<Entity> _pooledExplosions;
    private bool _initialized;
    
    /// <summary>
    /// Allocates the pooled explosion queue and registers system requirements for
    /// <see cref="DirtExplosionConfig"/> and <see cref="PrefabEntitiesReferences"/>.
    /// </summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DirtExplosionConfig>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        
        _pooledExplosions = new NativeQueue<Entity>(Allocator.Persistent);
        _initialized = false;
    }
    
    /// <summary>Disposes the pooled explosion queue and frees all associated native memory.</summary>
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_pooledExplosions.IsCreated)
        {
            _pooledExplosions.Dispose();
        }
    }
    
    /// <summary>
    /// On the first frame, pre-instantiates <see cref="DirtExplosionConfig.initialPoolSize"/> explosion
    /// entities from the <see cref="PrefabEntitiesReferences.dirtExplosionSmallPrefab"/>, initialises them
    /// as inactive at off-screen positions with a <see cref="TerrainAnchorTag"/>, and enqueues them.
    /// Runs only once.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        // Only initialize once
        if (_initialized)
            return;
        
        var config = SystemAPI.GetSingleton<DirtExplosionConfig>();
        var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
        
        if (prefabs.dirtExplosionSmallPrefab == Entity.Null)
        {
            Debug.LogWarning("[DirtExplosionPoolSystem] dirtExplosionSmallPrefab is null, cannot initialize pool");
            return;
        }
        
        // Get the prefab's scale to preserve it
        float prefabScale = 1f;
        if (state.EntityManager.HasComponent<LocalTransform>(prefabs.dirtExplosionSmallPrefab))
        {
            prefabScale = state.EntityManager.GetComponentData<LocalTransform>(prefabs.dirtExplosionSmallPrefab).Scale;
        }
        
        // Pre-spawn initial pool of explosions
        for (int i = 0; i < config.initialPoolSize; i++)
        {
            Entity explosion = state.EntityManager.Instantiate(prefabs.dirtExplosionSmallPrefab);
            
            // Initialize explosion as inactive
            state.EntityManager.AddComponentData(explosion, new DirtExplosion());
            state.EntityManager.AddComponentData(explosion, new DirtExplosionData
            {
                spawnTime = 0,
                active = false
            });

            // Tag as a terrain anchor so the explosion rides the terrain scroll once
            // activated. Pooled entities are parked below the map; TerrainAnchorSystem will
            // keep them there (apart from harmless XZ drift) until BulletCollisionSystem
            // rewrites basePosition on activation.
            state.EntityManager.AddComponentData(explosion, new TerrainAnchorTag
            {
                basePosition = new float3(0, -10000, 0)
            });
            
            // Set initial transform far away (inactive explosions off-screen)
            state.EntityManager.SetComponentData(explosion, new LocalTransform
            {
                Position = new float3(0, -10000, 0), // Far below map
                Rotation = quaternion.identity,
                Scale = prefabScale
            });
            
            _pooledExplosions.Enqueue(explosion);
        }
        
        _initialized = true;
        Debug.Log($"[DirtExplosionPoolSystem] Initialized pool with {config.initialPoolSize} explosions");
    }
    
    /// <summary>
    /// Retrieves an inactive dirt explosion entity from the pool for reuse. If the pool is empty
    /// and below <see cref="DirtExplosionConfig.maxPoolSize"/>, a new explosion entity is instantiated.
    /// Returns <see cref="Entity.Null"/> if the pool is both empty and at max capacity.
    /// </summary>
    /// <param name="state">The current <see cref="SystemState"/> used to access the <see cref="EntityManager"/>.</param>
    /// <returns>A pooled or newly-created explosion entity, or <see cref="Entity.Null"/> if unavailable.</returns>
    public Entity GetFromPool(ref SystemState state)
    {
        if (_pooledExplosions.Count > 0)
        {
            return _pooledExplosions.Dequeue();
        }
        
        // Pool exhausted - grow pool dynamically
        var config = SystemAPI.GetSingleton<DirtExplosionConfig>();
        var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
        
        if (config.currentPoolCount >= config.maxPoolSize)
        {
            Debug.LogWarning($"[DirtExplosionPoolSystem] Pool exhausted and at max size ({config.maxPoolSize}), cannot spawn explosion");
            return Entity.Null;
        }
        
        // Get the prefab's scale to preserve it
        float prefabScale = 1f;
        if (state.EntityManager.HasComponent<LocalTransform>(prefabs.dirtExplosionSmallPrefab))
        {
            prefabScale = state.EntityManager.GetComponentData<LocalTransform>(prefabs.dirtExplosionSmallPrefab).Scale;
        }
        
        // Create new explosion
        Entity explosion = state.EntityManager.Instantiate(prefabs.dirtExplosionSmallPrefab);
        
        state.EntityManager.AddComponentData(explosion, new DirtExplosion());
        state.EntityManager.AddComponentData(explosion, new DirtExplosionData
        {
            spawnTime = 0,
            active = false
        });

        state.EntityManager.AddComponentData(explosion, new TerrainAnchorTag
        {
            basePosition = new float3(0, -10000, 0)
        });
        
        state.EntityManager.SetComponentData(explosion, new LocalTransform
        {
            Position = new float3(0, -10000, 0),
            Rotation = quaternion.identity,
            Scale = prefabScale
        });
        
        config.currentPoolCount++;
        SystemAPI.SetSingleton(config);
        
        Debug.LogWarning($"[DirtExplosionPoolSystem] Pool grew to {config.currentPoolCount} explosions");
        
        return explosion;
    }
    
    /// <summary>
    /// Returns an active explosion entity to the pool so it can be reused on a future impact.
    /// The caller is responsible for resetting the entity's state before or after returning it.
    /// Does nothing if <paramref name="explosion"/> is <see cref="Entity.Null"/>.
    /// </summary>
    /// <param name="explosion">The explosion entity to return to the pool.</param>
    public void ReturnToPool(Entity explosion)
    {
        if (explosion != Entity.Null)
        {
            _pooledExplosions.Enqueue(explosion);
        }
    }
}

