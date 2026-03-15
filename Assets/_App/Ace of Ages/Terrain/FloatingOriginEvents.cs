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
    /// Subscribe to this to shift GameObjects (e.g., XR Origin) synchronously with ECS entities.
    /// </summary>
    public static event Action<float3> OnOriginShifted;

    /// <summary>
    /// Invoke the origin shifted event. Called by FloatingOriginSystem.
    /// </summary>
    /// <param name="offset">The offset that was subtracted from entity positions</param>
    public static void InvokeOriginShifted(float3 offset)
    {
        OnOriginShifted?.Invoke(offset);
    }
}

