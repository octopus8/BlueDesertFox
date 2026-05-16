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
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Bullet>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float3 terrainVelocity = float3.zero;
        if (SystemAPI.TryGetSingleton(out TerrainScrollVelocity sv))
            terrainVelocity = sv.direction * sv.speed;

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
