using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Shared helpers for instantiating static-object prefabs onto terrain tiles
/// (initial spawn and structural LOD prefab swaps).
/// </summary>
public static class StaticObjectSpawnUtility
{
    /// <summary>
    /// Instantiates <paramref name="prefab"/> on <paramref name="tileEntity"/> with ownership,
    /// chunk membership, instance data, and optional root <see cref="DisableRendering"/>.
    /// Appends the new root to the tile's <see cref="SpawnedStaticObjectReference"/> buffer when present.
    /// </summary>
    /// <returns>The new root entity, or <see cref="Entity.Null"/> if instantiate was skipped.</returns>
    public static Entity InstantiateOnTile(
        EntityManager entityManager,
        Entity prefab,
        Entity tileEntity,
        LocalTransform transform,
        GlobalStaticObjectInstanceData instanceData,
        float3 localOffset,
        quaternion localRotation,
        bool addDisableRendering)
    {
        if (!entityManager.Exists(prefab) || !entityManager.Exists(tileEntity))
            return Entity.Null;

        Entity objectEntity = entityManager.Instantiate(prefab);

        if (entityManager.HasComponent<LocalTransform>(objectEntity))
            entityManager.SetComponentData(objectEntity, transform);
        else
            entityManager.AddComponentData(objectEntity, transform);

        var localToWorld = StaticObjectHierarchyFlattenUtility.LocalToWorldFromLocalTransform(transform);
        if (entityManager.HasComponent<LocalToWorld>(objectEntity))
            entityManager.SetComponentData(objectEntity, localToWorld);
        else
            entityManager.AddComponentData(objectEntity, localToWorld);

        if (addDisableRendering && !entityManager.HasComponent<DisableRendering>(objectEntity))
            entityManager.AddComponent<DisableRendering>(objectEntity);

        if (entityManager.HasComponent<GlobalStaticObjectInstanceData>(objectEntity))
            entityManager.SetComponentData(objectEntity, instanceData);
        else
            entityManager.AddComponentData(objectEntity, instanceData);

        if (!entityManager.HasComponent<GlobalStaticObjectInstance>(objectEntity))
            entityManager.AddComponent<GlobalStaticObjectInstance>(objectEntity);

        var ownership = new StaticObjectTileOwnership
        {
            tileEntity = tileEntity,
            localOffset = localOffset,
            localRotation = localRotation
        };
        if (!entityManager.HasComponent<StaticObjectTileOwnership>(objectEntity))
            entityManager.AddComponentData(objectEntity, ownership);
        else
            entityManager.SetComponentData(objectEntity, ownership);

        var chunkMembership = new StaticObjectChunkMembership
        {
            chunkCoord = StaticObjectSpatialChunkUtility.GetChunkCoord(transform.Position)
        };
        if (!entityManager.HasComponent<StaticObjectChunkMembership>(objectEntity))
            entityManager.AddComponentData(objectEntity, chunkMembership);
        else
            entityManager.SetComponentData(objectEntity, chunkMembership);

        if (entityManager.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
        {
            entityManager.GetBuffer<SpawnedStaticObjectReference>(tileEntity).Add(
                new SpawnedStaticObjectReference { objectEntity = objectEntity });
        }

        return objectEntity;
    }

    /// <summary>
    /// Removes the first matching root from a tile's <see cref="SpawnedStaticObjectReference"/> buffer.
    /// </summary>
    public static void RemoveFromTileSpawnBuffer(EntityManager entityManager, Entity tileEntity, Entity objectEntity)
    {
        if (!entityManager.Exists(tileEntity) || !entityManager.HasBuffer<SpawnedStaticObjectReference>(tileEntity))
            return;

        var buffer = entityManager.GetBuffer<SpawnedStaticObjectReference>(tileEntity);
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].objectEntity != objectEntity)
                continue;

            buffer.RemoveAt(i);
            return;
        }
    }
}
