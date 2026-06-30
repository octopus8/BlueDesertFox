using Unity.Collections;
using Unity.Entities;

/// <summary>
/// After ECB prefab instantiation, child entities exist with runtime IDs.
/// This system flattens the ECS transform hierarchy (detaches Parent components) so each child
/// entity has a world-space <see cref="LocalTransform"/> and its own
/// <see cref="StaticObjectTileOwnership"/> for scroll-position updates.
///
/// No rendering components are stripped: both the root and any child parts (e.g. the Dome) keep
/// their <see cref="Unity.Rendering.MaterialMeshInfo"/> and are rendered via Entities.Graphics (BRG).
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
public partial struct StaticObjectLinkedRendererStripSystem : ISystem
{
    /// <summary>Registers the <see cref="PendingStaticObjectRendererStrip"/> requirement.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PendingStaticObjectRendererStrip>();
    }

    /// <summary>
    /// For each entity tagged <see cref="PendingStaticObjectRendererStrip"/>, detaches the Parent
    /// hierarchy (removes <c>Parent</c> and <c>Child</c> components) and assigns
    /// <see cref="StaticObjectTileOwnership"/> to each child so it can be individually scroll-positioned.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        var pendingRoots = new NativeList<Entity>(Allocator.Temp);

        foreach (var (_, entity) in SystemAPI.Query<RefRO<PendingStaticObjectRendererStrip>>().WithEntityAccess())
            pendingRoots.Add(entity);

        var em = state.EntityManager;

        for (var i = 0; i < pendingRoots.Length; i++)
        {
            var entity = pendingRoots[i];
            if (!em.Exists(entity))
                continue;

            // Detach child entities from the transform hierarchy and assign tile ownership.
            StaticObjectHierarchyFlattenUtility.FlattenSpawnHierarchy(entity, em);

            if (em.HasComponent<PendingStaticObjectRendererStrip>(entity))
                em.RemoveComponent<PendingStaticObjectRendererStrip>(entity);
        }

        pendingRoots.Dispose();
    }
}
