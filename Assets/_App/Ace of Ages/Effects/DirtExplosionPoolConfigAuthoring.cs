using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for dirt explosion pool configuration.
/// Place this on a GameObject in the scene (typically with TerrainConfigAuthoring or similar singleton).
/// Only one instance should exist per scene.
/// </summary>
public class DirtExplosionPoolConfigAuthoring : MonoBehaviour
{
    [Header("Dirt Explosion Pool Configuration")]
    [Tooltip("Number of explosions to pre-spawn at initialization")]
    [SerializeField] private int initialPoolSize = 20;
    
    [Tooltip("Maximum number of explosions that can exist in the pool")]
    [SerializeField] private int maxPoolSize = 50;
    
    [Header("Explosion Lifetime")]
    [Tooltip("How long explosions stay active before returning to pool (in seconds)")]
    [SerializeField] private float lifetime = 2.5f;
    
    public class Baker : Baker<DirtExplosionPoolConfigAuthoring>
    {
        public override void Bake(DirtExplosionPoolConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new DirtExplosionConfig
            {
                initialPoolSize = authoring.initialPoolSize,
                maxPoolSize = authoring.maxPoolSize,
                lifetime = authoring.lifetime,
                currentPoolCount = 0 // Will be set at runtime
            });
        }
    }
}

