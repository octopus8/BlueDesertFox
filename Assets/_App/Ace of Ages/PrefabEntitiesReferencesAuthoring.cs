using Unity.Entities;
using UnityEngine;

public class PrefabEntitiesReferencesAuthoring : MonoBehaviour
{
    public GameObject enemyZeroPrefab;
    public GameObject bulletSimplePrefab;


    public class Baker : Baker<PrefabEntitiesReferencesAuthoring>
    {
        public override void Bake(PrefabEntitiesReferencesAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PrefabEntitiesReferences
            {
                enemyZeroEntity = GetEntity(authoring.enemyZeroPrefab, TransformUsageFlags.Dynamic),
                bulletSimplePrefab = GetEntity(authoring.bulletSimplePrefab, TransformUsageFlags.Dynamic)
            });
        }
    }
}



public struct PrefabEntitiesReferences : IComponentData
{
    public Entity enemyZeroEntity;
    public Entity bulletSimplePrefab;
}