using Unity.Entities;
using UnityEngine;

public class PlayerTagAuthoring : MonoBehaviour
{
    public class Baker : Baker<PlayerTagAuthoring>
    {
        public override void Bake(PlayerTagAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
        }
    }
}


public struct PlayerTag : IComponentData
{
}