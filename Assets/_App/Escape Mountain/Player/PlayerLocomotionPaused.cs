using Unity.Entities;

/// <summary>
/// Singleton flag set by <see cref="MenuLocomotionPauseBridge"/> when the in-game menu is open.
/// Locomotion systems early-out while <see cref="Value"/> is true.
/// </summary>
public struct PlayerLocomotionPaused : IComponentData
{
    public bool Value;
}
