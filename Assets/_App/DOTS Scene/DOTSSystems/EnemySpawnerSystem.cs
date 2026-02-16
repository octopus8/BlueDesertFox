using Unity.Burst;
using Unity.Entities;
using UnityEngine;


[UpdateBefore(typeof(ResetEventsSystem))]
partial struct EnemySpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Ensure required singletons exist before system updates
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get the EntityCommandBuffer from the BeginSimulationEntityCommandBufferSystem
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        
        PrefabEntitiesReferences prefabEntitiesReferences = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
        
        foreach (var 
                     enemySpawner 
                 in SystemAPI.Query<
                     RefRW<EnemySpawner>
                 >())
        {
            if (enemySpawner.ValueRW.doSpawn)
            {
                Debug.Log("SPAWN!!");
                enemySpawner.ValueRW.doSpawn = false;
                
                // Use EntityCommandBuffer for structural changes
                Entity entity = ecb.Instantiate(prefabEntitiesReferences.prefabEntity);
                
                // Set the spline data on the spawned entity
                ecb.AddComponent(entity, enemySpawner.ValueRO.splineData);
            }
        }
        
    }
}
