using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for the BulletShooter system.
/// Attach this to the player ship GameObject to enable shooting.
/// </summary>
public class BulletShooterAuthoring : MonoBehaviour
{
    [Header("Shooting Configuration")]
    [Tooltip("Minimum time between shots in seconds (0.2 = 5 rounds/sec)")]
    [SerializeField] private float fireRate = 0.2f;
    
    [Tooltip("Speed of spawned bullets in units per second")]
    [SerializeField] private float bulletSpeed = 100f;
    
    /// <summary>Bakes the inspector fire-rate and bullet-speed values into a <see cref="BulletShooter"/> component.</summary>
    public class Baker : Baker<BulletShooterAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(BulletShooterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new BulletShooter
            {
                doShoot = false,
                fireRate = authoring.fireRate,
                bulletSpeed = authoring.bulletSpeed,
                lastFireTime = 0
            });
        }
    }
}

