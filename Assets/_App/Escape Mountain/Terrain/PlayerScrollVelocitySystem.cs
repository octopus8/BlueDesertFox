using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Drives terrain scroll (horizontal and vertical) from a single speed value distributed by pitch angle.
/// At pitch = 0 (level): terrain scrolls at full speed, vertical = 0.
/// At pitch = 90 (nose-up): horizontal scroll = 0, terrain scrolls down at full speed.
/// Also rotates the world origin based on player roll (bank-to-turn steering).
/// Only runs when PlayerTerrainScrollVelocityConfig exists in the scene.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(TransformFollowerSystemOptimized))]
public partial class PlayerScrollVelocitySystem : SystemBase
{
    /// <summary>Registers required singletons: scroll velocity, player config, player transform reference, world origin, and pose cache.</summary>
    protected override void OnCreate()
    {
        RequireForUpdate<TerrainScrollVelocity>();
        RequireForUpdate<PlayerTerrainScrollVelocityConfig>();
        RequireForUpdate<PlayerTransformReference>();
        RequireForUpdate<WorldOriginTransformReference>();
        RequireForUpdate<CameraDataSingleton>();
    }

    /// <summary>
    /// Reads the player ship's pitch and bank angles from <see cref="CameraDataSingleton"/> (written at
    /// end of previous frame), distributes the configured <c>speed</c> between horizontal and vertical
    /// terrain scroll, writes the resulting <see cref="TerrainScrollVelocity"/>, and optionally
    /// rotates the world-origin Transform based on bank angle.
    /// Writing to the world-origin managed Transform must remain on the main thread.
    /// </summary>
    protected override void OnUpdate()
    {
        RefRW<TerrainScrollVelocity> scrollVelocity = SystemAPI.GetSingletonRW<TerrainScrollVelocity>();

        if (SystemAPI.TryGetSingleton<GamePaused>(out var paused) && paused.Value)
        {
            scrollVelocity.ValueRW.speed = 0f;
            scrollVelocity.ValueRW.verticalSpeed = 0f;
            return;
        }

        var config = SystemAPI.GetSingleton<PlayerTerrainScrollVelocityConfig>();
        var worldOriginRef = SystemAPI.ManagedAPI.GetSingleton<WorldOriginTransformReference>();
        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();

        // Use cached full 3D forward from singleton — no managed Transform access needed.
        float3 fullFwd = cameraData.fullForward;
        float3 baseScrollDirection = new float3(fullFwd.x, 0, fullFwd.z);

        if (math.lengthsq(baseScrollDirection) > 0.0001f)
            baseScrollDirection = math.normalize(baseScrollDirection);
        else
            baseScrollDirection = new float3(0, 0, 1);

        // Decompose speed into horizontal scroll and vertical components using pitch angle.
        // fullFwd.y == sin(pitch), so cos(pitch) gives the horizontal factor.
        // At pitch = 0 (level):    scroll = speed, vertical = 0
        // At pitch = 90 (nose-up): scroll = 0,     vertical = speed
        float sinPitch = fullFwd.y;
        float cosPitch = math.sqrt(1f - sinPitch * sinPitch);

        scrollVelocity.ValueRW.direction = baseScrollDirection;
        scrollVelocity.ValueRW.speed = config.speed * cosPitch;
        scrollVelocity.ValueRW.verticalSpeed = config.speed * sinPitch;

        // Rotate world origin based on cached bank angle (already normalised to –180..180 by CameraDataUpdateSystem).
        if (worldOriginRef?.worldOriginTransform != null)
        {
            float bankRadians = math.radians(cameraData.bankAngle);
            float rotationSpeed = -math.sin(bankRadians);

            float rotationAmount = rotationSpeed * config.rotationSpeed * SystemAPI.Time.DeltaTime;
            worldOriginRef.worldOriginTransform.rotation *= UnityEngine.Quaternion.Euler(0, rotationAmount, 0);
        }
    }
}
