using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that spawns bullets when BulletShooter.doShoot is true.
/// Gets bullets from the pool, positions them at the spawn point using entity transform + offset, and fires them forward.
/// Bullet velocity includes the terrain scroll velocity so bullets fly relative to the terrain reference frame.
/// </summary>
[UpdateBefore(typeof(ResetEventsSystem))]
public partial struct BulletShooterSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BulletShooter>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        state.RequireForUpdate<BulletPoolConfig>();
        state.RequireForUpdate<BulletSpawnPointReference>();
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
        foreach (var (shooter, transform, spawnPointRef) in 
            SystemAPI.Query<RefRW<BulletShooter>, RefRO<LocalTransform>, RefRO<BulletSpawnPointReference>>())
        {
            if (!shooter.ValueRO.doShoot)
                continue;
            
            // Reset the shoot flag (will be done again in ResetEventsSystem, but do it here too for safety)
            shooter.ValueRW.doShoot = false;
            
            // Get bullet from pool
            Entity bulletEntity = poolSystem.GetFromPool(ref state);
            if (bulletEntity == Entity.Null)
            {
                Debug.LogWarning("[BulletShooterSystem] Failed to get bullet from pool");
                continue;
            }
            
            // Get the prefab's scale to preserve it
            var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
            float prefabScale = 1f;
            if (SystemAPI.HasComponent<LocalTransform>(prefabs.bulletSimplePrefab))
            {
                prefabScale = SystemAPI.GetComponent<LocalTransform>(prefabs.bulletSimplePrefab).Scale;
            }
            
            // Calculate spawn point position and rotation in world space
            // Apply the local offset to the ship's world transform
            var shipTransform = transform.ValueRO;
            float3 spawnPosition = shipTransform.Position + math.rotate(shipTransform.Rotation, spawnPointRef.ValueRO.localOffset);
            quaternion spawnRotation = math.mul(shipTransform.Rotation, spawnPointRef.ValueRO.localRotation);
            float3 forward = math.mul(spawnRotation, new float3(0, 0, 1)); // Get forward direction from rotation
            
            // Set bullet transform (preserving prefab scale)
            state.EntityManager.SetComponentData(bulletEntity, new LocalTransform
            {
                Position = spawnPosition,
                Rotation = spawnRotation,
                Scale = prefabScale
            });
            
            // Subtract terrain scroll velocity so the bullet travels at bulletSpeed in the terrain
            // reference frame. Tiles move at -terrainVelocity in world space; subtracting gives the
            // correct terrain-anchored trajectory.
            float3 terrainVelocity = float3.zero;
            if (SystemAPI.HasSingleton<TerrainScrollVelocity>())
            {
                var sv = SystemAPI.GetSingleton<TerrainScrollVelocity>();
                terrainVelocity = sv.direction * sv.speed;
            }
            
            float3 bulletVelocity = forward * shooter.ValueRO.bulletSpeed - terrainVelocity;
            
            // Set bullet velocity
            state.EntityManager.SetComponentData(bulletEntity, new PhysicsVelocity
            {
                Linear = bulletVelocity,
                Angular = float3.zero
            });
            
            // Update bullet data (mark as active)
            state.EntityManager.SetComponentData(bulletEntity, new BulletData
            {
                spawnPosition = spawnPosition,
                creationTime = SystemAPI.Time.ElapsedTime,
                active = true,
                linearVelocityTerrainRelative = bulletVelocity + terrainVelocity
            });
            
            // Update last fire time
            shooter.ValueRW.lastFireTime = SystemAPI.Time.ElapsedTime;
            
            Debug.Log($"[BulletShooterSystem] Fired bullet at position {spawnPosition}, velocity {bulletVelocity} (scroll contribution: {terrainVelocity})");
        }
    }
}

