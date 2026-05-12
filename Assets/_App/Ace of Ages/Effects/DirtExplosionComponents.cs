using Unity.Entities;

/// <summary>
/// Tag component to identify dirt explosion entities.
/// </summary>
public struct DirtExplosion : IComponentData
{
}

/// <summary>
/// Component that tracks dirt explosion state for pooling and lifecycle management.
/// </summary>
public struct DirtExplosionData : IComponentData
{
    /// <summary>Time when the explosion was spawned (from Time.ElapsedTime).</summary>
    public double spawnTime;
    
    /// <summary>Whether the explosion is currently active (true) or pooled (false).</summary>
    public bool active;
}

/// <summary>
/// Singleton component that configures the dirt explosion pooling system.
/// </summary>
public struct DirtExplosionConfig : IComponentData
{
    /// <summary>Maximum number of explosions that can exist in the pool.</summary>
    public int maxPoolSize;
    
    /// <summary>Number of explosions to pre-spawn at initialization.</summary>
    public int initialPoolSize;
    
    /// <summary>How long explosions stay active before returning to pool (in seconds).</summary>
    public float lifetime;
    
    /// <summary>Current number of explosions in the pool (tracked at runtime).</summary>
    public int currentPoolCount;
}

