using System;
using Unity.Mathematics;

/// <summary>
/// Static event manager for floating origin shifts.
/// Provides a way for GameObjects to subscribe to world origin shift events from the ECS system.
/// </summary>
public static class FloatingOriginEvents
{
    /// <summary>
    /// Event fired when the world origin shifts. Passes the offset that was applied to entities.
    /// Note: The player GameObject is shifted directly by FloatingOriginSystem before this event fires.
    /// Subscribe to this to shift non-player GameObjects (terrain decorations, particles, etc.) synchronously.
    /// </summary>
    public static event Action<float3> OnNonPlayerOriginShifted;

    /// <summary>
    /// Invoke the origin shifted event. Called by FloatingOriginSystem after shifting player GameObject.
    /// </summary>
    /// <param name="offset">The offset that was subtracted from entity positions and player GameObject</param>
    public static void InvokeNonPlayerOriginShifted(float3 offset)
    {
        OnNonPlayerOriginShifted?.Invoke(offset);
    }
    
    /// <summary>
    /// Legacy event name for backwards compatibility. Maps to OnNonPlayerOriginShifted.
    /// </summary>
    [Obsolete("Use OnNonPlayerOriginShifted instead. This event is maintained for backwards compatibility.", false)]
    public static event Action<float3> OnOriginShifted
    {
        add => OnNonPlayerOriginShifted += value;
        remove => OnNonPlayerOriginShifted -= value;
    }
    
    /// <summary>
    /// Legacy method name for backwards compatibility. Maps to InvokeNonPlayerOriginShifted.
    /// </summary>
    [Obsolete("Use InvokeNonPlayerOriginShifted instead. This method is maintained for backwards compatibility.", false)]
    public static void InvokeOriginShifted(float3 offset)
    {
        InvokeNonPlayerOriginShifted(offset);
    }
}

