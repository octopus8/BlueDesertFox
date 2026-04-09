using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for player-based scroll velocity.
/// Scrolls terrain in the direction the player is facing at a constant speed.
/// Only one velocity provider (PlayerScrollVelocityAuthoring or ConstantScrollVelocityAuthoring) should be in the scene.
/// </summary>
public class PlayerScrollVelocityAuthoring : MonoBehaviour
{
    [Tooltip("Scroll speed in units per second (scrolls in player's forward direction)")]
    public float speed = 50f;

    public class Baker : Baker<PlayerScrollVelocityAuthoring>
    {
        public override void Bake(PlayerScrollVelocityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new PlayerTerrainScrollVelocityConfig
            {
                speed = authoring.speed
            });
        }
    }
}


