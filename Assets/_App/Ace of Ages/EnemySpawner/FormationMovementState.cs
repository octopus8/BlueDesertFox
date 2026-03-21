using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Component that tracks the current movement phase of an enemy formation member.
/// Controls the state machine for approach → follow → exit behavior.
/// </summary>
public struct FormationMovementState : IComponentData
{
    /// <summary>Current movement phase</summary>
    public MovementPhase phase;
    
    /// <summary>Target position when approaching the spline entry point</summary>
    public float3 splineEntryPoint;
    
    /// <summary>Exit direction when leaving the spline (captured tangent at spline end)</summary>
    public float3 exitDirection;
    
    /// <summary>Distance from player at which to destroy entity (cleanup distance)</summary>
    public float despawnDistance;
    
    /// <summary>Movement speed during approach and exit phases</summary>
    public float formationSpeed;
}

/// <summary>
/// Movement phase enum for enemy formation lifecycle.
/// </summary>
public enum MovementPhase : byte
{
    /// <summary>Moving toward the spline entry point from spawn position</summary>
    ApproachingSpline = 0,
    
    /// <summary>Following the spline path with formation offsets</summary>
    FollowingSpline = 1,
    
    /// <summary>Continuing straight after reaching spline end</summary>
    LeavingSpline = 2,
    
    /// <summary>Beyond view distance, ready for cleanup</summary>
    OutOfBounds = 3
}

