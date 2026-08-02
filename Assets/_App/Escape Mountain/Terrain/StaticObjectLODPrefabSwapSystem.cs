using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// Processes structural static-object LOD transitions by destroying the current prefab instance
/// and instantiating the target LOD prefab (e.g. turret LOD1 mesh → LOD0 hierarchy with shooter).
/// Mesh-only LOD types are handled in-place by <see cref="StaticObjectLODUpdateSystem"/>.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(StaticObjectLODUpdateSystem))]
[UpdateBefore(typeof(TurretAimingSystem))]
[UpdateBefore(typeof(LocalToWorldSystem))]
public partial struct StaticObjectLODPrefabSwapSystem : ISystem
{
    private const int DefaultMaxSwapsPerFrame = 8;

#if UNITY_EDITOR
    private static readonly SharedStatic<ProfilerMarker> s_ProfilerMarker =
        SharedStatic<ProfilerMarker>.GetOrCreate<ProfilerMarkerKey>();
    private struct ProfilerMarkerKey { }
#endif

    /// <summary>Requires LOD config and at least one instance with pending structural swap data.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectLODConfig>();
        state.RequireForUpdate<StaticObjectPrefabElement>();
        state.RequireForUpdate<GlobalStaticObjectInstanceData>();

#if UNITY_EDITOR
        s_ProfilerMarker.Data = new ProfilerMarker("StaticObjectLOD.PrefabSwap");
#endif
    }

    /// <summary>
    /// Collects entities with <see cref="GlobalStaticObjectInstanceData.pendingPrefabLOD"/> set,
    /// prioritizes upgrades toward lower LOD indices, and re-instantiates up to the frame budget.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        var lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();
        int maxSwaps = lodConfig.maxPrefabLODSwapsPerFrame > 0
            ? lodConfig.maxPrefabLODSwapsPerFrame
            : DefaultMaxSwapsPerFrame;

        var upgrades = new NativeList<Entity>(maxSwaps * 2, Allocator.Temp);
        var downgrades = new NativeList<Entity>(maxSwaps * 2, Allocator.Temp);

        foreach (var (instanceData, entity) in SystemAPI
                     .Query<RefRO<GlobalStaticObjectInstanceData>>()
                     .WithAll<StaticObjectTileOwnership, LocalTransform, GlobalStaticObjectInstance>()
                     .WithEntityAccess())
        {
            byte pending = instanceData.ValueRO.pendingPrefabLOD;
            if (pending == GlobalStaticObjectInstanceData.NoPendingPrefabLOD)
                continue;

            if (pending < instanceData.ValueRO.currentLODLevel)
                upgrades.Add(entity);
            else
                downgrades.Add(entity);
        }

        if (upgrades.Length == 0 && downgrades.Length == 0)
        {
            upgrades.Dispose();
            downgrades.Dispose();
            return;
        }

#if UNITY_EDITOR
        s_ProfilerMarker.Data.Begin();
