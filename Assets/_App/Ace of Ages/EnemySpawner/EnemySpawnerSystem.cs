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
                
                // Get the spline data from the referenced spline entity
                if (SystemAPI.HasComponent<SplineDataComponent>(enemySpawner.ValueRO.splineEntity))
                {
                    SplineDataComponent splineData = SystemAPI.GetComponent<SplineDataComponent>(enemySpawner.ValueRO.splineEntity);
                    
                    // Spawn enemies in bowling pin formation
                    int formationCount = enemySpawner.ValueRO.formationCount;
                    float spacing = enemySpawner.ValueRO.formationSpacing;
                    
                    // Get the prefab's scale to preserve it
                    float prefabScale = 1f;
                    if (SystemAPI.HasComponent<LocalTransform>(prefabEntitiesReferences.prefabEntity))
                    {
                        prefabScale = SystemAPI.GetComponent<LocalTransform>(prefabEntitiesReferences.prefabEntity).Scale;
                    }
                    
                    for (int i = 0; i < formationCount; i++)
                    {
                        // Use EntityCommandBuffer for structural changes
                        Entity entity = ecb.Instantiate(prefabEntitiesReferences.prefabEntity);
                        
                        // Set the spline data on the spawned entity
                        ecb.AddComponent(entity, splineData);
                        
                        // Calculate bowling pin formation position
                        var formationData = CalculateBowlingPinPosition(i, spacing);
                        
                        // Add formation position component
                        ecb.AddComponent(entity, new FormationPosition
                        {
                            positionIndex = i,
                            lateralOffset = formationData.lateralOffset,
                            forwardOffset = formationData.forwardOffset
                        });
                        
                        // Get the initial position and rotation from the spline at the start (distanceRatio = 0)
                        if (splineData.splineData.IsCreated)
                        {
                            ref var spline = ref splineData.splineData.Value;
                            
                            // Calculate distance ratio based on forward offset
                            float distanceRatio = formationData.forwardOffset / spline.totalLength;
                            SplineSample initialSample = spline.Evaluate(distanceRatio);
                            
                            // Calculate the right vector (perpendicular to tangent)
                            float3 rightVector = math.normalize(math.cross(initialSample.upVector, initialSample.tangent));
                            
                            // Apply lateral offset
                            float3 offsetPosition = initialSample.position + rightVector * formationData.lateralOffset.x;
                            
                            // Calculate initial rotation from the spline's tangent
                            quaternion initialRotation = quaternion.LookRotation(initialSample.tangent, initialSample.upVector);
                            
                            // Set the transform component with the spline's initial position and rotation, preserving prefab's scale
                            ecb.SetComponent(entity, new LocalTransform
                            {
                                Position = offsetPosition,
                                Rotation = initialRotation,
                                Scale = prefabScale
                            });
                        }
                    }
                }
            }
        }
        
    }
    
    /// <summary>
    /// Calculates the position of a bowling pin in a standard 10-pin formation.
    /// Row 0 (back): 1 pin
    /// Row 1: 2 pins
    /// Row 2: 3 pins
    /// Row 3 (front): 4 pins
    /// </summary>
    private static (float3 lateralOffset, float forwardOffset) CalculateBowlingPinPosition(int pinIndex, float spacing)
    {
        // Bowling pin arrangement (standard 10-pin):
        // Position index: 0=back center, then row by row from back to front
        // Row 0: index 0
        // Row 1: indices 1, 2
        // Row 2: indices 3, 4, 5
        // Row 3: indices 6, 7, 8, 9
        
        int row;
        int positionInRow;
        
        if (pinIndex == 0)
        {
            row = 0;
            positionInRow = 0;
        }
        else if (pinIndex <= 2)
        {
            row = 1;
            positionInRow = pinIndex - 1;
        }
        else if (pinIndex <= 5)
        {
            row = 2;
            positionInRow = pinIndex - 3;
        }
        else
        {
            row = 3;
            positionInRow = pinIndex - 6;
        }
        
        // Calculate forward offset (row number determines depth)
        float forwardOffset = -row * spacing;
        
        // Calculate lateral offset (centered around 0)
        int pinsInRow = row + 1;
        float lateralSpacing = spacing * 0.866f; // Hexagonal spacing (sqrt(3)/2)
        float lateralOffset = (positionInRow - (pinsInRow - 1) * 0.5f) * lateralSpacing;
        
        return (new float3(lateralOffset, 0, 0), forwardOffset);
    }
}
