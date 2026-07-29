using Unity.Entities;

/// <summary>
/// Explicitly destroys all entities in a static-object prefab instance via <see cref="LinkedEntityGroup"/>.
/// Children are destroyed before the root (reverse buffer order).
/// </summary>
public static class StaticObjectHierarchyDestroyUtility
{
    /// <summary>
    /// Returns how many existing entities are in <paramref name="root"/>'s <see cref="LinkedEntityGroup"/>,
    /// or 1 when the root has no linked buffer.
    /// </summary>
    public static int CountLinkedEntities(Entity root, EntityManager entityManager)
    {
        if (!entityManager.Exists(root))
            return 0;

        if (!entityManager.HasBuffer<LinkedEntityGroup>(root))
            return 1;

        var linkedBuffer = entityManager.GetBuffer<LinkedEntityGroup>(root);
        if (linkedBuffer.Length <= 1)
            return 1;

        int count = 0;
        for (var i = 0; i < linkedBuffer.Length; i++)
        {
            if (entityManager.Exists(linkedBuffer[i].Value))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Queues destruction of every existing entity in the linked group, children first then root.
    /// </summary>
    /// <returns>Number of entities queued for destruction.</returns>
    public static int DestroyHierarchy(Entity root, EntityCommandBuffer ecb, EntityManager entityManager)
    {
        if (!entityManager.Exists(root))
            return 0;

        if (!entityManager.HasBuffer<LinkedEntityGroup>(root))
        {
            ecb.DestroyEntity(root);
            return 1;
        }

        var linkedBuffer = entityManager.GetBuffer<LinkedEntityGroup>(root);
        if (linkedBuffer.Length <= 1)
        {
            ecb.DestroyEntity(root);
            return 1;
        }

        var destroyed = 0;
        for (var i = linkedBuffer.Length - 1; i >= 0; i--)
        {
            var e = linkedBuffer[i].Value;
            if (!entityManager.Exists(e))
                continue;

            ecb.DestroyEntity(e);
            destroyed++;
        }

        return destroyed;
    }

    /// <summary>
    /// Immediately destroys every existing entity in the linked group, children first then root.
    /// </summary>
    /// <returns>Number of entities destroyed.</returns>
    public static int DestroyHierarchyImmediate(Entity root, EntityManager entityManager)
    {
        if (!entityManager.Exists(root))
            return 0;

        if (!entityManager.HasBuffer<LinkedEntityGroup>(root))
        {
            entityManager.DestroyEntity(root);
            return 1;
        }

        var linkedBuffer = entityManager.GetBuffer<LinkedEntityGroup>(root);
        if (linkedBuffer.Length <= 1)
        {
            entityManager.DestroyEntity(root);
            return 1;
        }

        var destroyed = 0;
        for (var i = linkedBuffer.Length - 1; i >= 0; i--)
        {
            var e = linkedBuffer[i].Value;
            if (!entityManager.Exists(e))
                continue;

            entityManager.DestroyEntity(e);
            destroyed++;
        }

        return destroyed;
    }
}
