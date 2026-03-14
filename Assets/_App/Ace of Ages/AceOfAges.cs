using System.Collections;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class AceOfAges : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(TestFunc());
    }
    

    IEnumerator TestFunc()
    {
        yield return new WaitForSeconds(3);
        DoTestSpawn();
    }


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
