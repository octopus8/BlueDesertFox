using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for bullet pool configuration.
/// Place this on a GameObject in the scene (typically with TerrainConfigAuthoring or similar singleton).
/// Only one instance should exist per scene.
/// </summary>
public class BulletPoolConfigAuthoring : MonoBehaviour
{
    [Header("Bullet Pool Configuration")]
    [Tooltip("Number of bullets to pre-spawn at initialization")]
    [SerializeField] private int initialPoolSize = 300;
    
    [Tooltip("Maximum number of bullets that can exist in the pool")]
    [SerializeField] private int maxPoolSize = 600;
    
    public class Baker : Baker<BulletPoolConfigAuthoring>
    {
        public override void Bake(BulletPoolConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new BulletPoolConfig
            {
                initialPoolSize = authoring.initialPoolSize,
                maxPoolSize = authoring.maxPoolSize,
                currentPoolCount = 0 // Will be set at runtime
            });
        }
    }
}

