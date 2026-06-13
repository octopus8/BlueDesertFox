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
    }

    /// <summary>
    /// Computes a finite-difference XZ velocity estimate from the player's current and previous world positions
    /// and applies 0.45 lerp smoothing to reduce VR tracking noise. Writes the result to <see cref="PlayerTargetVelocity"/>.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        if (playerRef?.playerTransform == null)
            return;

        float dt = SystemAPI.Time.DeltaTime;
        if (dt < 1e-8f)
            return;

        float3 pos = playerRef.playerTransform.position;
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
        // Light smoothing to reduce VR tracking noise without lagging too badly.
        k.horizontal = math.lerp(k.horizontal, raw, 0.45f);
    }
}
