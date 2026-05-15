using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

/// <summary>
/// After ECB prefab instantiation, child entities exist with runtime IDs. Strips ECS Graphics render
/// components so GlobalStaticObjectInstanceSystem can draw single-mesh instances from the root alone.
/// If any linked child still has <see cref="MaterialMeshInfo"/> (multi-part prefab / mesh on children),
/// those children keep rendering via Entities.Graphics at their flattened world transforms while the root
/// keeps instancing as today.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
public partial struct StaticObjectLinkedRendererStripSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PendingStaticObjectRendererStrip>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var pendingRoots = new NativeList<Entity>(Allocator.Temp);
        var linkedScratch = new NativeList<Entity>(32, Allocator.Temp);

        foreach (var (_, entity) in SystemAPI.Query<RefRO<PendingStaticObjectRendererStrip>>().WithEntityAccess())
        {
            pendingRoots.Add(entity);
        }

        var em = state.EntityManager;

        for (var i = 0; i < pendingRoots.Length; i++)
        {
            var entity = pendingRoots[i];
            if (!em.Exists(entity))
                continue;

            StaticObjectHierarchyFlattenUtility.FlattenSpawnHierarchy(entity, em);

            linkedScratch.Clear();
            if (em.HasBuffer<LinkedEntityGroup>(entity))
            {
                var linkedGroup = em.GetBuffer<LinkedEntityGroup>(entity);
                for (var j = 0; j < linkedGroup.Length; j++)
                    linkedScratch.Add(linkedGroup[j].Value);
            }

            var rootEntity = entity;
            var preserveChildGraphics = false;
            for (var p = 0; p < linkedScratch.Length; p++)
            {
                var linked = linkedScratch[p];
                if (linked != rootEntity && em.Exists(linked) && em.HasComponent<MaterialMeshInfo>(linked))
                {
                    preserveChildGraphics = true;
                    break;
                }
            }

            // Strip after copying IDs: removals invalidate LinkedEntityGroup buffer handles if done during traversal.
            for (var k = 0; k < linkedScratch.Length; k++)
            {
                var e = linkedScratch[k];
                if (!em.Exists(e))
                    continue;

                if (preserveChildGraphics && e != rootEntity)
                    continue;

                if (em.HasComponent<MaterialMeshInfo>(e))
                    em.RemoveComponent<MaterialMeshInfo>(e);

                if (em.HasComponent<RenderBounds>(e))
                    em.RemoveComponent<RenderBounds>(e);
            }

            if (em.HasComponent<PendingStaticObjectRendererStrip>(entity))
                em.RemoveComponent<PendingStaticObjectRendererStrip>(entity);
        }

        linkedScratch.Dispose();
        pendingRoots.Dispose();
    }
}
