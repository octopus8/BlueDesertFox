using Unity.Burst;
using Unity.Entities;

/// <summary>
/// System that destroys enemy entities marked as OutOfBounds.
/// Runs in LateSimulationSystemGroup to clean up after all movement updates.
/// </summary>
[UpdateInGroup(typeof(LateSimulationSystemGroup))]
partial struct FormationCleanupSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Use EndSimulationEntityCommandBufferSystem for cleanup operations
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        
        // Query for entities in OutOfBounds phase
        foreach (var (movementState, entity) in 
                 SystemAPI.Query<RefRO<FormationMovementState>>()
                     .WithEntityAccess())
        {
            if (movementState.ValueRO.phase == MovementPhase.OutOfBounds)
            {
                // Destroy the entity
                ecb.DestroyEntity(entity);
            }
        }
    }
}


