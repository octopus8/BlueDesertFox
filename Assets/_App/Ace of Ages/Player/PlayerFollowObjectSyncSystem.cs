using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Main-thread pose cache for the Player Follow Object entity in a baked subscene.
/// Updated each frame by <see cref="PlayerFollowObjectSyncSystem"/>.
/// </summary>
/// <remarks>
/// Two positions are published because the rider and the board are separated by the suspension.
/// <see cref="Position"/> is the sprung rider body (what the XR rig follows) and
/// <see cref="BoardContactPosition"/> is the surface the board rests on. The gap between them is the
/// leg travel that absorbs bumps.
/// </remarks>
public static class PlayerFollowObjectPoseBridge
{
    /// <summary>World position of the sprung rider body.</summary>
    public static Vector3 Position { get; private set; }

    public static Quaternion Rotation { get; private set; }

    /// <summary>World position of the supporting surface under the board. Only valid when <see cref="HasBoardContact"/>.</summary>
    public static Vector3 BoardContactPosition { get; private set; }

    public static bool HasBoardContact { get; private set; }

    public static Vector3 TerrainNormal { get; private set; } = Vector3.up;

    public static bool HasTiltTerrainNormal { get; private set; }

    /// <summary>False while the leg is out of reach and the rider is in ballistic flight.</summary>
    public static bool IsInContact { get; private set; }

    /// <summary>Suspension squash, 0 at or above neutral ride height, 1 fully bottomed out.</summary>
    public static float LegCompression01 { get; private set; }

    public static bool IsValid { get; private set; }

    internal static void SetPose(
        float3 position,
        quaternion rotation,
        float3 contactPoint,
        float3 groundNormal,
        bool inContact,
        float legCompression01)
    {
        Position = (Vector3)position;
        Rotation = (Quaternion)rotation;
        BoardContactPosition = (Vector3)contactPoint;
        HasBoardContact = inContact;
        TerrainNormal = (Vector3)math.normalizesafe(groundNormal, math.up());
        HasTiltTerrainNormal = inContact;
        IsInContact = inContact;
        LegCompression01 = legCompression01;
        IsValid = true;
    }

    internal static void Clear()
    {
        IsValid = false;
        HasBoardContact = false;
        HasTiltTerrainNormal = false;
        IsInContact = false;
        LegCompression01 = 0f;
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

    public void OnUpdate(ref SystemState state)
    {
        bool found = false;
        int count = 0;

        foreach (var (localTransform, motionState, groundConfig) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<PlayerFollowObjectMotionState>, RefRO<PlayerFollowObjectGroundConfig>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            count++;
            if (found)
                continue;

            found = true;

            bool inContact = motionState.ValueRO.inContact != 0;

            float maxCompression = groundConfig.ValueRO.maxLegCompression;
            float compression01 = maxCompression > 0f
                ? math.saturate((groundConfig.ValueRO.rideHeight - motionState.ValueRO.legLength) / maxCompression)
                : 0f;

            // The ground-contact system already fitted a plane across the contact footprint, which is
            // far steadier than re-raycasting a single per-triangle normal here.
            PlayerFollowObjectPoseBridge.SetPose(
                localTransform.ValueRO.Position,
                localTransform.ValueRO.Rotation,
                motionState.ValueRO.contactPoint,
                motionState.ValueRO.previousGroundNormal,
                inContact,
                compression01);
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
