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
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DirtExplosionConfig>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        
        _pooledExplosions = new NativeQueue<Entity>(Allocator.Persistent);
        _initialized = false;
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_pooledExplosions.IsCreated)
        {
            _pooledExplosions.Dispose();
        }
    }
    
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
    /// Gets a dirt explosion entity from the pool. Returns Entity.Null if pool is empty.
    /// </summary>
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
    /// Returns a dirt explosion entity to the pool for reuse.
    /// </summary>
    public void ReturnToPool(Entity explosion)
    {
        if (explosion != Entity.Null)
        {
            _pooledExplosions.Enqueue(explosion);
        }
    }
}

