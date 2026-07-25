using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Rotates the Player Follow Object's terrain-relative horizontal velocity around Y based on HMD head roll.
/// Runs before ground-contact physics so steering affects the velocity used for integration.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(PlayerFollowObjectGroundContactSystem))]
public partial struct PlayerFollowObjectHeadSteeringSystem : ISystem
{
    private const float MinSteeringSpeed = 0.01f;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerFollowObjectTag>();
        state.RequireForUpdate<PlayerFollowObjectSteeringConfig>();
        state.RequireForUpdate<PlayerFollowObjectMotionState>();
        state.RequireForUpdate<CameraDataSingleton>();
        state.RequireForUpdate<TerrainScrollVelocity>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
        float3 scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>().WorldVelocity;
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (steeringConfig, motionState) in SystemAPI
                     .Query<RefRO<PlayerFollowObjectSteeringConfig>, RefRW<PlayerFollowObjectMotionState>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            if (motionState.ValueRO.inContact == 0)
                continue;

            float sensitivity = steeringConfig.ValueRO.steeringSensitivity;
            if (sensitivity <= 0f)
                continue;

            float3 terrainRelativeVelocity = motionState.ValueRO.terrainRelativeVelocity;
            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);
            float3 flat = new float3(worldVelocity.x, 0f, worldVelocity.z);
            if (math.lengthsq(flat) < MinSteeringSpeed * MinSteeringSpeed)
                continue;

            float bankRadians = math.radians(cameraData.headBankAngle);
            float rotationAmount = -math.sin(bankRadians) * sensitivity * dt;
            quaternion yawRotation = quaternion.RotateY(math.radians(rotationAmount));
            float3 rotatedFlat = math.rotate(yawRotation, flat);

            worldVelocity = new float3(rotatedFlat.x, worldVelocity.y, rotatedFlat.z);
            motionState.ValueRW.terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                worldVelocity,
                scrollVelocity);
        }
    }
}
