using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Snaps <see cref="TrailPathConfig"/> start XZ to the player once at startup so the shared
/// straight run is centered under the player (content space = world + scroll offset).
/// Prefers the <see cref="PlayerFollowObjectTag"/> entity when present; otherwise uses
/// <see cref="PlayerTransformReference"/>.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(PlayerTrackingInitSystem))]
public partial class TrailPathPlayerAlignSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<TrailPathConfig>();
    }

    protected override void OnUpdate()
    {
        var path = SystemAPI.GetSingletonRW<TrailPathConfig>();
        if (path.ValueRO.snapStartToPlayer == 0 || path.ValueRO.startAligned != 0)
            return;

        float3 worldPos = float3.zero;
        bool found = false;

        foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<PlayerFollowObjectTag>())
        {
            worldPos = transform.ValueRO.Position;
            found = true;
            break;
        }

        if (!found)
        {
            if (!SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) ||
                playerRef.playerTransform == null)
                return;

            worldPos = playerRef.playerTransform.position;
            found = true;
        }

        if (!found)
            return;

        float3 scroll = float3.zero;
        if (SystemAPI.HasSingleton<ScrollOffset>())
            scroll = SystemAPI.GetSingleton<ScrollOffset>().accumulatedOffset;

        // Trail centerlines are authored in content/grid space (pre-scroll).
        float3 contentPos = worldPos + scroll;

        float prevX = path.ValueRO.startX;
        float prevZ = path.ValueRO.startZ;

        path.ValueRW.startX = contentPos.x;
        path.ValueRW.startZ = contentPos.z;
        if (path.ValueRW.straightLength <= 0f)
            path.ValueRW.straightLength = 80f;
        path.ValueRW.startAligned = 1;

        // If tiles already carved with the old origin, force remesh.
        float2 delta = new float2(contentPos.x - prevX, contentPos.z - prevZ);
        if (math.lengthsq(delta) > 0.01f)
        {
            foreach (var tile in SystemAPI.Query<RefRW<TerrainTile>>())
            {
                if (tile.ValueRO.meshGenerated)
                    tile.ValueRW.needsRegeneration = true;
            }
        }

#if UNITY_EDITOR
        Debug.Log(
            $"[TrailPath] Aligned start to content XZ=({contentPos.x:F1}, {contentPos.z:F1}), " +
            $"straightLength={path.ValueRO.straightLength:F0}");
#endif
    }
}
