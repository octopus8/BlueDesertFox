using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

/// <summary>
/// One-shot-per-SubScene system that populates the
/// <see cref="StaticObjectLODMaterialMeshInfoElement"/> buffer on the config entity.
///
/// Each LOD prefab entity baked via Entities.Graphics already has a <see cref="MaterialMeshInfo"/>
/// component containing the pre-registered BRG mesh/material IDs. This system reads those IDs in
/// prefab order (objectTypeIndex * 3 + lodLevel) and stores them in a buffer so the spawning and
/// LOD-update systems can retrieve the correct <see cref="MaterialMeshInfo"/> for any LOD slot
/// without touching managed arrays.
///
/// Also populates per-LOD <see cref="RenderBounds"/> and per-type max bounds for frustum culling.
///
/// After the buffer is populated, a <see cref="StaticObjectLODMeshInfoReady"/> tag is added to the
/// config entity. Mesh infos are refreshed every frame from live prefabs so SubScene reload picks up
/// BRG-registered runtime IDs (Init in Initialization would snapshot bake-time array indices).
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
public partial class StaticObjectLODMeshInfoInitSystem : SystemBase
{
    private const int MaxEmptyBufferRetries = 120;
    private const int LodsPerObjectType = 3;
    private int _emptyBufferRetryCount;
    private bool _gaveUp;

    /// <summary>Registers <see cref="StaticObjectSpawnerConfig"/> and <see cref="StaticObjectPrefabElement"/> requirements.</summary>
    protected override void OnCreate()
    {
        RequireForUpdate<StaticObjectSpawnerConfig>();
        RequireForUpdate<StaticObjectPrefabElement>();
    }

    /// <summary>
    /// Re-arms init after SubScene unload so the next load can populate LOD buffers again.
    /// </summary>
    protected override void OnStopRunning()
    {
        _emptyBufferRetryCount = 0;
        _gaveUp = false;
    }

    /// <summary>
    /// Reads the baked <see cref="MaterialMeshInfo"/> from each LOD prefab entity,
    /// populates the <see cref="StaticObjectLODMaterialMeshInfoElement"/> buffer on the config entity,
    /// and adds the <see cref="StaticObjectLODMeshInfoReady"/> tag. Once Ready, refreshes mesh infos
    /// from prefabs each frame so BRG registration after SubScene reload stays current.
    /// </summary>
    protected override void OnUpdate()
    {
        if (_gaveUp)
            return;

        var configEntity = SystemAPI.GetSingletonEntity<StaticObjectSpawnerConfig>();

        if (EntityManager.HasComponent<StaticObjectLODMeshInfoReady>(configEntity))
        {
            RefreshMaterialMeshInfos(configEntity);
            return;
        }

        if (!EntityManager.HasBuffer<StaticObjectPrefabElement>(configEntity))
        {
            LogEmptyPrefabBufferFatal(configEntity, "StaticObjectPrefabElement buffer is missing on the config entity.");
            return;
        }

        if (!EntityManager.HasBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity) ||
            !EntityManager.HasBuffer<StaticObjectLODRenderBoundsElement>(configEntity) ||
            !EntityManager.HasBuffer<StaticObjectTypeMaxRenderBoundsElement>(configEntity))
        {
            LogEmptyPrefabBufferFatal(configEntity,
                "LOD lookup buffers are missing on the config entity. Re-bake the Entities SubScene.");
            return;
        }

