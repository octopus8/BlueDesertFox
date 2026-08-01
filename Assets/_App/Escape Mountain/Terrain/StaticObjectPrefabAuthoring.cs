using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place on static object LOD prefab roots (trees, turrets, etc.).
/// Bakes <see cref="GlobalStaticObjectInstance"/> and default <see cref="GlobalStaticObjectInstanceData"/>
/// onto the prefab entity so the spawn system can use SetComponent instead of AddComponent at runtime.
/// </summary>
[DisallowMultipleComponent]
public class StaticObjectPrefabAuthoring : MonoBehaviour
{
    public class Baker : Baker<StaticObjectPrefabAuthoring>
    {
        public override void Bake(StaticObjectPrefabAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<GlobalStaticObjectInstance>(entity);
            AddComponent(entity, new GlobalStaticObjectInstanceData
            {
                pendingPrefabLOD = GlobalStaticObjectInstanceData.NoPendingPrefabLOD
            });

            if (authoring.GetComponentsInChildren<Transform>(true).Length > 1)
                AddComponent<PendingStaticObjectRendererStrip>(entity);
        }
    }
}
