using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Rotates the Player Follow Object's surface-tangent velocity around the contact normal based on HMD head roll,
/// so steering keeps full strength on steep faces (e.g. quarterpipe walls) where most speed is vertical.
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
        state.RequireForUpdate<PlayerFollowObjectBrakeState>();
        state.RequireForUpdate<CameraDataSingleton>();
        state.RequireForUpdate<TerrainScrollVelocity>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var paused) && paused.Value)
            return;

        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
        float3 scrollVelocity = SystemAPI.GetSingleton<TerrainScrollVelocity>().WorldVelocity;
        float dt = SystemAPI.Time.DeltaTime;

        foreach (var (steeringConfig, motionState, brakeState) in SystemAPI
                     .Query<RefRO<PlayerFollowObjectSteeringConfig>, RefRW<PlayerFollowObjectMotionState>,
                         RefRO<PlayerFollowObjectBrakeState>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            if (brakeState.ValueRO.active != 0)
                continue;

            if (motionState.ValueRO.inContact == 0)
                continue;

            float sensitivity = steeringConfig.ValueRO.steeringSensitivity;
            if (sensitivity <= 0f)
                continue;

            float3 terrainRelativeVelocity = motionState.ValueRO.terrainRelativeVelocity;
            float3 worldVelocity = TerrainScrollVelocityMath.WorldVelocityFromTerrainRelative(
                terrainRelativeVelocity,
                scrollVelocity);
            float3 surfaceNormal = math.normalizesafe(motionState.ValueRO.previousGroundNormal, math.up());
            float normalSpeed = math.dot(worldVelocity, surfaceNormal);
            float3 tangent = worldVelocity - surfaceNormal * normalSpeed;
            if (math.lengthsq(tangent) < MinSteeringSpeed * MinSteeringSpeed)
                continue;

            float bankRadians = math.radians(cameraData.headBankAngle);
            float sinBank = math.sin(bankRadians);
            float rotationAmount = -sinBank * sensitivity * dt;
            quaternion turnRotation = quaternion.AxisAngle(surfaceNormal, math.radians(rotationAmount));
            float3 rotatedTangent = math.rotate(turnRotation, tangent);

            float turnDrag = steeringConfig.ValueRO.turnDrag;
            if (turnDrag > 0f)
            {
                float tiltFactor = math.abs(sinBank);
                float speedScale = math.max(0f, 1f - turnDrag * tiltFactor);
                rotatedTangent *= speedScale;
            }

            worldVelocity = rotatedTangent + surfaceNormal * normalSpeed;
            motionState.ValueRW.terrainRelativeVelocity = TerrainScrollVelocityMath.TerrainRelativeFromWorld(
                worldVelocity,
                scrollVelocity);
        }
    }
}
