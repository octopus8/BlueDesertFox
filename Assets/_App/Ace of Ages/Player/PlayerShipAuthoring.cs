using Unity.Entities;
using Unity.Mathematics;
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
            
            // Calculate bullet spawn point offset relative to player ship
            // This bakes the design-time spawn point position as a runtime offset
            float3 localOffset = float3.zero;
            quaternion localRotation = quaternion.identity;
            
            if (authoring.bulletSpawnPoint != null)
            {
                // Get local position and rotation relative to the player ship
                localOffset = authoring.bulletSpawnPoint.transform.localPosition;
                localRotation = authoring.bulletSpawnPoint.transform.localRotation;
            }
            else
            {
                Debug.LogWarning("[PlayerShipAuthoring] bulletSpawnPoint is null, bullets will spawn at ship center", authoring);
            }
            
            AddComponent(entity, new BulletSpawnPointReference
            {
                localOffset = localOffset,
                localRotation = localRotation
            });
        }
    }
}


public struct PlayerShip : IComponentData
{
}

/// <summary>
/// Component that stores the bullet spawn point's offset relative to the PlayerShip.
/// At bake time, this captures the spawn point's local position and rotation.
/// At runtime, systems apply this offset to the ship's world transform.
/// </summary>
public struct BulletSpawnPointReference : IComponentData
{
    /// <summary>Local offset from the ship's center to the bullet spawn point.</summary>
    public float3 localOffset;
    
    /// <summary>Local rotation of the spawn point relative to the ship.</summary>
    public quaternion localRotation;
}

