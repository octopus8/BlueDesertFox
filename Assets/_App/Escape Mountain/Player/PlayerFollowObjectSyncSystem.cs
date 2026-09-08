using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Main-thread pose cache for the Player Follow Object entity in a baked subscene.
/// Updated each frame by <see cref="PlayerFollowObjectSyncSystem"/>.
/// </summary>
/// <remarks>
/// <see cref="Position"/> is the sliding sphere (what the XR rig follows).
/// <see cref="BoardContactPosition"/> is the Terrain contact under the sphere.
/// <see cref="SphereCenter"/> and <see cref="SphereRadius"/> reconstruct visual contact from a smoothed normal.
/// </remarks>
public static class PlayerFollowObjectPoseBridge
{
    /// <summary>World position of the sliding sphere.</summary>
    public static Vector3 Position { get; private set; }

    public static Quaternion Rotation { get; private set; }

    /// <summary>World position of the supporting surface under the sphere. Only valid when <see cref="HasBoardContact"/>.</summary>
    public static Vector3 BoardContactPosition { get; private set; }

    public static bool HasBoardContact { get; private set; }

    public static Vector3 TerrainNormal { get; private set; } = Vector3.up;

    public static bool HasTiltTerrainNormal { get; private set; }

    /// <summary>False while the sphere is in ballistic flight.</summary>
    public static bool IsInContact { get; private set; }

    /// <summary>Local offset of the follow sphere/capsule center from <see cref="Position"/>.</summary>
    public static Vector3 SphereCenter { get; private set; }

    /// <summary>Radius used to reconstruct visual contact from a smoothed terrain normal.</summary>
    public static float SphereRadius { get; private set; }

    public static bool IsValid { get; private set; }

    internal static void SetPose(
        float3 position,
        quaternion rotation,
        float3 contactPoint,
        float3 groundNormal,
        float3 sphereCenter,
        float sphereRadius,
        bool inContact)
    {
        Position = (Vector3)position;
        Rotation = (Quaternion)rotation;
        BoardContactPosition = (Vector3)contactPoint;
        HasBoardContact = inContact;
        TerrainNormal = (Vector3)math.normalizesafe(groundNormal, math.up());
        HasTiltTerrainNormal = inContact;
        IsInContact = inContact;
        SphereCenter = (Vector3)sphereCenter;
        SphereRadius = sphereRadius;
        IsValid = true;
    }

    internal static void Clear()
    {
        IsValid = false;
        HasBoardContact = false;
        HasTiltTerrainNormal = false;
        IsInContact = false;
        SphereCenter = Vector3.zero;
        SphereRadius = 0f;
    }
}

/// <summary>
/// Copies the world pose of the Player Follow Object entity into <see cref="PlayerFollowObjectPoseBridge"/>
/// each frame so main-scene MonoBehaviours can follow subscene entities.
/// Remains managed (non-Burst) because it writes to a static GameObject bridge.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerFollowObjectGroundContactSystem))]
[UpdateBefore(typeof(TileScrollPositionSystem))]
public partial struct PlayerFollowObjectSyncSystem : ISystem
{
    private bool _loggedMultipleWarning;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerFollowObjectTag>();
    }

    public void OnDestroy(ref SystemState state)
    {
        // Static bridge survives world teardown; clear so a scene reload cannot
        // briefly follow the previous session's pose.
        PlayerFollowObjectPoseBridge.Clear();
    }

    public void OnUpdate(ref SystemState state)
    {
        bool found = false;
        int count = 0;

        foreach (var (localTransform, motionState, config) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<PlayerFollowObjectMotionState>, RefRO<PlayerFollowObjectGroundConfig>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            count++;
            if (found)
                continue;

            found = true;

            bool inContact = motionState.ValueRO.inContact != 0;

            PlayerFollowObjectPoseBridge.SetPose(
                localTransform.ValueRO.Position,
                localTransform.ValueRO.Rotation,
                motionState.ValueRO.contactPoint,
                motionState.ValueRO.previousGroundNormal,
                config.ValueRO.sphereCenter,
                config.ValueRO.sphereRadius,
                inContact);
        }

        if (!found)
        {
            PlayerFollowObjectPoseBridge.Clear();
            return;
        }

        if (count > 1 && !_loggedMultipleWarning)
        {
            Debug.LogWarning($"[PlayerFollowObjectSyncSystem] Found {count} entities with {nameof(PlayerFollowObjectTag)}. Using the first one.");
            _loggedMultipleWarning = true;
        }
    }
}
