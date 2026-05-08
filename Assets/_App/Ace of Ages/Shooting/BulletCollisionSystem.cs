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
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get reference to pool system
        var poolSystemHandle = state.World.GetExistingSystem<BulletPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
            return;
        
        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<BulletPoolSystem>(poolSystemHandle);
        
        // Get physics world and simulation
        var physicsWorldSingleton = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();
        
        // IMPORTANT: Complete the physics simulation dependency before reading collision events
        // This ensures all physics jobs have finished writing to the collision event stream
        state.Dependency.Complete();
        
        // Collect bullets that collided
        var bulletsToReturn = new NativeList<Entity>(32, Allocator.Temp);
        
        // Iterate through all collision events
        var collisionEvents = simulationSingleton.AsSimulation().CollisionEvents;
        foreach (var collisionEvent in collisionEvents)
        {
            Entity entityA = collisionEvent.EntityA;
            Entity entityB = collisionEvent.EntityB;
            
            // Check if either entity is a bullet
            bool aIsBullet = state.EntityManager.HasComponent<Bullet>(entityA);
            bool bIsBullet = state.EntityManager.HasComponent<Bullet>(entityB);
            
            if (aIsBullet)
            {
                var bulletData = state.EntityManager.GetComponentData<BulletData>(entityA);
                if (bulletData.active && !bulletsToReturn.Contains(entityA))
                {
                    bulletsToReturn.Add(entityA);
                }
            }
            
            if (bIsBullet)
            {
                var bulletData = state.EntityManager.GetComponentData<BulletData>(entityB);
                if (bulletData.active && !bulletsToReturn.Contains(entityB))
                {
                    bulletsToReturn.Add(entityB);
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
        
        bulletsToReturn.Dispose();
    }
}


