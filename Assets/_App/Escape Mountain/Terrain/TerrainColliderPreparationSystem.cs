using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Reads the player's Transform once at the start of SimulationSystemGroup and writes
/// pose data into CameraDataSingleton for use by terrain systems without managed references.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
public partial class CameraDataUpdateSystem : SystemBase
{
    protected override void OnCreate()
    {
        if (!SystemAPI.HasSingleton<CameraDataSingleton>())
            EntityManager.CreateEntity(typeof(CameraDataSingleton));

        RequireForUpdate<PlayerTransformReference>();
    }

    /// <summary>
    /// Resets cached pose when player tracking leaves the world (SubScene unload / scene reload)
    /// so the next load does not briefly use a stale Quest/head pose for tile centering.
    /// </summary>
    protected override void OnStopRunning()
    {
        if (SystemAPI.HasSingleton<CameraDataSingleton>())
        {
            SystemAPI.SetSingleton(new CameraDataSingleton
            {
                position = float3.zero,
                forward = new float3(0, 0, 1),
                fullForward = new float3(0, 0, 1),
                bankAngle = 0f,
                headBankAngle = 0f
            });
        }
    }

    protected override void OnUpdate()
    {
        float3 position = float3.zero;
        float3 forward = new float3(0, 0, 1);
        float3 fullForward = new float3(0, 0, 1);
        float bankAngle = 0f;
        float headBankAngle = 0f;

        if (Camera.main != null)
        {
            float rawHeadZ = Camera.main.transform.eulerAngles.z;
            headBankAngle = rawHeadZ > 180f ? rawHeadZ - 360f : rawHeadZ;
        }

        if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
            playerRef != null &&
            playerRef.playerTransform != null)
        {
            var tf = playerRef.playerTransform;
            position = tf.position;

            var fwd = tf.forward;
            fullForward = math.normalize(new float3(fwd.x, fwd.y, fwd.z));
            forward = math.normalizesafe(new float3(fwd.x, 0f, fwd.z), new float3(0, 0, 1));

            float rawZ = tf.eulerAngles.z;
            bankAngle = rawZ > 180f ? rawZ - 360f : rawZ;
        }

        SystemAPI.SetSingleton(new CameraDataSingleton
        {
            position = position,
            forward = forward,
            fullForward = fullForward,
            bankAngle = bankAngle,
            headBankAngle = headBankAngle
        });
    }
}

/// <summary>
/// Singleton ECS component that caches the player's world-space pose each frame.
/// </summary>
public struct CameraDataSingleton : IComponentData
{
    public float3 position;
    public float3 forward;
    public float3 fullForward;
    public float bankAngle;
    /// <summary>Head roll angle (world Euler Z) from the HMD camera, normalized to -180..180.</summary>
    public float headBankAngle;
}
