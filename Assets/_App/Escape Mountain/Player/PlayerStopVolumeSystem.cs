using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Engages <see cref="PlayerFollowObjectBrakeState"/> when the player enters a
/// <see cref="PlayerStopVolume"/> OBB. One-shot: once active, further volumes are ignored.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(PlayerFollowObjectHeadSteeringSystem))]
public partial struct PlayerStopVolumeSystem : ISystem
{
    private const float InsideEpsilon = 1e-4f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerStopVolume>();
        state.RequireForUpdate<PlayerFollowObjectTag>();
        state.RequireForUpdate<PlayerFollowObjectBrakeState>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var paused) && paused.Value)
            return;

        Entity playerEntity = SystemAPI.GetSingletonEntity<PlayerFollowObjectTag>();
        RefRW<PlayerFollowObjectBrakeState> brakeState =
            SystemAPI.GetComponentRW<PlayerFollowObjectBrakeState>(playerEntity);
        if (brakeState.ValueRO.active != 0)
            return;

        float3 playerPos = SystemAPI.GetComponent<LocalTransform>(playerEntity).Position;

        foreach (var (volume, localToWorld) in SystemAPI
                     .Query<RefRO<PlayerStopVolume>, RefRO<LocalToWorld>>())
        {
            if (!ContainsPoint(localToWorld.ValueRO.Value, volume.ValueRO, playerPos))
                continue;

            brakeState.ValueRW.active = 1;
            brakeState.ValueRW.deceleration = volume.ValueRO.deceleration;
            brakeState.ValueRW.holdAfterStop = volume.ValueRO.holdAfterStop;
            break;
        }
    }

    private static bool ContainsPoint(float4x4 localToWorld, in PlayerStopVolume volume, float3 worldPoint)
    {
        float4x4 worldToLocal = math.inverse(localToWorld);
        float3 local = math.mul(worldToLocal, new float4(worldPoint, 1f)).xyz - volume.localCenter;
        float3 extents = volume.halfExtents + InsideEpsilon;
        return math.all(math.abs(local) <= extents);
    }
}
