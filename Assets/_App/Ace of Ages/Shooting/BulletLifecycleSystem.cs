using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that manages bullet lifecycle - returns bullets to pool when they exceed max distance.
/// Uses distance-based cleanup (200m from spawn point).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(BulletShooterSystem))]
public partial struct BulletLifecycleSystem : ISystem
{
    private const float MAX_BULLET_DISTANCE = 200f;
    private const float MAX_BULLET_DISTANCE_SQ = MAX_BULLET_DISTANCE * MAX_BULLET_DISTANCE;
    
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Bullet>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get reference to pool system
        var poolSystemHandle = state.World.GetExistingSystem<BulletPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
            return;
        
        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<BulletPoolSystem>(poolSystemHandle);
        
        // Collect bullets to return to pool (can't modify during iteration)
        var bulletsToReturn = new NativeList<Entity>(32, Allocator.Temp);
        
        // Check all active bullets
        foreach (var (bulletData, transform, entity) in 
            SystemAPI.Query<RefRO<BulletData>, RefRO<LocalTransform>>()
                .WithAll<Bullet>()
                .WithEntityAccess())
        {
            // Skip inactive bullets (already in pool)
            if (!bulletData.ValueRO.active)
                continue;
            
            // Calculate distance from spawn point
            float3 currentPosition = transform.ValueRO.Position;
            float3 spawnPosition = bulletData.ValueRO.spawnPosition;
            float distanceSq = math.distancesq(currentPosition, spawnPosition);
            
            // Return to pool if beyond max distance
            if (distanceSq > MAX_BULLET_DISTANCE_SQ)
            {
                bulletsToReturn.Add(entity);
            }
        }
        
        // Return bullets to pool
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
            Debug.Log($"[BulletLifecycleSystem] Returned {bulletsToReturn.Length} bullets to pool (distance cleanup)");
        }
        
        bulletsToReturn.Dispose();
    }
}


