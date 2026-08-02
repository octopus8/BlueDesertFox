using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Estimates the player's world horizontal velocity from the tracked transform for ballistic aiming.
/// Runs before <see cref="TurretAimingSystem"/>.
/// </summary>
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(TurretAimingSystem))]
public partial struct PlayerTargetVelocityEstimateSystem : ISystem
{
    /// <summary>Registers the <see cref="PlayerTransformReference"/> requirement.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<CameraDataSingleton>();
    }

    /// <summary>
    /// Computes a finite-difference XZ velocity estimate from the cached player position and applies
    /// 0.45 lerp smoothing to reduce VR tracking noise. Writes the result to <see cref="PlayerTargetVelocity"/>.
    /// Reads <see cref="CameraDataSingleton"/> written at end of the previous frame.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var paused) && paused.Value)
            return;

        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
        float3 pos = cameraData.position;

        float dt = SystemAPI.Time.DeltaTime;
        if (dt < 1e-8f)
            return;

        if (!SystemAPI.HasSingleton<PlayerTargetVelocity>())
            return;

        var kinRW = SystemAPI.GetSingletonRW<PlayerTargetVelocity>();
        ref PlayerTargetVelocity k = ref kinRW.ValueRW;

        if (!k.hasPrevious)
        {
            k.lastWorldPosition = pos;
            k.hasPrevious = true;
            k.horizontal = float3.zero;
            return;
        }

        float3 raw = (pos - k.lastWorldPosition) / dt;
        raw = new float3(raw.x, 0f, raw.z);

        k.lastWorldPosition = pos;
        k.horizontal = math.lerp(k.horizontal, raw, 0.45f);
    }
}
