using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that manages dirt explosion lifecycle - returns explosions to pool after their lifetime expires.
/// Uses time-based cleanup (configured lifetime from DirtExplosionConfig).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct DirtExplosionLifecycleSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DirtExplosion>();
        state.RequireForUpdate<DirtExplosionConfig>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get reference to pool system
        var poolSystemHandle = state.World.GetExistingSystem<DirtExplosionPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
            return;
        
        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<DirtExplosionPoolSystem>(poolSystemHandle);
        
        var config = SystemAPI.GetSingleton<DirtExplosionConfig>();
        double currentTime = SystemAPI.Time.ElapsedTime;
        
        // Collect explosions to return to pool (can't modify during iteration)
        var explosionsToReturn = new NativeList<Entity>(32, Allocator.Temp);
        
        // Check all active explosions
        foreach (var (explosionData, entity) in 
            SystemAPI.Query<RefRO<DirtExplosionData>>()
                .WithAll<DirtExplosion>()
                .WithEntityAccess())
        {
            // Skip inactive explosions (already in pool)
            if (!explosionData.ValueRO.active)
                continue;
            
            // Calculate elapsed time since spawn
            double elapsedTime = currentTime - explosionData.ValueRO.spawnTime;
            
            // Return to pool if lifetime exceeded
            if (elapsedTime > config.lifetime)
            {
                explosionsToReturn.Add(entity);
            }
        }
        
        // Return explosions to pool
        for (int i = 0; i < explosionsToReturn.Length; i++)
        {
            Entity explosion = explosionsToReturn[i];
            
            // Mark as inactive. triggered=false so next activation re-fires the VFX.
            state.EntityManager.SetComponentData(explosion, new DirtExplosionData
            {
                spawnTime = 0,
                active = false,
                triggered = false
            });

            // Re-park the terrain anchor below the map. TerrainAnchorSystem will keep this
            // entity at y=-10000 (with harmless XZ drift) until the next activation rewrites
            // basePosition.
            state.EntityManager.SetComponentData(explosion, new TerrainAnchorTag
            {
                basePosition = new float3(0, -10000, 0)
            });
            
            // Move far away (off-screen)
            var transform = state.EntityManager.GetComponentData<LocalTransform>(explosion);
            transform.Position = new float3(0, -10000, 0);
            state.EntityManager.SetComponentData(explosion, transform);
            
            // Return to pool
            poolSystem.ReturnToPool(explosion);
        }
        
        if (explosionsToReturn.Length > 0)
        {
            Debug.Log($"[DirtExplosionLifecycleSystem] Returned {explosionsToReturn.Length} explosions to pool (lifetime cleanup)");
        }
        
        explosionsToReturn.Dispose();
    }
}



