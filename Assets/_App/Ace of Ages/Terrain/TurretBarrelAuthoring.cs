using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for the howitzer_barrel child of a turret dome.
/// Attach this to the howitzer_barrel GameObject inside ConcreteTurret_LOD0.prefab.
///
/// Because StaticObjectHierarchyFlattenUtility removes the ECS parent-child transform
/// hierarchy, the barrel entity ends up with a fully independent world-space LocalTransform.
/// This authoring component captures the barrel's dome-relative position and rotation at
/// bake time so TurretBarrelSystem can reapply them every frame after the dome rotates.
/// </summary>
public class TurretBarrelAuthoring : MonoBehaviour
{
    private class Baker : Baker<TurretBarrelAuthoring>
    {
        public override void Bake(TurretBarrelAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // The barrel's direct parent in the prefab hierarchy is the Dome GameObject.
            // GetEntity resolves it to the baked Dome entity, giving us the stable
            // Entity reference we need for the runtime component lookup.
            var domeGameObject = authoring.transform.parent.gameObject;
            var domeEntity = GetEntity(domeGameObject, TransformUsageFlags.Dynamic);

            AddComponent(entity, new TurretBarrelTag
            {
                domeEntity = domeEntity,
                localOffset = authoring.transform.localPosition,
                localRotation = authoring.transform.localRotation
            });
        }
    }
}

/// <summary>
/// Component placed on a turret barrel entity.
/// Stores the barrel's dome-relative transform so TurretBarrelSystem can keep the barrel
/// locked to the dome even after the ECS parent-child hierarchy has been flattened.
/// </summary>
public struct TurretBarrelTag : IComponentData
{
    /// <summary>The dome entity this barrel belongs to.</summary>
    public Entity domeEntity;

    /// <summary>Barrel position in dome-local space (from the prefab's localPosition).</summary>
    public float3 localOffset;

    /// <summary>Barrel rotation in dome-local space (from the prefab's localRotation).</summary>
    public quaternion localRotation;
}
