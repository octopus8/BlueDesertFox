using Unity.Entities;
using UnityEngine;

public class PrefabEntitiesReferencesAuthoring : MonoBehaviour
{
    public GameObject prefab;


    public class Baker : Baker<PrefabEntitiesReferencesAuthoring>
    {
        public override void Bake(PrefabEntitiesReferencesAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PrefabEntitiesReferences
            {
                prefabEntity = GetEntity(authoring.prefab, TransformUsageFlags.Dynamic)
            });
        }
    }
}



public struct PrefabEntitiesReferences : IComponentData
{
    public Entity prefabEntity;
}