using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using RaycastHit = Unity.Physics.RaycastHit;

/// <summary>
/// Main-thread pose cache for the Player Follow Object entity in a baked subscene.
/// Updated each frame by <see cref="PlayerFollowObjectSyncSystem"/>.
/// </summary>
public static class PlayerFollowObjectPoseBridge
{
    public static Vector3 Position { get; private set; }
    public static Quaternion Rotation { get; private set; }
    public static Vector3 TerrainNormal { get; private set; } = Vector3.up;
    public static bool HasTiltTerrainNormal { get; private set; }
    public static float AirborneTimeRemaining { get; private set; }
    public static bool IsValid { get; private set; }

    internal static void SetPose(
        float3 position,
        quaternion rotation,
        float3 terrainNormal,
        float3 tiltTerrainNormal,
        bool hasTiltTerrainNormal,
        float airborneTimeRemaining)
    {
        Position = (Vector3)position;
        Rotation = (Quaternion)rotation;
        TerrainNormal = (Vector3)math.normalizesafe(terrainNormal, math.up());
        HasTiltTerrainNormal = hasTiltTerrainNormal;
        AirborneTimeRemaining = airborneTimeRemaining;

        if (hasTiltTerrainNormal)
            TerrainNormal = (Vector3)math.normalizesafe(tiltTerrainNormal, math.up());

        IsValid = true;
    }

    internal static void Clear()
    {
        IsValid = false;
        HasTiltTerrainNormal = false;
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
    private const float MinWalkableNormalY = 0.01f;
    private const float MaxTiltDistance = 8f;

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

            bool hasTiltNormal = TryGetTiltTerrainNormal(
                ref state,
                localTransform.ValueRO.Position,
                groundConfig.ValueRO,
                out float3 tiltNormal);

            PlayerFollowObjectPoseBridge.SetPose(
                localTransform.ValueRO.Position,
                localTransform.ValueRO.Rotation,
                motionState.ValueRO.previousGroundNormal,
                tiltNormal,
                hasTiltNormal,
                motionState.ValueRO.airborneTimeRemaining);
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

    private bool TryGetTiltTerrainNormal(
        ref SystemState state,
        float3 position,
        in PlayerFollowObjectGroundConfig groundConfig,
        out float3 terrainNormal)
    {
        terrainNormal = math.up();

        if (!SystemAPI.TryGetSingleton(out TerrainTileConfig terrainConfig)
            || !terrainConfig.enablePhysicsColliders
            || !SystemAPI.HasSingleton<PhysicsWorldSingleton>())
        {
            return false;
        }

        state.Dependency.Complete();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld.CollisionWorld;

        int terrainLayer = terrainConfig.terrainPhysicsLayer;
        int rideableLayer = groundConfig.rideablePhysicsLayer;
        if (rideableLayer < 0 || rideableLayer > 30)
            rideableLayer = 15;

        uint groundLayerMask = (1u << terrainLayer) | (1u << rideableLayer);
        var groundFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = groundLayerMask,
            GroupIndex = 0
        };

        float3 rayStart = position + math.up() * groundConfig.rayHeightAbove;
        float3 rayEnd = position - math.up() * groundConfig.rayLengthBelow;
        var rayInput = new RaycastInput
        {
            Start = rayStart,
            End = rayEnd,
            Filter = groundFilter
        };

        if (!collisionWorld.CastRay(rayInput, out RaycastHit hit))
            return false;

        terrainNormal = math.normalizesafe(hit.SurfaceNormal, math.up());
        if (terrainNormal.y < MinWalkableNormalY)
            return false;

        float heightAboveSurface = math.dot(position - hit.Position, terrainNormal);
        return heightAboveSurface <= MaxTiltDistance;
    }
}
