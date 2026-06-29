using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

/// <summary>
/// One-shot system that runs once at world startup to populate the
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
/// config entity and this system disables itself.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial class StaticObjectLODMeshInfoInitSystem : SystemBase
{
    private const int MaxEmptyBufferRetries = 120;
    private const int LodsPerObjectType = 3;
    private int _emptyBufferRetryCount;

    /// <summary>Registers <see cref="StaticObjectSpawnerConfig"/> and <see cref="StaticObjectPrefabElement"/> requirements.</summary>
    protected override void OnCreate()
    {
        RequireForUpdate<StaticObjectSpawnerConfig>();
        RequireForUpdate<StaticObjectPrefabElement>();
    }

    /// <summary>
    /// On the first frame, reads the baked <see cref="MaterialMeshInfo"/> from each LOD prefab entity,
    /// populates the <see cref="StaticObjectLODMaterialMeshInfoElement"/> buffer on the config entity,
    /// adds the <see cref="StaticObjectLODMeshInfoReady"/> tag, and disables this system.
    /// </summary>
    protected override void OnUpdate()
    {
        var configEntity = SystemAPI.GetSingletonEntity<StaticObjectSpawnerConfig>();

        if (EntityManager.HasComponent<StaticObjectLODMeshInfoReady>(configEntity))
        {
            Enabled = false;
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

        int populatedCount = infoBuffer.Length;
        int maxBoundsCount = maxBoundsBuffer.Length;

        EntityManager.AddComponent<StaticObjectLODMeshInfoReady>(configEntity);
        Enabled = false;

        Debug.Log($"[StaticObjectLODMeshInfoInit] Populated {populatedCount} LOD MaterialMeshInfo slots and {maxBoundsCount} type max-bounds entries.");
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
        Enabled = false;
    }
}
