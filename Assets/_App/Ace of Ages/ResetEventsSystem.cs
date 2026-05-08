using Unity.Burst;
using Unity.Entities;

partial struct ResetEventsSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Reset enemy spawner events
        foreach (var 
                     enemySpawner 
                 in SystemAPI.Query<
                     RefRW<EnemySpawner>
                 >())
        {
            enemySpawner.ValueRW.doSpawn = false;
        }
        
        // Reset bullet shooter events
        foreach (var 
                     bulletShooter 
                 in SystemAPI.Query<
                     RefRW<BulletShooter>
                 >())
        {
            bulletShooter.ValueRW.doShoot = false;
        }
    }
}
