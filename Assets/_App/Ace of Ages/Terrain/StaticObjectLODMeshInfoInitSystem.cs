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

        // Already initialised — disable and exit.
        if (EntityManager.HasComponent<StaticObjectLODMeshInfoReady>(configEntity))
        {
            Enabled = false;
            return;
        }

        // Perform any structural changes BEFORE obtaining buffer references —
        // AddBuffer is a structural change that would invalidate any previously held buffer handle.
        if (!EntityManager.HasBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity))
            EntityManager.AddBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity);

        // Fetch both buffers after all structural changes are complete.
        var prefabBuffer = EntityManager.GetBuffer<StaticObjectPrefabElement>(configEntity, isReadOnly: true);
        var infoBuffer   = EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity);

        if (prefabBuffer.Length == 0)
        {
            Debug.LogWarning("[StaticObjectLODMeshInfoInit] StaticObjectPrefabElement buffer is empty — retrying next frame.");
            return;
        }

        infoBuffer.Clear();

        int missing = 0;
        for (int i = 0; i < prefabBuffer.Length; i++)
        {
            var prefabEntity = prefabBuffer[i].prefabEntity;

            if (!EntityManager.Exists(prefabEntity) || !EntityManager.HasComponent<MaterialMeshInfo>(prefabEntity))
            {
                // Prefab entity not yet available — abort and retry next frame.
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

        // Capture length before the structural change below invalidates the buffer handle.
        int populatedCount = infoBuffer.Length;

        // All slots populated — mark ready and self-disable.
        EntityManager.AddComponent<StaticObjectLODMeshInfoReady>(configEntity);
        Enabled = false;

        Debug.Log($"[StaticObjectLODMeshInfoInit] Populated {populatedCount} LOD MaterialMeshInfo slots.");
    }
}
