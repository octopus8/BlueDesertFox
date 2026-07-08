using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Rotates the Player Follow Object's terrain-relative horizontal velocity around Y based on HMD head roll.
/// Runs before ground-contact physics so steering affects the velocity used for integration.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(PlayerFollowObjectGroundContactSystem))]
public partial class PlayerFollowObjectHeadSteeringSystem : SystemBase
{
    private const float MinSteeringSpeed = 0.01f;

    protected override void OnCreate()
    {
        RequireForUpdate<PlayerFollowObjectTag>();
        RequireForUpdate<PlayerFollowObjectSteeringConfig>();
        RequireForUpdate<PlayerFollowObjectMotionState>();
        RequireForUpdate<CameraDataSingleton>();
        RequireForUpdate<TerrainScrollVelocity>();
    }

    protected override void OnUpdate()
    {
        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
        float3 scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>().WorldVelocity;
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (steeringConfig, motionState) in SystemAPI
                     .Query<RefRO<PlayerFollowObjectSteeringConfig>, RefRW<PlayerFollowObjectMotionState>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
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
