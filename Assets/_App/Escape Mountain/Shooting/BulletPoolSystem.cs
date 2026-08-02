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
    private Entity _trackedConfigEntity;
    
    /// <summary>
    /// Allocates the pooled bullet queue and registers system requirements for
    /// <see cref="BulletPoolConfig"/> and <see cref="PrefabEntitiesReferences"/>.
    /// </summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BulletPoolConfig>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        
        _pooledBullets = new NativeQueue<Entity>(Allocator.Persistent);
        _initialized = false;
        _trackedConfigEntity = Entity.Null;
    }
    
    /// <summary>Disposes the pooled bullet queue and frees all associated native memory.</summary>
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_pooledBullets.IsCreated)
        {
            _pooledBullets.Dispose();
        }
    }

    /// <summary>
    /// Destroys pooled Default-World bullets when SubScene config disappears (scene reload).
    /// </summary>
    public void OnStopRunning(ref SystemState state)
    {
        ResetPool(ref state);
        _trackedConfigEntity = Entity.Null;
    }
    
    /// <summary>
    /// On the first frame, pre-instantiates <see cref="BulletPoolConfig.initialPoolSize"/> bullet
    /// entities from the <see cref="PrefabEntitiesReferences.bulletSimplePrefab"/>, initialises them
    /// as inactive at off-screen positions, and enqueues them in the pool. Runs only once per
    /// SubScene config lifetime (re-seeds after AutoLoad reload).
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        var configEntity = SystemAPI.GetSingletonEntity<BulletPoolConfig>();
        if (_trackedConfigEntity != configEntity)
        {
            // AutoLoad SubScene reload can skip OnStopRunning; drop stale Default-World pool entities.
            if (_trackedConfigEntity != Entity.Null || _initialized)
                ResetPool(ref state);
            _trackedConfigEntity = configEntity;
        }

        // Only initialize once per config
        if (_initialized)
            return;
        
        var config = SystemAPI.GetSingleton<BulletPoolConfig>();
        var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
        
        if (prefabs.bulletSimplePrefab == Entity.Null)
        {
            Debug.LogWarning("[BulletPoolSystem] bulletSimplePrefab is null, cannot initialize pool");
            return;
        }
        
        // Get the prefab's scale to preserve it
        float prefabScale = 1f;
        if (state.EntityManager.HasComponent<LocalTransform>(prefabs.bulletSimplePrefab))
        {
            prefabScale = state.EntityManager.GetComponentData<LocalTransform>(prefabs.bulletSimplePrefab).Scale;
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
                active = false,
                linearVelocityTerrainRelative = float3.zero
            });
            
            // Set initial transform far away (inactive bullets off-screen)
            state.EntityManager.SetComponentData(bullet, new LocalTransform
            {
                Position = new float3(0, -10000, 0), // Far below map
                Rotation = quaternion.identity,
                Scale = prefabScale
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
    }

    void ResetPool(ref SystemState state)
    {
        var em = state.EntityManager;

        // Destroy entities still sitting in the pool.
        if (_pooledBullets.IsCreated)
        {
            while (_pooledBullets.TryDequeue(out Entity bullet))
            {
                if (em.Exists(bullet))
                    em.DestroyEntity(bullet);
            }
        }

        // Also destroy any active/orphan bullets left in the Default World from the previous scene.
        using var query = em.CreateEntityQuery(ComponentType.ReadOnly<Bullet>());
        var bullets = query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < bullets.Length; i++)
        {
            if (em.Exists(bullets[i]))
                em.DestroyEntity(bullets[i]);
        }
        bullets.Dispose();

        _initialized = false;
    }
    
    /// <summary>
    /// Retrieves an inactive bullet entity from the pool for reuse. If the pool is empty and
    /// below <see cref="BulletPoolConfig.maxPoolSize"/>, a new bullet entity is instantiated and
    /// returned. Returns <see cref="Entity.Null"/> if the pool is both empty and at max capacity.
    /// </summary>
    /// <param name="state">The current <see cref="SystemState"/> used to access the <see cref="EntityManager"/>.</param>
    /// <returns>A pooled or newly-created bullet entity, or <see cref="Entity.Null"/> if unavailable.</returns>
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
        
        // Get the prefab's scale to preserve it
        float prefabScale = 1f;
        if (state.EntityManager.HasComponent<LocalTransform>(prefabs.bulletSimplePrefab))
        {
            prefabScale = state.EntityManager.GetComponentData<LocalTransform>(prefabs.bulletSimplePrefab).Scale;
        }
        
        // Create new bullet
        Entity bullet = state.EntityManager.Instantiate(prefabs.bulletSimplePrefab);
        
        state.EntityManager.AddComponentData(bullet, new Bullet());
        state.EntityManager.AddComponentData(bullet, new BulletData
        {
            spawnPosition = float3.zero,
            creationTime = 0,
            active = false,
            linearVelocityTerrainRelative = float3.zero
        });
        
        state.EntityManager.SetComponentData(bullet, new LocalTransform
        {
            Position = new float3(0, -10000, 0),
            Rotation = quaternion.identity,
            Scale = prefabScale
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
    /// Returns an active bullet entity to the pool so it can be reused on a future spawn.
    /// The caller is responsible for resetting the entity's state (position, velocity, data)
    /// before or after returning it. Does nothing if <paramref name="bullet"/> is <see cref="Entity.Null"/>.
    /// </summary>
    /// <param name="bullet">The bullet entity to return to the pool.</param>
    public void ReturnToPool(Entity bullet)
    {
        if (bullet != Entity.Null)
        {
            _pooledBullets.Enqueue(bullet);
        }
    }
}
