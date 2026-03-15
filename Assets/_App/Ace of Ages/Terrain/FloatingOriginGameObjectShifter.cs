using LiquidForce;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Shifts GameObjects (like XR Origin) synchronously when the floating origin system shifts ECS entities.
/// Prevents visual artifacts by ensuring terrain and player rig shift in the same frame.
/// </summary>
public class FloatingOriginGameObjectShifter : MonoBehaviour
{
    [Header("GameObject References")]
    [Tooltip("Transforms to shift when origin shifts. If empty, will use DeviceTracking.Instance.TrackingOrigin")]
    [SerializeField] private Transform[] transformsToShift;

    [Header("Options")]
    [Tooltip("If true, calls DeviceTracking.Instance.UpdateImmediate() after shift to snap UI/camera followers")]
    [SerializeField] private bool updateDeviceTrackingImmediate = true;

    [Tooltip("Enable debug logging when origin shifts occur")]
    [SerializeField] private bool debugLog = false;

    private void OnEnable()
    {
        // Subscribe to the floating origin shift event
        FloatingOriginEvents.OnOriginShifted += OnOriginShifted;

        // If no transforms specified, try to use DeviceTracking singleton
        if (transformsToShift == null || transformsToShift.Length == 0)
        {
            if (DeviceTracking.Instance != null && DeviceTracking.Instance.TrackingOrigin != null)
            {
                transformsToShift = new Transform[] { DeviceTracking.Instance.TrackingOrigin };
                if (debugLog)
                {
                    Debug.Log($"FloatingOriginGameObjectShifter: Auto-configured to shift DeviceTracking.Instance.TrackingOrigin ({DeviceTracking.Instance.TrackingOrigin.name})");
                }
            }
            else
            {
                Debug.LogWarning("FloatingOriginGameObjectShifter: No transforms specified and DeviceTracking.Instance.TrackingOrigin is not available!");
            }
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from the event
        FloatingOriginEvents.OnOriginShifted -= OnOriginShifted;
    }

    /// <summary>
    /// Callback invoked when the floating origin system shifts the world.
    /// Applies the negative offset to configured GameObjects.
    /// </summary>
    /// <param name="offset">The offset that was applied to ECS entities</param>
    private void OnOriginShifted(float3 offset)
    {
        if (transformsToShift == null || transformsToShift.Length == 0)
        {
            Debug.LogWarning("FloatingOriginGameObjectShifter: No transforms to shift!");
            return;
        }

        // Convert float3 to Vector3
        Vector3 shiftVector = new Vector3(offset.x, offset.y, offset.z);

        if (debugLog)
        {
            Debug.Log($"FloatingOriginGameObjectShifter: Shifting {transformsToShift.Length} GameObject(s) by -{shiftVector}");
        }

        // Shift all configured transforms
        foreach (var transform in transformsToShift)
        {
            if (transform != null)
            {
                // Subtract the offset to move GameObjects back toward origin (same as ECS entities)
                transform.position -= shiftVector;

                if (debugLog)
                {
                    Debug.Log($"FloatingOriginGameObjectShifter: Shifted {transform.name} to {transform.position}");
                }
            }
            else
            {
                Debug.LogWarning("FloatingOriginGameObjectShifter: Found null transform reference!");
            }
        }

        // Update DeviceTracking to snap followers immediately (prevents smooth lerp after shift)
        if (updateDeviceTrackingImmediate && DeviceTracking.Instance != null)
        {
            DeviceTracking.Instance.UpdateImmediate();

            if (debugLog)
            {
                Debug.Log("FloatingOriginGameObjectShifter: Called DeviceTracking.Instance.UpdateImmediate()");
            }
        }
    }
}



