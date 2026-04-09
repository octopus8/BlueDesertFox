using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that provides constant scroll velocity for testing.
/// Writes fixed direction and speed to TerrainScrollVelocity singleton each frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ScrollTerrainSystem))]
public partial struct ConstantScrollVelocitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainScrollVelocity>();
        state.RequireForUpdate<ConstantTerrainScrollVelocityConfig>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<ConstantTerrainScrollVelocityConfig>();
        RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();
        
        scrollVelocity.ValueRW.direction = config.direction;
        scrollVelocity.ValueRW.speed = config.speed;
    }
}

/// <summary>
/// Configuration component for constant scroll velocity.
/// </summary>
public struct ConstantTerrainScrollVelocityConfig : IComponentData
{
    public float3 direction;
    public float speed;
}

