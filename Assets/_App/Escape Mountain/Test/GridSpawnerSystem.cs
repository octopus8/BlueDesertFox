using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// System that spawns objects in a grid pattern in the XY plane.
/// Spawns automatically on first update when hasSpawned is false.
/// Creates a centered grid at the specified Z position.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
partial struct GridSpawnerSystem : ISystem
{
    /// <summary>Registers the <see cref="BeginSimulationEntityCommandBufferSystem.Singleton"/> and <see cref="GridSpawner"/> requirements.</summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Ensure required components exist before system updates
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<GridSpawner>();
    }
    
    /// <summary>
    /// On the first frame, instantiates a centred <c>gridSize × gridSize</c> array of prefab entities
    /// in the XY plane at the configured Z position using an ECB, then marks <c>hasSpawned</c> to prevent
    /// re-spawning on subsequent frames.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get the EntityCommandBuffer from the BeginSimulationEntityCommandBufferSystem
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        
        foreach (var gridSpawner in SystemAPI.Query<RefRW<GridSpawner>>())
        {
            // Only spawn once
            if (!gridSpawner.ValueRO.hasSpawned)
            {
                int gridSize = gridSpawner.ValueRO.gridSize;
                float spacing = gridSpawner.ValueRO.spacing;
                float zPosition = gridSpawner.ValueRO.zPosition;
                Entity prefabEntity = gridSpawner.ValueRO.prefabEntity;
                
                // Calculate center offset to center the grid around the origin
                float centerOffset = -(gridSize - 1) * spacing * 0.5f;
                
                // Get the prefab's scale to preserve it
                float prefabScale = 1f;
                if (SystemAPI.HasComponent<LocalTransform>(prefabEntity))
                {
                    prefabScale = SystemAPI.GetComponent<LocalTransform>(prefabEntity).Scale;
                }
                
                // Spawn grid
                for (int x = 0; x < gridSize; x++)
                {
                    for (int y = 0; y < gridSize; y++)
                    {
                        // Calculate world position (centered around origin)
                        float3 position = new float3(
                            centerOffset + x * spacing,
                            centerOffset + y * spacing,
                            zPosition
                        );
                        
                        // Instantiate the prefab entity
                        Entity instance = ecb.Instantiate(prefabEntity);
                        
                        // Set the transform
                        ecb.SetComponent(instance, new LocalTransform
                        {
                            Position = position,
                            Rotation = quaternion.identity,
                            Scale = prefabScale
                        });
                    }
                }
                
                // Mark as spawned to prevent repeated spawning
                gridSpawner.ValueRW.hasSpawned = true;
            }
        }
    }
}

