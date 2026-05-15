using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Mirrors visibility logic from <see cref="GlobalStaticObjectInstanceSystem"/> (spatial grid, distance,
/// camera frustum) for static-object entities still rendered via Entities.Graphics—typically flattened
/// linked prefab children that retain <see cref="MaterialMeshInfo"/>.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
public partial class StaticObjectLinkedGraphicsCullSystem : SystemBase
{
    private const float DefaultMaxRenderDistance = 400f;
    private const float GridCellSize = 150f;

    private EntityQuery _graphicsPartsQuery;

    protected override void OnCreate()
    {
        RequireForUpdate<StaticObjectLODConfig>();

        _graphicsPartsQuery = GetEntityQuery(
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<MaterialMeshInfo>(),
            ComponentType.ReadOnly<StaticObjectTileOwnership>(),
            ComponentType.Exclude<GlobalStaticObjectInstance>());
    }

    protected override void OnUpdate()
    {
        if (_graphicsPartsQuery.IsEmptyIgnoreFilter)
            return;

        var lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();
        bool enableDistanceCulling = lodConfig.enableDistanceCulling;
        float maxRenderDistance = lodConfig.maxObjectRenderDistance > 0
            ? lodConfig.maxObjectRenderDistance
            : DefaultMaxRenderDistance;

        float3 playerPosition = float3.zero;
        bool hasPlayerPosition = false;
        if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
            playerRef != null && playerRef.playerTransform != null)
        {
            playerPosition = playerRef.playerTransform.position;
            hasPlayerPosition = true;
        }

        bool enableSpatialCulling = enableDistanceCulling && hasPlayerPosition;
        var visibleCells = new NativeHashSet<int2>(256, Allocator.Temp);
        var cam = Camera.main;
        if (enableSpatialCulling && cam != null)
            PopulateVisibleGridCells(cam, maxRenderDistance, visibleCells);

        var planes = new NativeArray<float4>(6, Allocator.Temp);
        bool enableFrustum = cam != null;
        if (enableFrustum)
        {
            var fp = GeometryUtility.CalculateFrustumPlanes(cam);
            for (var i = 0; i < 6; i++)
            {
                var plane = fp[i];
                planes[i] = new float4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);
            }
        }

        var toAddDisable = new NativeList<Entity>(Allocator.Temp);
        var toRemoveDisable = new NativeList<Entity>(Allocator.Temp);
        try
        {
            foreach (var (transformRef, entity) in SystemAPI.Query<RefRO<LocalTransform>>()
                         .WithAll<MaterialMeshInfo, StaticObjectTileOwnership>()
                         .WithNone<GlobalStaticObjectInstance>()
                         .WithEntityAccess())
            {
                float3 objectPos = transformRef.ValueRO.Position;
                float radius = transformRef.ValueRO.Scale * 10f;
                if (EntityManager.HasComponent<WorldRenderBounds>(entity))
                {
                    var aabb = EntityManager.GetComponentData<WorldRenderBounds>(entity).Value;
                    objectPos = aabb.Center;
                    radius = math.length(aabb.Extents);
                }

                bool culled = false;

                if (enableSpatialCulling && cam != null)
                {
                    var gridCell = new int2(
                        (int)math.floor(objectPos.x / GridCellSize),
                        (int)math.floor(objectPos.z / GridCellSize));
                    if (!visibleCells.Contains(gridCell))
                        culled = true;
                }

                if (!culled && enableDistanceCulling && hasPlayerPosition)
                {
                    var objectPos2D = new float2(objectPos.x, objectPos.z);
                    var playerPos2D = new float2(playerPosition.x, playerPosition.z);
                    float distanceSq = math.distancesq(objectPos2D, playerPos2D);
                    if (distanceSq > maxRenderDistance * maxRenderDistance)
                        culled = true;
                }

                if (!culled && enableFrustum && planes.Length == 6)
                {
                    for (var i = 0; i < 6; i++)
                    {
                        float4 plane = planes[i];
                        float dist = math.dot(plane.xyz, objectPos) + plane.w;
                        if (dist < -radius)
                        {
                            culled = true;
                            break;
                        }
                    }
                }

                if (culled)
                {
                    if (!EntityManager.HasComponent<DisableRendering>(entity))
                        toAddDisable.Add(entity);
                }
                else if (EntityManager.HasComponent<DisableRendering>(entity))
                {
                    toRemoveDisable.Add(entity);
                }
            }

            for (var i = 0; i < toAddDisable.Length; i++)
                EntityManager.AddComponent<DisableRendering>(toAddDisable[i]);

            for (var i = 0; i < toRemoveDisable.Length; i++)
                EntityManager.RemoveComponent<DisableRendering>(toRemoveDisable[i]);
        }
        finally
        {
            toAddDisable.Dispose();
            toRemoveDisable.Dispose();
            visibleCells.Dispose();
            planes.Dispose();
        }
    }

    private static void PopulateVisibleGridCells(Camera camera, float maxDistance, NativeHashSet<int2> visibleCells)
    {
        visibleCells.Clear();
        if (camera == null)
            return;

        float3 camPos = camera.transform.position;
        int cellRadius = (int)math.ceil(maxDistance / GridCellSize);
        var camGridPos = new int2(
            (int)math.floor(camPos.x / GridCellSize),
            (int)math.floor(camPos.z / GridCellSize));

        float maxDistSq = maxDistance * maxDistance;
        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int z = -cellRadius; z <= cellRadius; z++)
            {
                var cellCoord = camGridPos + new int2(x, z);
                var cellCenter = (float2)cellCoord * GridCellSize + GridCellSize * 0.5f;
                var camPos2D = new float2(camPos.x, camPos.z);

                if (math.distancesq(cellCenter, camPos2D) <= maxDistSq)
                    visibleCells.Add(cellCoord);
            }
        }
    }
}
