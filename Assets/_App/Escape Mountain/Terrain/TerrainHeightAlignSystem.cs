using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// One-shot vertical align: samples unaligned terrain height at the player's start XZ and
/// stores <see cref="TerrainTileConfig.heightOffset"/> so the surface sits under their feet.
/// Prefers the Player Follow Object (capsule feet via <see cref="PlayerFollowObjectGroundConfig.bottomOffset"/>,
/// plus the suspension's neutral <see cref="PlayerFollowObjectGroundConfig.rideHeight"/>);
/// falls back to the tracked player Transform when no follow object exists.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(PlayerTrackingInitSystem))]
public partial struct TerrainHeightAlignSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<TerrainHeightAlignState>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var alignState = SystemAPI.GetSingleton<TerrainHeightAlignState>();
        if (alignState.aligned != 0)
            return;

        float3 anchorPosition = float3.zero;
        float bottomOffset = 0f;
        float rideHeight = 0f;
        bool hasAnchor = false;

        foreach (var (localTransform, groundConfig) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<PlayerFollowObjectGroundConfig>>()
                     .WithAll<PlayerFollowObjectTag>())
        {
            anchorPosition = localTransform.ValueRO.Position;
            bottomOffset = groundConfig.ValueRO.bottomOffset;
            rideHeight = groundConfig.ValueRO.rideHeight;
            hasAnchor = true;
            break;
        }

        if (!hasAnchor)
        {
            if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
                playerRef != null &&
                playerRef.playerTransform != null)
            {
                Vector3 pos = playerRef.playerTransform.position;
                anchorPosition = new float3(pos.x, pos.y, pos.z);
                bottomOffset = 0f;
                rideHeight = 0f;
                hasAnchor = true;
            }
        }

        if (!hasAnchor)
            return;

        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        bool hasTrailConfig = SystemAPI.HasSingleton<TrailConfig>();
        TrailConfig trailConfig = hasTrailConfig
            ? SystemAPI.GetSingleton<TrailConfig>()
            : default;
        TrailPathConfig trailPath = SystemAPI.HasSingleton<TrailPathConfig>()
            ? SystemAPI.GetSingleton<TrailPathConfig>()
            : new TrailPathConfig { straightLength = 80f, weaveFadeLength = 30f, snapStartToPlayer = 1 };
        TrailImagePaths trailImagePaths = SystemAPI.HasSingleton<TrailImagePaths>()
            ? SystemAPI.GetSingleton<TrailImagePaths>()
            : default;

        float unalignedHeight = TerrainMeshNoise.SampleUnalignedHeightAt(
            anchorPosition.x,
            anchorPosition.z,
            config,
            hasTrailConfig,
            trailConfig,
            trailPath,
            trailImagePaths);

        // The board hangs a full leg below the sprung body, so drop the surface by the neutral ride
        // height too. Without this the suspension would start fully compressed and push the rider up
        // by rideHeight on the first frames.
        float feetY = anchorPosition.y - bottomOffset - rideHeight;
        config.heightOffset = feetY - unalignedHeight + config.initYOffset;
        SystemAPI.SetSingleton(config);

        alignState.aligned = 1;
        SystemAPI.SetSingleton(alignState);
    }
}
