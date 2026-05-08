using Unity.Entities;
using UnityEngine;

public class PlayerShipAuthoring : MonoBehaviour
{
    [SerializeField] private GameObject bulletSpawnPoint;
    
    public class Baker : Baker<PlayerShipAuthoring>
    {
        public override void Bake(PlayerShipAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerShip>(entity);
            
            // Add managed component to store bullet spawn point Transform reference
            // This allows systems to access the spawn point position/rotation at runtime
            AddComponentObject(entity, new BulletSpawnPointReference
            {
                spawnPoint = authoring.bulletSpawnPoint?.transform
            });
        }
    }
}


public struct PlayerShip : IComponentData
{
}

/// <summary>
/// Managed component that holds a reference to the bullet spawn point Transform.
/// This allows ECS systems to read the spawn point's position and rotation at runtime.
/// </summary>
public class BulletSpawnPointReference : IComponentData
{
    public Transform spawnPoint;
}

