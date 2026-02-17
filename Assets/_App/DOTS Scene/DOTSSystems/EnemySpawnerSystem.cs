using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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
                
                // Get the initial position and rotation from the spline at the start (distanceRatio = 0)
                if (enemySpawner.ValueRO.splineData.splineData.IsCreated)
                {
                    ref var spline = ref enemySpawner.ValueRO.splineData.splineData.Value;
                    SplineSample initialSample = spline.Evaluate(0f); // Start at the beginning of the spline
                    
                    // Calculate initial rotation from the spline's tangent
                    quaternion initialRotation = quaternion.LookRotation(initialSample.tangent, initialSample.upVector);
                    
                    // Set the transform component with the spline's initial position and rotation
                    ecb.SetComponent(entity, new LocalTransform
                    {
                        Position = initialSample.position,
                        Rotation = initialRotation,
                        Scale = 1f
                    });
                }
            }
        }
        
    }
}
