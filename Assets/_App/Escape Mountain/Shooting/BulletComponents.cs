using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Tag component to identify bullet entities.
/// </summary>
public struct Bullet : IComponentData
{
}

/// <summary>
/// Component that tracks bullet state for pooling and lifecycle management.
/// </summary>
public struct BulletData : IComponentData
{
    /// <summary>World position where the bullet was spawned (used for distance calculations).</summary>
    public float3 spawnPosition;
    
    /// <summary>Time when the bullet was spawned (from Time.ElapsedTime).</summary>
    public double creationTime;
    
    /// <summary>Whether the bullet is currently active (true) or pooled (false).</summary>
    public bool active;

    /// <summary>
    /// Ballistic velocity in the scrolling-terrain sense: world linear velocity plus terrain scroll velocity
    /// at spawn (<c>PhysicsVelocity.Linear + terrainVelocity</c>). Each physics tick we set
    /// <see cref="Unity.Physics.PhysicsVelocity.Linear"/> to this minus current scroll velocity so bullets stay
    /// correct when <see cref="TerrainScrollVelocity.direction"/> changes (e.g. player turns).
    /// </summary>
    public float3 linearVelocityTerrainRelative;
}

/// <summary>
/// Component that controls shooting behavior for the player ship.
/// Triggering doShoot=true will spawn a bullet on the next frame.
/// </summary>
public struct BulletShooter : IComponentData
{
    /// <summary>Flag to trigger bullet spawn (reset to false by ResetEventsSystem).</summary>
    public bool doShoot;
    
    /// <summary>Minimum time between shots in seconds (0.2 = 5 rounds/sec).</summary>
    public float fireRate;
    
    /// <summary>Speed of spawned bullets in units per second.</summary>
    public float bulletSpeed;
    
    /// <summary>Last time a bullet was fired (from Time.ElapsedTime).</summary>
    public double lastFireTime;
}

/// <summary>
/// Singleton component that configures the bullet pooling system.
/// </summary>
public struct BulletPoolConfig : IComponentData
{
    /// <summary>Maximum number of bullets that can exist in the pool.</summary>
    public int maxPoolSize;
    
    /// <summary>Number of bullets to pre-spawn at initialization.</summary>
    public int initialPoolSize;
    
    /// <summary>Current number of bullets in the pool (tracked at runtime).</summary>
    public int currentPoolCount;
}

