using Unity.Burst;
using Unity.Entities;
using UnityEngine;


[UpdateBefore(typeof(ResetEventsSystem))]
partial struct EnemySpawnerSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
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
                PrefabEntitiesReferences prefabEntitiesReferences = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
                Entity entity = state.EntityManager.Instantiate(prefabEntitiesReferences.prefabEntity);
                
                // Set the spline value on the UnitMover component
                UnitMover unitMover = state.EntityManager.GetComponentData<UnitMover>(entity);
                unitMover.spline = enemySpawner.ValueRO.spline;
                state.EntityManager.SetComponentData(entity, unitMover);
                
            }
        }
        
    }
}
