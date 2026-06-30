using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Removes ECS transform parenting from instantiated static-object prefabs while preserving world poses,
/// and assigns <see cref="StaticObjectTileOwnership"/> to descendants so scrolling matches the root.
/// </summary>
public static class StaticObjectHierarchyFlattenUtility
{
    /// <summary>
    /// Builds a <see cref="LocalToWorld"/> matrix from a world-space <see cref="LocalTransform"/>.
    /// Use when writing transforms after <see cref="LocalToWorldSystem"/> has already run this frame.
    /// </summary>
    public static LocalToWorld LocalToWorldFromLocalTransform(LocalTransform lt)
        => new LocalToWorld { Value = float4x4.TRS(lt.Position, lt.Rotation, new float3(lt.Scale)) };

    /// <summary>
    /// Flattens all entities in <see cref="LinkedEntityGroup"/> under <paramref name="root"/>.
    /// Expects <paramref name="root"/> to already have world-space <see cref="LocalTransform"/> and tile ownership.
    /// </summary>
    public static void FlattenSpawnHierarchy(Entity root, EntityManager entityManager)
    {
        if (!entityManager.Exists(root) || !entityManager.HasBuffer<LinkedEntityGroup>(root))
            return;

        var linkedBuffer = entityManager.GetBuffer<LinkedEntityGroup>(root);
        if (linkedBuffer.Length <= 1)
            return;

        if (!entityManager.HasComponent<StaticObjectTileOwnership>(root) ||
            !entityManager.HasComponent<LocalTransform>(root))
            return;

        var rootOwnership = entityManager.GetComponentData<StaticObjectTileOwnership>(root);
        if (!entityManager.Exists(rootOwnership.tileEntity) ||
            !entityManager.HasComponent<LocalTransform>(rootOwnership.tileEntity))
            return;

        var linkedEntities = new NativeList<Entity>(linkedBuffer.Length, Allocator.Temp);
        for (var i = 0; i < linkedBuffer.Length; i++)
            linkedEntities.Add(linkedBuffer[i].Value);

        var inLinked = new NativeParallelHashMap<Entity, byte>(linkedEntities.Length, Allocator.Temp);
        for (var i = 0; i < linkedEntities.Length; i++)
            inLinked.TryAdd(linkedEntities[i], 0);

        var childrenByParent = new NativeParallelMultiHashMap<Entity, Entity>(linkedEntities.Length, Allocator.Temp);
        for (var i = 0; i < linkedEntities.Length; i++)
        {
            var e = linkedEntities[i];
            if (e == root || !entityManager.HasComponent<Parent>(e))
                continue;

            var parent = entityManager.GetComponentData<Parent>(e).Value;
            if (!inLinked.ContainsKey(parent))
                continue;

            childrenByParent.Add(parent, e);
        }

        var worldMatrices = new NativeParallelHashMap<Entity, float4x4>(linkedEntities.Length, Allocator.Temp);

        var rootLt = entityManager.GetComponentData<LocalTransform>(root);
        var rootWorld = float4x4.TRS(rootLt.Position, rootLt.Rotation, new float3(rootLt.Scale));
        worldMatrices[root] = rootWorld;

        AccumulateWorldTransforms(entityManager, childrenByParent, worldMatrices, root, rootWorld);

        var tileWorldPos = entityManager.GetComponentData<LocalTransform>(rootOwnership.tileEntity).Position;

        for (var i = 0; i < linkedEntities.Length; i++)
        {
            var e = linkedEntities[i];
            if (e == root || !worldMatrices.TryGetValue(e, out var worldMat))
                continue;

            if (entityManager.HasComponent<Parent>(e))
                entityManager.RemoveComponent<Parent>(e);

            var worldTransform = LocalTransformFromWorldMatrix(worldMat);
            entityManager.SetComponentData(e, worldTransform);
            entityManager.SetComponentData(e, LocalToWorldFromLocalTransform(worldTransform));

            if (!entityManager.HasComponent<StaticObjectTileOwnership>(e))
            {
                entityManager.AddComponentData(e, new StaticObjectTileOwnership
                {
                    tileEntity = rootOwnership.tileEntity,
                    localOffset = worldTransform.Position - tileWorldPos,
                    localRotation = worldTransform.Rotation
                });
            }
        }

        worldMatrices.Dispose();
        childrenByParent.Dispose();
        inLinked.Dispose();
        linkedEntities.Dispose();
    }

    /// <summary>
    /// Recursively walks the child graph rooted at <paramref name="parentEntity"/>, composing each
    /// child's local TRS matrix with <paramref name="parentWorldMatrix"/> to produce a world-space
    /// matrix, and stores the result in <paramref name="worldMatrices"/> for each child entity.
    /// </summary>
    private static void AccumulateWorldTransforms(
        EntityManager entityManager,
        NativeParallelMultiHashMap<Entity, Entity> childrenByParent,
        NativeParallelHashMap<Entity, float4x4> worldMatrices,
        Entity parentEntity,
        float4x4 parentWorldMatrix)
    {
        if (!childrenByParent.TryGetFirstValue(parentEntity, out var child, out var it))
            return;

        do
        {
            if (!entityManager.Exists(child) || !entityManager.HasComponent<LocalTransform>(child))
                continue;

            var local = entityManager.GetComponentData<LocalTransform>(child);
            var localMat = float4x4.TRS(local.Position, local.Rotation, new float3(local.Scale));
            var worldMat = math.mul(parentWorldMatrix, localMat);
            worldMatrices[child] = worldMat;
            AccumulateWorldTransforms(entityManager, childrenByParent, worldMatrices, child, worldMat);
        } while (childrenByParent.TryGetNextValue(out child, ref it));
    }

    /// <summary>
    /// Decomposes a world-space TRS matrix <paramref name="m"/> into position, rotation, and the
    /// maximum axis scale, returning a <see cref="LocalTransform"/> suitable for assigning directly
    /// as a world-space transform to a hierarchy-free entity.
    /// </summary>
    private static LocalTransform LocalTransformFromWorldMatrix(float4x4 m)
    {
        var c0 = m.c0.xyz;
        var c1 = m.c1.xyz;
        var c2 = m.c2.xyz;
        var sx = math.length(c0);
        var sy = math.length(c1);
        var sz = math.length(c2);
        const float eps = 1e-8f;
        sx = math.max(sx, eps);
        sy = math.max(sy, eps);
        sz = math.max(sz, eps);

        var rotMat = new float3x3(c0 / sx, c1 / sy, c2 / sz);
        var rot = new quaternion(rotMat);
        var uniformScale = math.cmax(new float3(sx, sy, sz));

        return new LocalTransform
        {
            Position = m.c3.xyz,
            Rotation = rot,
            Scale = uniformScale
        };
    }
}
