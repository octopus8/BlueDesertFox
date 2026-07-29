using Unity.Entities;
using Unity.Rendering;
using UnityEngine;

/// <summary>
/// Place on child GameObjects with renderers inside hierarchical static-object prefabs
/// (e.g. turret dome/barrel). Bakes <see cref="DisableRendering"/> onto the child entity so
/// BRG does not draw it at the prefab subscene position before hierarchy flatten runs at spawn.
/// Each child baker may only modify its own entity.
/// </summary>
[DisallowMultipleComponent]
public class StaticObjectHierarchyChildAuthoring : MonoBehaviour
{
    public class Baker : Baker<StaticObjectHierarchyChildAuthoring>
    {
        public override void Bake(StaticObjectHierarchyChildAuthoring authoring)
        {
            if (authoring.GetComponent<MeshRenderer>() == null)
                return;

            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<DisableRendering>(entity);
        }
    }
}
