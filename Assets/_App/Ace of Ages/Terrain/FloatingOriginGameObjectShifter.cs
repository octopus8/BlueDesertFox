using LiquidForce;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Shifts non-player GameObjects (terrain decorations, particle systems, etc.) synchronously 
/// when the floating origin system shifts ECS entities.
/// Note: The player GameObject is shifted directly by FloatingOriginSystem to prevent double-shifting.
/// </summary>
public class FloatingOriginGameObjectShifter : MonoBehaviour
{
    [Header("GameObject References")]
    [Tooltip("Transforms to shift when origin shifts (excludes player transform automatically). Leave empty to skip.")]
    [SerializeField] private Transform[] transformsToShift;

    [Header("Options")]
    [Tooltip("If true, calls DeviceTracking.Instance.UpdateImmediate() after shift to snap UI/camera followers")]
    [SerializeField] private bool updateDeviceTrackingImmediate = true;

    [Tooltip("Enable debug logging when origin shifts occur")]
    [SerializeField] private bool debugLog = false;

    private Transform _playerTransform;

    private void OnEnable()
    {
        // Subscribe to the floating origin shift event
        FloatingOriginEvents.OnNonPlayerOriginShifted += OnOriginShifted;

        // Get reference to player transform to exclude it from shifting
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null)
        {
            var em = world.EntityManager;
            var query = em.CreateEntityQuery(typeof(PlayerTransformReference));
            if (query.CalculateEntityCount() > 0)
            {
                var entity = query.GetSingletonEntity();
                var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
                _playerTransform = playerRef?.playerTransform;
                
                if (debugLog && _playerTransform != null)
                {
                    Debug.Log($"FloatingOriginGameObjectShifter: Player transform detected ({_playerTransform.name}), will be excluded from shifting");
                }
            }
            query.Dispose();
        }
    }

    private void OnDisable()
    {
        // Unsubscribe from the event
        FloatingOriginEvents.OnNonPlayerOriginShifted -= OnOriginShifted;
    }

    /// <summary>
    /// Callback invoked when the floating origin system shifts the world.
    /// Applies the negative offset to configured non-player GameObjects.
    /// </summary>
    /// <param name="offset">The offset that was applied to ECS entities and player</param>
    private void OnOriginShifted(float3 offset)
    {
        if (transformsToShift == null || transformsToShift.Length == 0)
        {
            if (debugLog)
            {
                Debug.Log("FloatingOriginGameObjectShifter: No transforms configured to shift");
            }
        }
        else
        {
            // Convert float3 to Vector3
            Vector3 shiftVector = new Vector3(offset.x, offset.y, offset.z);

            if (debugLog)
            {
                Debug.Log($"FloatingOriginGameObjectShifter: Shifting {transformsToShift.Length} GameObject(s) by -{shiftVector}");
            }

            // Shift all configured transforms (excluding player)
            foreach (var transform in transformsToShift)
            {
                if (transform != null)
                {
                    // Skip player transform (already shifted by FloatingOriginSystem)
                    if (transform == _playerTransform)
                    {
                        if (debugLog)
                        {
                            Debug.Log($"FloatingOriginGameObjectShifter: Skipping player transform ({transform.name}) - already shifted by FloatingOriginSystem");
                        }
                        continue;
                    }

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



