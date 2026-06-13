using System.Collections;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Scene entry point for the Ace of Ages demo. After a short delay, triggers
/// a test enemy spawn by setting <see cref="EnemySpawner.doSpawn"/> on all
/// active spawner components via the ECS <see cref="EntityManager"/>.
/// Replace this MonoBehaviour with gameplay-driven spawn logic for production use.
/// </summary>
public class AceOfAges : MonoBehaviour
{
    /// <summary>Starts the <see cref="TestFunc"/> coroutine that delays before triggering the test enemy spawn.</summary>
    void Start()
    {
        StartCoroutine(TestFunc());
    }
    
    /// <summary>Waits 3 seconds then calls <see cref="DoTestSpawn"/> to set all spawner <c>doSpawn</c> flags.</summary>
    IEnumerator TestFunc()
    {
        yield return new WaitForSeconds(3);
        DoTestSpawn();
    }

    /// <summary>Finds all <see cref="EnemySpawner"/> components in the ECS world and sets <c>doSpawn = true</c> on each to trigger a test formation spawn.</summary>
    private void DoTestSpawn()
    {
        EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<EnemySpawner>().Build(entityManager);
        NativeArray<EnemySpawner> enemySpawners = entityQuery.ToComponentDataArray<EnemySpawner>(Allocator.Temp);
        for (int i = 0; i < enemySpawners.Length; i++)
        {
            EnemySpawner enemySpawner = enemySpawners[i];
            enemySpawner.doSpawn = true;
            enemySpawners[i] = enemySpawner;
        }

        entityQuery.CopyFromComponentDataArray(enemySpawners);
    }
}
