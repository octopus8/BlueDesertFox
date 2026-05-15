using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

/// <summary>
/// After ECB prefab instantiation, child entities exist with runtime IDs. This system removes ECS Graphics
/// render components from every entity in LinkedEntityGroup so only GlobalStaticObjectInstanceSystem draws
/// the hierarchy (matching legacy TerrainTreeSpawningSystem parity).
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

            linkedScratch.Clear();
            if (em.HasBuffer<LinkedEntityGroup>(entity))
            {
                var linkedGroup = em.GetBuffer<LinkedEntityGroup>(entity);
                for (var j = 0; j < linkedGroup.Length; j++)
                    linkedScratch.Add(linkedGroup[j].Value);
            }

            // Strip after copying IDs: removals invalidate LinkedEntityGroup buffer handles if done during traversal.
            for (var k = 0; k < linkedScratch.Length; k++)
            {
                var e = linkedScratch[k];
                if (!em.Exists(e))
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
