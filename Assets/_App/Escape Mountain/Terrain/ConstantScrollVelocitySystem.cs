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
        RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();

        if (SystemAPI.TryGetSingleton<PlayerLocomotionPaused>(out var paused) && paused.Value)
        {
            scrollVelocity.ValueRW.speed = 0f;
            scrollVelocity.ValueRW.verticalSpeed = 0f;
            return;
        }

        var config = SystemAPI.GetSingleton<ConstantTerrainScrollVelocityConfig>();
        
        scrollVelocity.ValueRW.direction = config.direction;
        scrollVelocity.ValueRW.speed = config.speed;
        scrollVelocity.ValueRW.verticalSpeed = 0f;
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