        var prefabBuffer = EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity, isReadOnly: true);
        var infoBuffer = EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity);
        var boundsBuffer = EntityManager.GetBuffer<StaticObjectLODRenderBoundsElement>(configEntity);
        var maxBoundsBuffer = EntityManager.GetBuffer<StaticObjectTypeMaxRenderBoundsElement>(configEntity);

        if (prefabBuffer.Length == 0)
        {
            _emptyBufferRetryCount++;
            if (_emptyBufferRetryCount == 1 || _emptyBufferRetryCount == MaxEmptyBufferRetries)
            {
                Debug.LogWarning(
                    "[StaticObjectLODMeshInfoInit] StaticObjectPrefabElement buffer is empty on the config entity. " +
                    "Re-bake the Entities SubScene (StaticObjectSpawnerConfigAuthoring → object LOD sets). " +
                    $"Retry {_emptyBufferRetryCount}/{MaxEmptyBufferRetries}.");
            }

            if (_emptyBufferRetryCount >= MaxEmptyBufferRetries)
            {
                LogEmptyPrefabBufferFatal(configEntity,
                    "StaticObjectPrefabElement buffer is still empty after maximum retries.");
            }

            return;
        }

        _emptyBufferRetryCount = 0;
        infoBuffer.Clear();
        boundsBuffer.Clear();
        maxBoundsBuffer.Clear();

        int missing = 0;
        for (int i = 0; i < prefabBuffer.Length; i++)
        {
            var prefabEntity = prefabBuffer[i].prefabEntity;

            if (!EntityManager.Exists(prefabEntity) || !EntityManager.HasComponent<MaterialMeshInfo>(prefabEntity))
            {
                infoBuffer.Clear();
                boundsBuffer.Clear();
                maxBoundsBuffer.Clear();
                missing++;
                break;
            }

            var info = EntityManager.GetComponentData<MaterialMeshInfo>(prefabEntity);
            infoBuffer.Add(new StaticObjectLODMaterialMeshInfoElement { materialMeshInfo = info });

            AABB bounds = EntityManager.HasComponent<RenderBounds>(prefabEntity)
                ? EntityManager.GetComponentData<RenderBounds>(prefabEntity).Value
                : new AABB { Center = float3.zero, Extents = new float3(5f, 10f, 5f) };
            boundsBuffer.Add(new StaticObjectLODRenderBoundsElement { bounds = bounds });
        }

        if (missing > 0)
        {
            Debug.LogWarning($"[StaticObjectLODMeshInfoInit] {missing} prefab entities missing MaterialMeshInfo — retrying next frame.");
            return;
        }

        int objectTypeCount = prefabBuffer.Length / LodsPerObjectType;
        for (int typeIndex = 0; typeIndex < objectTypeCount; typeIndex++)
        {
            AABB maxBounds = default;
            bool hasBounds = false;
            for (int lod = 0; lod < LodsPerObjectType; lod++)
            {
                int slotIndex = typeIndex * LodsPerObjectType + lod;
                if (slotIndex >= boundsBuffer.Length)
                    continue;

                var lodBounds = boundsBuffer[slotIndex].bounds;
                if (!hasBounds)
                {
                    maxBounds = lodBounds;
                    hasBounds = true;
                }
                else
                {
                    maxBounds = EncapsulateAabb(maxBounds, lodBounds);
                }
            }

            if (!hasBounds)
                maxBounds = new AABB { Center = float3.zero, Extents = new float3(5f, 10f, 5f) };

            maxBoundsBuffer.Add(new StaticObjectTypeMaxRenderBoundsElement { bounds = maxBounds });
        }

        EntityManager.AddComponent<StaticObjectLODMeshInfoReady>(configEntity);
    }

    private void RefreshMaterialMeshInfos(Entity configEntity)
    {
        if (!EntityManager.HasBuffer<StaticObjectPrefabElement>(configEntity) ||
            !EntityManager.HasBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity))
            return;

        var prefabBuffer = EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity, isReadOnly: true);
        var infoBuffer = EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity);
        if (prefabBuffer.Length == 0 || infoBuffer.Length != prefabBuffer.Length)
            return;

        for (int i = 0; i < prefabBuffer.Length; i++)
        {
            var prefabEntity = prefabBuffer[i].prefabEntity;
            if (!EntityManager.Exists(prefabEntity) || !EntityManager.HasComponent<MaterialMeshInfo>(prefabEntity))
                return;

            infoBuffer[i] = new StaticObjectLODMaterialMeshInfoElement
            {
                materialMeshInfo = EntityManager.GetComponentData<MaterialMeshInfo>(prefabEntity)
            };
        }
    }

    private static AABB EncapsulateAabb(AABB a, AABB b)
    {
        float3 aMin = a.Center - a.Extents;
        float3 aMax = a.Center + a.Extents;
        float3 bMin = b.Center - b.Extents;
        float3 bMax = b.Center + b.Extents;
        float3 min = math.min(aMin, bMin);
        float3 max = math.max(aMax, bMax);
        return new AABB { Center = (min + max) * 0.5f, Extents = (max - min) * 0.5f };
    }

    private void LogEmptyPrefabBufferFatal(Entity configEntity, string detail)
    {
        Debug.LogError(
            $"[StaticObjectLODMeshInfoInit] {detail} " +
            "Open the Entities SubScene, verify StaticObjectSpawnerConfigAuthoring has valid object LOD sets, " +
            "then re-bake the SubScene. Static object spawning and LOD will not run until this is fixed.");
        _gaveUp = true;
    }
}