#endif
        try
        {
            var em = state.EntityManager;
            var configEntity = SystemAPI.GetSingletonEntity<StaticObjectLODConfig>();

            // Copy prefab entities before any structural change — DynamicBuffer handles invalidate
            // after DestroyEntity / Instantiate.
            var prefabBuffer = em.GetBuffer<StaticObjectPrefabElement>(configEntity, isReadOnly: true);
            var prefabEntities = new NativeArray<Entity>(prefabBuffer.Length, Allocator.Temp);
            for (int i = 0; i < prefabBuffer.Length; i++)
                prefabEntities[i] = prefabBuffer[i].prefabEntity;

            NativeArray<bool> hierarchicalSlots = BuildHierarchicalSlots(em, configEntity, prefabEntities);
            NativeArray<StaticObjectTypeScaleElement> typeScales = BuildTypeScales(
                em, configEntity, lodConfig, prefabEntities.Length);

            int swapsDone = 0;
            swapsDone += ProcessSwapList(
                em, upgrades, prefabEntities, hierarchicalSlots, typeScales, lodConfig, maxSwaps - swapsDone);
            if (swapsDone < maxSwaps)
            {
                ProcessSwapList(
                    em, downgrades, prefabEntities, hierarchicalSlots, typeScales, lodConfig, maxSwaps - swapsDone);
            }

            hierarchicalSlots.Dispose();
            typeScales.Dispose();
            prefabEntities.Dispose();
        }
        finally
        {
            upgrades.Dispose();
            downgrades.Dispose();
#if UNITY_EDITOR
            s_ProfilerMarker.Data.End();
#endif
        }
    }

    private static int ProcessSwapList(
        EntityManager em,
        NativeList<Entity> entities,
        NativeArray<Entity> prefabEntities,
        NativeArray<bool> hierarchicalSlots,
        NativeArray<StaticObjectTypeScaleElement> typeScales,
        StaticObjectLODConfig lodConfig,
        int budget)
    {
        int swapsDone = 0;
        int lodsPerType = math.max(1, lodConfig.lodsPerObjectType);

        for (int i = 0; i < entities.Length && swapsDone < budget; i++)
        {
            Entity oldEntity = entities[i];
            if (!em.Exists(oldEntity)
                || !em.HasComponent<GlobalStaticObjectInstanceData>(oldEntity)
                || !em.HasComponent<StaticObjectTileOwnership>(oldEntity)
                || !em.HasComponent<LocalTransform>(oldEntity))
            {
                continue;
            }

            var instanceData = em.GetComponentData<GlobalStaticObjectInstanceData>(oldEntity);
            byte pendingLOD = instanceData.pendingPrefabLOD;
            if (pendingLOD == GlobalStaticObjectInstanceData.NoPendingPrefabLOD
                || pendingLOD == instanceData.currentLODLevel)
            {
                if (pendingLOD == instanceData.currentLODLevel)
                {
                    instanceData.pendingPrefabLOD = GlobalStaticObjectInstanceData.NoPendingPrefabLOD;
                    em.SetComponentData(oldEntity, instanceData);
                }
                continue;
            }

            int prefabIndex = (instanceData.objectTypeIndex * lodsPerType) + pendingLOD;
            if (prefabIndex < 0 || prefabIndex >= prefabEntities.Length)
                continue;

            Entity prefab = prefabEntities[prefabIndex];
            if (!em.Exists(prefab))
                continue;

            var ownership = em.GetComponentData<StaticObjectTileOwnership>(oldEntity);
            if (!em.Exists(ownership.tileEntity))
                continue;

            var oldTransform = em.GetComponentData<LocalTransform>(oldEntity);
            float spawnScale = instanceData.spawnScale > 0f ? instanceData.spawnScale : oldTransform.Scale;
            float displayScale = spawnScale;
            if (instanceData.objectTypeIndex < typeScales.Length)
                displayScale = spawnScale * typeScales[instanceData.objectTypeIndex].GetLodScaleMultiplier(pendingLOD);

            var newInstanceData = instanceData;
            newInstanceData.currentLODLevel = pendingLOD;
            newInstanceData.pendingPrefabLOD = GlobalStaticObjectInstanceData.NoPendingPrefabLOD;
            newInstanceData.prefabIndex = instanceData.objectTypeIndex * lodsPerType;
            newInstanceData.spawnScale = spawnScale;

            var newTransform = new LocalTransform
            {
                Position = oldTransform.Position,
                Rotation = ownership.localRotation,
                Scale = displayScale
            };

            bool addDisableRendering = prefabIndex < hierarchicalSlots.Length && hierarchicalSlots[prefabIndex];

            StaticObjectSpawnUtility.RemoveFromTileSpawnBuffer(em, ownership.tileEntity, oldEntity);
            StaticObjectHierarchyDestroyUtility.DestroyHierarchyImmediate(oldEntity, em);

            StaticObjectSpawnUtility.InstantiateOnTile(
                em,
                prefab,
                ownership.tileEntity,
                newTransform,
                newInstanceData,
                ownership.localOffset,
                ownership.localRotation,
                addDisableRendering);

            swapsDone++;
        }

        return swapsDone;
    }

    private static NativeArray<bool> BuildHierarchicalSlots(
        EntityManager em,
        Entity configEntity,
        NativeArray<Entity> prefabEntities)
    {
        var hierarchicalSlots = new NativeArray<bool>(prefabEntities.Length, Allocator.Temp);
        bool usedBaked = em.HasBuffer<StaticObjectLODHierarchicalSlotElement>(configEntity)
            && em.GetBuffer<StaticObjectLODHierarchicalSlotElement>(configEntity, isReadOnly: true).Length
                == prefabEntities.Length;

        if (usedBaked)
        {
            var hierarchicalBuffer = em.GetBuffer<StaticObjectLODHierarchicalSlotElement>(configEntity, isReadOnly: true);
            for (int i = 0; i < hierarchicalSlots.Length; i++)
                hierarchicalSlots[i] = hierarchicalBuffer[i].isHierarchical;
        }
        else
        {
            for (int i = 0; i < hierarchicalSlots.Length; i++)
            {
                Entity prefabEntity = prefabEntities[i];
                hierarchicalSlots[i] = em.Exists(prefabEntity)
                    && em.HasComponent<PendingStaticObjectRendererStrip>(prefabEntity);
            }
        }

        return hierarchicalSlots;
    }

    private static NativeArray<StaticObjectTypeScaleElement> BuildTypeScales(
        EntityManager em,
        Entity configEntity,
        StaticObjectLODConfig lodConfig,
        int prefabCount)
    {
        int lodsPerType = math.max(1, lodConfig.lodsPerObjectType);
        int objectTypeCount = prefabCount / lodsPerType;
        var typeScales = new NativeArray<StaticObjectTypeScaleElement>(objectTypeCount, Allocator.Temp);
        var defaultScale = new StaticObjectTypeScaleElement
        {
            baseScale = 1f,
            lod1ScaleMultiplier = 1f,
            lod2ScaleMultiplier = 1f
        };

        if (em.HasBuffer<StaticObjectTypeScaleElement>(configEntity))
        {
            var buffer = em.GetBuffer<StaticObjectTypeScaleElement>(configEntity, isReadOnly: true);
            for (int i = 0; i < objectTypeCount; i++)
                typeScales[i] = i < buffer.Length ? buffer[i] : defaultScale;
        }
        else
        {
            for (int i = 0; i < objectTypeCount; i++)
                typeScales[i] = defaultScale;
        }

        return typeScales;
    }
}
