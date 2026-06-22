using Unity.Entities;
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
/// After the buffer is populated, a <see cref="StaticObjectLODMeshInfoReady"/> tag is added to the
/// config entity and this system disables itself.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial class StaticObjectLODMeshInfoInitSystem : SystemBase
{
    private const int MaxEmptyBufferRetries = 120;
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

        if (!EntityManager.HasBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity))
            EntityManager.AddBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity);

        var prefabBuffer = EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity, isReadOnly: true);
        var infoBuffer = EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity);

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

        int missing = 0;
        for (int i = 0; i < prefabBuffer.Length; i++)
        {
            var prefabEntity = prefabBuffer[i].prefabEntity;

            if (!EntityManager.Exists(prefabEntity) || !EntityManager.HasComponent<MaterialMeshInfo>(prefabEntity))
            {
                infoBuffer.Clear();
                missing++;
                break;
            }

            var info = EntityManager.GetComponentData<MaterialMeshInfo>(prefabEntity);
            infoBuffer.Add(new StaticObjectLODMaterialMeshInfoElement { materialMeshInfo = info });
        }

        if (missing > 0)
        {
            Debug.LogWarning($"[StaticObjectLODMeshInfoInit] {missing} prefab entities missing MaterialMeshInfo — retrying next frame.");
            return;
        }

        int populatedCount = infoBuffer.Length;

        EntityManager.AddComponent<StaticObjectLODMeshInfoReady>(configEntity);
        Enabled = false;

        Debug.Log($"[StaticObjectLODMeshInfoInit] Populated {populatedCount} LOD MaterialMeshInfo slots.");
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
