using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that provides constant scroll velocity for testing.
/// Writes fixed direction and speed to ScrollVelocity singleton each frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ScrollTerrainSystem))]
public partial struct ConstantScrollVelocitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ScrollVelocity>();
        state.RequireForUpdate<ConstantScrollVelocityConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ConstantScrollVelocityConfig>();
        RefRW<ScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<ScrollVelocity>();
        
        scrollVelocity.ValueRW.direction = config.direction;
        scrollVelocity.ValueRW.speed = config.speed;
    }
}

/// <summary>
/// Configuration component for constant scroll velocity.
/// </summary>
public struct ConstantScrollVelocityConfig : IComponentData
{
    public float3 direction;
    public float speed;
}

