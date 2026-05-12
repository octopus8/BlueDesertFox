using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that detects bullet collisions and returns them to the pool.
/// Checks for collisions with terrain and enemies.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSystemGroup))]
public partial struct BulletCollisionSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Bullet>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get reference to pool system
        var poolSystemHandle = state.World.GetExistingSystem<BulletPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
            return;
        
        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<BulletPoolSystem>(poolSystemHandle);
        
        // Get reference to dirt explosion pool system
        var explosionPoolSystemHandle = state.World.GetExistingSystem<DirtExplosionPoolSystem>();
        bool hasExplosionPool = explosionPoolSystemHandle != SystemHandle.Null;
        ref var explosionPoolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<DirtExplosionPoolSystem>(explosionPoolSystemHandle);
        
        // Get physics simulation
        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        
        // IMPORTANT: Complete the physics simulation dependency before reading collision events
        // This ensures all physics jobs have finished writing to the collision event stream
        state.Dependency.Complete();
        
        // Collect bullets that collided and their collision positions
        var bulletsToReturn = new NativeList<Entity>(32, Allocator.Temp);
        var terrainCollisionPositions = new NativeList<float3>(32, Allocator.Temp);
        
        // Iterate through all collision events
        var collisionEvents = simulationSingleton.AsSimulation().CollisionEvents;
        foreach (var collisionEvent in collisionEvents)
        {
            Entity entityA = collisionEvent.EntityA;
            Entity entityB = collisionEvent.EntityB;
            
            // Check if either entity is a bullet
            bool aIsBullet = state.EntityManager.HasComponent<Bullet>(entityA);
            bool bIsBullet = state.EntityManager.HasComponent<Bullet>(entityB);
            
            // Check if either entity is terrain
            bool aIsTerrain = state.EntityManager.HasComponent<TerrainTile>(entityA);
            bool bIsTerrain = state.EntityManager.HasComponent<TerrainTile>(entityB);
            
            if (aIsBullet)
            {
                var bulletData = state.EntityManager.GetComponentData<BulletData>(entityA);
                if (bulletData.active && !bulletsToReturn.Contains(entityA))
                {
                    bulletsToReturn.Add(entityA);
                    
                    // If bullet hit terrain, record position for explosion spawn
                    if (bIsTerrain)
                    {
                        var bulletTransform = state.EntityManager.GetComponentData<LocalTransform>(entityA);
                        terrainCollisionPositions.Add(bulletTransform.Position);
                    }
                }
            }
            
            if (bIsBullet)
            {
                var bulletData = state.EntityManager.GetComponentData<BulletData>(entityB);
                if (bulletData.active && !bulletsToReturn.Contains(entityB))
                {
                    bulletsToReturn.Add(entityB);
                    
                    // If bullet hit terrain, record position for explosion spawn
                    if (aIsTerrain)
                    {
                        var bulletTransform = state.EntityManager.GetComponentData<LocalTransform>(entityB);
                        terrainCollisionPositions.Add(bulletTransform.Position);
                    }
                }
            }
        }
        
        // Return collided bullets to pool
        for (int i = 0; i < bulletsToReturn.Length; i++)
        {
            Entity bullet = bulletsToReturn[i];
            
            // Mark as inactive
            state.EntityManager.SetComponentData(bullet, new BulletData
            {
                spawnPosition = float3.zero,
                creationTime = 0,
                active = false
            });
            
            // Reset velocity
            if (state.EntityManager.HasComponent<PhysicsVelocity>(bullet))
            {
                state.EntityManager.SetComponentData(bullet, new PhysicsVelocity
                {
                    Linear = float3.zero,
                    Angular = float3.zero
                });
            }
            
            // Move far away (off-screen)
            var transform = state.EntityManager.GetComponentData<LocalTransform>(bullet);
            transform.Position = new float3(0, -10000, 0);
            state.EntityManager.SetComponentData(bullet, transform);
            
            // Return to pool
            poolSystem.ReturnToPool(bullet);
        }
        
        if (bulletsToReturn.Length > 0)
        {
            Debug.Log($"[BulletCollisionSystem] Returned {bulletsToReturn.Length} bullets to pool (collision cleanup)");
        }
        
        // Spawn dirt explosions for terrain collisions
        if (hasExplosionPool && terrainCollisionPositions.Length > 0)
        {
            var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
            float prefabScale = 1f;
            if (state.EntityManager.HasComponent<LocalTransform>(prefabs.dirtExplosionSmallPrefab))
            {
                prefabScale = state.EntityManager.GetComponentData<LocalTransform>(prefabs.dirtExplosionSmallPrefab).Scale;
            }
            
            for (int i = 0; i < terrainCollisionPositions.Length; i++)
            {
                Entity explosion = explosionPoolSystem.GetFromPool(ref state);
                if (explosion == Entity.Null)
                {
                    Debug.LogWarning("[BulletCollisionSystem] Failed to get dirt explosion from pool");
                    continue;
                }
                
                // Set explosion transform at collision point
                state.EntityManager.SetComponentData(explosion, new LocalTransform
                {
                    Position = terrainCollisionPositions[i],
                    Rotation = quaternion.identity, // Upward-facing VFX
                    Scale = prefabScale
                });
                
                // Set explosion data (mark as active)
                state.EntityManager.SetComponentData(explosion, new DirtExplosionData
                {
                    spawnTime = SystemAPI.Time.ElapsedTime,
                    active = true
                });
            }
            
            Debug.Log($"[BulletCollisionSystem] Spawned {terrainCollisionPositions.Length} dirt explosions at terrain collision points");
        }
        
        bulletsToReturn.Dispose();
        terrainCollisionPositions.Dispose();
    }
}


