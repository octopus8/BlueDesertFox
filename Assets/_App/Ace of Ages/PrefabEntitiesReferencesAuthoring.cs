using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component that registers the shared prefab entities used by ECS systems at runtime.
/// Assign the source GameObjects in the Inspector; the Baker converts them into baked
/// <see cref="Entity"/> references stored on a singleton <see cref="PrefabEntitiesReferences"/> component.
/// </summary>
public class PrefabEntitiesReferencesAuthoring : MonoBehaviour
{
    /// <summary>The enemy unit prefab (basic enemy type).</summary>
    public GameObject enemyZeroPrefab;
    /// <summary>The simple bullet projectile prefab used by player and turret shooters.</summary>
    public GameObject bulletSimplePrefab;
    /// <summary>The small dirt explosion VFX prefab triggered on bullet-terrain collisions.</summary>
    public GameObject dirtExplosionSmallPrefab;
    /// <summary>The concrete turret static object prefab spawned on terrain tiles.</summary>
    public GameObject concreteTurretPrefab;


    /// <summary>
    /// Bakes the authoring prefab GameObjects into a <see cref="PrefabEntitiesReferences"/> singleton
    /// component, converting each prefab reference into an ECS <see cref="Entity"/>.
    /// </summary>
    public class Baker : Baker<PrefabEntitiesReferencesAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(PrefabEntitiesReferencesAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PrefabEntitiesReferences
            {
                enemyZeroEntity = GetEntity(authoring.enemyZeroPrefab, TransformUsageFlags.Dynamic),
                bulletSimplePrefab = GetEntity(authoring.bulletSimplePrefab, TransformUsageFlags.Dynamic),
                dirtExplosionSmallPrefab = GetEntity(authoring.dirtExplosionSmallPrefab, TransformUsageFlags.Dynamic)
            });
        }
    }
}



/// <summary>
/// Singleton ECS component that holds baked prefab <see cref="Entity"/> references for the
/// key spawnable objects in the scene. Systems read this component to instantiate enemies,
/// bullets, and VFX without needing to look up prefabs at runtime.
/// </summary>
public struct PrefabEntitiesReferences : IComponentData
{
    /// <summary>Baked entity for the basic enemy unit prefab.</summary>
    public Entity enemyZeroEntity;
    /// <summary>Baked entity for the simple bullet prefab used by shooters.</summary>
    public Entity bulletSimplePrefab;
    /// <summary>Baked entity for the small dirt explosion VFX prefab.</summary>
    public Entity dirtExplosionSmallPrefab;
}
