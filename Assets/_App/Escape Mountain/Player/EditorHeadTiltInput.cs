#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Editor play-mode stand-in for HMD head roll. Holding one arrow key reports a fixed bank
/// so steering and the hoverboard visual match a head tilt. Does not rotate the camera.
/// </summary>
public static class EditorHeadTiltInput
{
    public const float TiltDegrees = 45f;

    /// <summary>
    /// Returns true while exactly one arrow key is held.
    /// Left is +45° (the sign <see cref="PlayerFollowObjectHeadSteeringSystem"/> turns left).
    /// Right is -45°.
    /// </summary>
    public static bool TryGetBankAngle(out float bankDegrees)
    {
        bankDegrees = 0f;
        if (!Application.isPlaying)
            return false;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        bool left = keyboard.leftArrowKey.isPressed;
        bool right = keyboard.rightArrowKey.isPressed;
        if (left == right)
            return false;

        bankDegrees = left ? TiltDegrees : -TiltDegrees;
        return true;
    }
}
#endif
