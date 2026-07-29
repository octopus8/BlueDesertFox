using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;

/// <summary>
/// Keeps active bullets' world velocity aligned with changing terrain scroll velocity.
/// Spawn code stores invariant <see cref="BulletData.linearVelocityTerrainRelative"/>; each fixed step we apply
/// <c>Linear = linearVelocityTerrainRelative - terrainVelocity</c> before physics integration.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(PhysicsSystemGroup))]
public partial struct BulletTerrainScrollVelocitySystem : ISystem
{
    /// <summary>Registers the <see cref="Bullet"/> requirement so the system only runs when bullets exist.</summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Bullet>();
    }

    /// <summary>
    /// Reads the current <see cref="TerrainScrollVelocity"/> and reapplies each active bullet's
    /// terrain-relative velocity (<see cref="BulletData.linearVelocityTerrainRelative"/> minus scroll
    /// velocity) to <see cref="PhysicsVelocity.Linear"/> before physics integration this fixed step.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float3 terrainVelocity = float3.zero;
        if (SystemAPI.TryGetSingleton(out TerrainScrollVelocity sv))
            terrainVelocity = sv.WorldVelocity;

        foreach (var (velocityRw, bulletData) in SystemAPI.Query<RefRW<PhysicsVelocity>, RefRO<BulletData>>()
                     .WithAll<Bullet>())
        {
            if (!bulletData.ValueRO.active)
                continue;

            var pv = velocityRw.ValueRO;
            velocityRw.ValueRW = new PhysicsVelocity
            {
                Linear = bulletData.ValueRO.linearVelocityTerrainRelative - terrainVelocity,
                Angular = pv.Angular
            };
        }
    }
}
