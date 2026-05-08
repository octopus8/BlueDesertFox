using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that spawns bullets when BulletShooter.doShoot is true.
/// Gets bullets from the pool, positions them at the spawn point, and fires them forward.
/// </summary>
[UpdateBefore(typeof(ResetEventsSystem))]
public partial struct BulletShooterSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BulletShooter>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        state.RequireForUpdate<BulletPoolConfig>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get reference to pool system (non-Burst due to managed component access)
        var poolSystemHandle = state.World.GetExistingSystem<BulletPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
        {
            Debug.LogWarning("[BulletShooterSystem] BulletPoolSystem not found");
            return;
        }
        
        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<BulletPoolSystem>(poolSystemHandle);
        
        // Process all bullet shooters that want to shoot
        foreach (var (shooter, entity) in SystemAPI.Query<RefRW<BulletShooter>>().WithEntityAccess())
        {
            if (!shooter.ValueRO.doShoot)
                continue;
            
            // Reset the shoot flag (will be done again in ResetEventsSystem, but do it here too for safety)
            shooter.ValueRW.doShoot = false;
            
            // Get bullet spawn point reference (managed component)
            if (!state.EntityManager.HasComponent<BulletSpawnPointReference>(entity))
            {
                Debug.LogWarning("[BulletShooterSystem] BulletShooter entity missing BulletSpawnPointReference");
                continue;
            }
            
            var spawnPointRef = state.EntityManager.GetComponentObject<BulletSpawnPointReference>(entity);
            if (spawnPointRef == null || spawnPointRef.spawnPoint == null)
            {
                Debug.LogWarning("[BulletShooterSystem] BulletSpawnPointReference has null Transform");
                continue;
            }
            
            // Get bullet from pool
            Entity bulletEntity = poolSystem.GetFromPool(ref state);
            if (bulletEntity == Entity.Null)
            {
                Debug.LogWarning("[BulletShooterSystem] Failed to get bullet from pool");
                continue;
            }
            
            // Get spawn point position and direction
            Transform spawnTransform = spawnPointRef.spawnPoint;
            float3 spawnPosition = spawnTransform.position;
            float3 forward = spawnTransform.forward;
            quaternion spawnRotation = spawnTransform.rotation;
            
            // Set bullet transform
            state.EntityManager.SetComponentData(bulletEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = spawnRotation,
                Scale = 500f
            });
            
            // Set bullet velocity
            state.EntityManager.SetComponentData(bulletEntity, new PhysicsVelocity
            {
                Linear = forward * shooter.ValueRO.bulletSpeed,
                Angular = float3.zero
            });
            
            // Update bullet data (mark as active)
            state.EntityManager.SetComponentData(bulletEntity, new BulletData
            {
                spawnPosition = spawnPosition,
                creationTime = SystemAPI.Time.ElapsedTime,
                active = true
            });
            
            // Update last fire time
            shooter.ValueRW.lastFireTime = SystemAPI.Time.ElapsedTime;
            
            Debug.Log($"[BulletShooterSystem] Fired bullet at position {spawnPosition}, velocity {forward * shooter.ValueRO.bulletSpeed}");
        }
    }
}

