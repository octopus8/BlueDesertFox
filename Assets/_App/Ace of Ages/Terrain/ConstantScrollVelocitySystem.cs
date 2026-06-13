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
    /// <summary>Registers <see cref="TerrainScrollVelocity"/> and <see cref="ConstantTerrainScrollVelocityConfig"/> requirements.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainScrollVelocity>();
        state.RequireForUpdate<ConstantTerrainScrollVelocityConfig>();
    }

    /// <summary>
    /// Writes the fixed direction and speed from <see cref="ConstantTerrainScrollVelocityConfig"/>
    /// into the <see cref="TerrainScrollVelocity"/> singleton, overriding any player-driven velocity.
    /// </summary>
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
    /// <summary>Normalized scroll direction in world space (XZ plane).</summary>
    public float3 direction;
    /// <summary>Scroll speed in world units per second.</summary>
    public float speed;
}

