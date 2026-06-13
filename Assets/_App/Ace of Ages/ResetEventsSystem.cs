using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Resets per-frame event flags on ECS components at the end of each simulation tick so
/// that events triggered during a frame do not persist into the next.
/// Currently resets <see cref="EnemySpawner.doSpawn"/> and <see cref="BulletShooter.doShoot"/>.
/// </summary>
partial struct ResetEventsSystem : ISystem
{
    /// <summary>
    /// Iterates all <see cref="EnemySpawner"/> and <see cref="BulletShooter"/> components and
    /// clears their one-shot trigger flags (<c>doSpawn</c>, <c>doShoot</c>).
    /// </summary>
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
