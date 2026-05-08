using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that manages a pool of reusable bullet entities.
/// Pre-spawns bullets at initialization and provides GetFromPool/ReturnToPool helpers.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct BulletPoolSystem : ISystem
{
    private NativeQueue<Entity> _pooledBullets;
    private bool _initialized;
    
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BulletPoolConfig>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        
        _pooledBullets = new NativeQueue<Entity>(Allocator.Persistent);
        _initialized = false;
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_pooledBullets.IsCreated)
        {
            _pooledBullets.Dispose();
        }
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Only initialize once
        if (_initialized)
            return;
        
        var config = SystemAPI.GetSingleton<BulletPoolConfig>();
        var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
        
        if (prefabs.bulletSimplePrefab == Entity.Null)
        {
            Debug.LogWarning("[BulletPoolSystem] bulletSimplePrefab is null, cannot initialize pool");
            return;
        }
        
        // Pre-spawn initial pool of bullets
        for (int i = 0; i < config.initialPoolSize; i++)
        {
            Entity bullet = state.EntityManager.Instantiate(prefabs.bulletSimplePrefab);
            
            // Initialize bullet as inactive
            state.EntityManager.AddComponentData(bullet, new Bullet());
            state.EntityManager.AddComponentData(bullet, new BulletData
            {
                spawnPosition = float3.zero,
                creationTime = 0,
                active = false
            });
            
            // Set initial transform far away (inactive bullets off-screen)
            state.EntityManager.SetComponentData(bullet, new LocalTransform
            {
                Position = new float3(0, -10000, 0), // Far below map
                Rotation = quaternion.identity,
                Scale = 1f
            });
            
            // Ensure PhysicsVelocity exists and is zeroed
            if (!state.EntityManager.HasComponent<PhysicsVelocity>(bullet))
            {
                state.EntityManager.AddComponentData(bullet, new PhysicsVelocity
                {
                    Linear = float3.zero,
                    Angular = float3.zero
                });
            }
            else
            {
                state.EntityManager.SetComponentData(bullet, new PhysicsVelocity
                {
                    Linear = float3.zero,
                    Angular = float3.zero
                });
            }
            
            _pooledBullets.Enqueue(bullet);
        }
        
        _initialized = true;
        Debug.Log($"[BulletPoolSystem] Initialized pool with {config.initialPoolSize} bullets");
    }
    
    /// <summary>
    /// Gets a bullet entity from the pool. Returns Entity.Null if pool is empty.
    /// </summary>
    public Entity GetFromPool(ref SystemState state)
    {
        if (_pooledBullets.Count > 0)
        {
            return _pooledBullets.Dequeue();
        }
        
        // Pool exhausted - grow pool dynamically
        var config = SystemAPI.GetSingleton<BulletPoolConfig>();
        var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
        
        if (config.currentPoolCount >= config.maxPoolSize)
        {
            Debug.LogWarning($"[BulletPoolSystem] Pool exhausted and at max size ({config.maxPoolSize}), cannot spawn bullet");
            return Entity.Null;
        }
        
        // Create new bullet
        Entity bullet = state.EntityManager.Instantiate(prefabs.bulletSimplePrefab);
        
        state.EntityManager.AddComponentData(bullet, new Bullet());
        state.EntityManager.AddComponentData(bullet, new BulletData
        {
            spawnPosition = float3.zero,
            creationTime = 0,
            active = false
        });
        
        state.EntityManager.SetComponentData(bullet, new LocalTransform
        {
            Position = new float3(0, -10000, 0),
            Rotation = quaternion.identity,
            Scale = 1f
        });
        
        if (!state.EntityManager.HasComponent<PhysicsVelocity>(bullet))
        {
            state.EntityManager.AddComponentData(bullet, new PhysicsVelocity());
        }
        
        config.currentPoolCount++;
        SystemAPI.SetSingleton(config);
        
        Debug.LogWarning($"[BulletPoolSystem] Pool grew to {config.currentPoolCount} bullets");
        
        return bullet;
    }
    
    /// <summary>
    /// Returns a bullet entity to the pool for reuse.
    /// </summary>
    public void ReturnToPool(Entity bullet)
    {
        if (bullet != Entity.Null)
        {
            _pooledBullets.Enqueue(bullet);
        }
    }
}

