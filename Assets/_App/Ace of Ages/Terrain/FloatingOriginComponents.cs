using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Singleton component that stores the accumulated world offset.
/// Uses double3 for high precision to prevent terrain noise from shifting when the origin resets.
/// </summary>
public struct WorldOriginOffset : IComponentData
{
    /// <summary>
    /// The cumulative offset that has been subtracted from all entities.
    /// This is added to grid coordinates when sampling noise to maintain terrain consistency.
    /// </summary>
    public double3 accumulatedOffset;
}

/// <summary>
/// Singleton configuration component for floating origin system.
/// </summary>
public struct FloatingOriginConfig : IComponentData
{
    /// <summary>
    /// Distance from origin (0,0,0) that triggers a world shift (e.g., 2000 meters).
    /// </summary>
    public float shiftThreshold;
    
    /// <summary>
    /// If true, the floating origin system is active.
    /// </summary>
    public bool enabled;
}

/// <summary>
/// Tag component added to entities that should be affected by floating origin shifts.
/// When the world origin shifts, all entities with this tag have their positions adjusted.
/// </summary>
public struct FloatingOriginEnabled : IComponentData
{
}

