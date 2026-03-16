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

/// <summary>
/// Singleton managed component that holds a reference to the player GameObject's Transform.
/// This allows the terrain system to track a GameObject that exists outside the ECS subscene.
/// </summary>
public class PlayerTransformReference : IComponentData
{
    /// <summary>
    /// The Transform of the player GameObject to track for terrain centering and floating origin.
    /// </summary>
    public UnityEngine.Transform playerTransform;
}

/// <summary>
/// Component that stores search parameters for finding the player GameObject at runtime.
/// This is baked into the entity so it can find the target after subscenes load.
/// </summary>
public struct PlayerTrackingSearch : IComponentData
{
    public enum Mode : byte
    {
        FindByName = 0,
        FindByTag = 1,
        FindAutoHandPlayer = 2,
        FindMainCamera = 3
    }
    
    /// <summary>
    /// How to search for the player GameObject.
    /// </summary>
    public Mode mode;
    
    /// <summary>
    /// Search string (name or tag) - only used for FindByName and FindByTag modes.
    /// </summary>
    public Unity.Collections.FixedString128Bytes searchString;
    
    /// <summary>
    /// True if the PlayerTransformReference has been set up successfully.
    /// </summary>
    public bool initialized;
}

