using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Main-thread pose cache for the Player Follow Object entity in a baked subscene.
/// Updated each frame by <see cref="PlayerFollowObjectSyncSystem"/>.
/// </summary>
public static class PlayerFollowObjectPoseBridge
{
    public static Vector3 Position { get; private set; }
    public static Quaternion Rotation { get; private set; }
    public static bool IsValid { get; private set; }

    internal static void SetPose(float3 position, quaternion rotation)
    {
        Position = (Vector3)position;
        Rotation = (Quaternion)rotation;
        IsValid = true;
    }

    internal static void Clear()
    {
        IsValid = false;
    }
}

/// <summary>
/// Copies the world pose of the Player Follow Object entity into <see cref="PlayerFollowObjectPoseBridge"/>
/// each frame so main-scene MonoBehaviours can follow subscene entities.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerFollowObjectGroundContactSystem))]
[UpdateBefore(typeof(TileScrollPositionSystem))]
public partial class PlayerFollowObjectSyncSystem : SystemBase
{
    private EntityQuery _followObjectQuery;
    private bool _loggedMultipleWarning;

    protected override void OnCreate()
    {
        _followObjectQuery = GetEntityQuery(
            ComponentType.ReadOnly<PlayerFollowObjectTag>(),
            ComponentType.ReadOnly<LocalTransform>());

        RequireForUpdate(_followObjectQuery);
    }

    protected override void OnUpdate()
    {
        int count = _followObjectQuery.CalculateEntityCount();
        if (count == 0)
        {
            PlayerFollowObjectPoseBridge.Clear();
            return;
        }

        if (count > 1 && !_loggedMultipleWarning)
        {
            Debug.LogWarning($"[PlayerFollowObjectSyncSystem] Found {count} entities with {nameof(PlayerFollowObjectTag)}. Using the first one.");
            _loggedMultipleWarning = true;
        }

        using var entities = _followObjectQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        var localTransform = EntityManager.GetComponentData<LocalTransform>(entities[0]);
        PlayerFollowObjectPoseBridge.SetPose(localTransform.Position, localTransform.Rotation);
    }
}
