using Unity.Entities;
using UnityEngine;

public class PlayerShipAuthoring : MonoBehaviour
{
    [SerializeField] private GameObject bulletSpawnPoint;
    
    public class Baker : Baker<PlayerTagAuthoring>
    {
        public override void Bake(PlayerTagAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerShip>(entity);
        }
    }
}


public struct PlayerShip : IComponentData
{
}
