using Unity.Burst;
using Unity.Entities;

partial struct ResetEventsSystem : ISystem
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
            enemySpawner.ValueRW.doSpawn = false;
        }
    }
}
