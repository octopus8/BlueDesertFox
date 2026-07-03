using Unity.Scenes;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Moves a main-scene Transform to follow a target. Main-scene targets use a Transform reference;
/// Entities subscene targets use a name + <see cref="PlayerFollowObjectPoseBridge"/> (cross-scene
/// Transform references are not supported by Unity).
/// </summary>
[AddComponentMenu("Ace of Ages/Transform Follow Target")]
[DefaultExecutionOrder(100)]
public class TransformFollowTarget : MonoBehaviour
{
    public enum TargetScene
    {
        MainScene,
        EntitiesSubScene
    }

    const float MinDirectionSpeedSq = 0.0001f;

    [Tooltip("Transform to move (main scene, e.g. XR Origin root).")]
    [SerializeField] private Transform follower;

    [Tooltip("MainScene uses a Transform reference. EntitiesSubScene uses the named subscene object via ECS.")]
    [SerializeField] private TargetScene targetScene = TargetScene.EntitiesSubScene;

    [Tooltip("Main-scene target to follow.")]
    [FormerlySerializedAs("target")]
    [SerializeField] private Transform mainSceneTarget;

    [Tooltip("Entities subscene loaded by this scene (assign the SubScene component from the main scene hierarchy).")]
    [SerializeField] private SubScene entitiesSubScene;

    [Tooltip("Name of the target GameObject in the Entities subscene. Must have PlayerFollowObjectAuthoring.")]
    [SerializeField] private string entitiesSubSceneTargetName = "Player Follow Object";

    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Tooltip("Match the target object's rotation.")]
    [SerializeField] private bool followRotation;

    [Tooltip("Rotate the follower to face the target's movement direction (yaw only).")]
    [SerializeField] private bool followDirection;

    [Tooltip("Rotation smoothing duration when Follow Rotation is enabled. 0 = instant snap.")]
    [SerializeField] private float rotationSmoothTime;

    [Tooltip("Rotation smoothing duration when Follow Direction is enabled. 0 = instant snap.")]
    [SerializeField] private float directionSmoothTime;

    private bool _snapOnNextUpdate = true;
    private bool _loggedWaitingForSubScene;
    private Vector3 _previousTargetPosition;
    private bool _hasPreviousTargetPosition;

    private void OnEnable()
    {
        _snapOnNextUpdate = true;
        _loggedWaitingForSubScene = false;
        _hasPreviousTargetPosition = false;
    }

    private void LateUpdate()
    {
        if (follower == null)
        {
            Debug.LogWarning("[TransformFollowTarget] Follower transform is not assigned.", this);
            return;
        }

        if (!TryGetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation))
            return;

        Vector3 targetVelocity = Vector3.zero;
        if (_hasPreviousTargetPosition)
            targetVelocity = targetPosition - _previousTargetPosition;

        _previousTargetPosition = targetPosition;
        _hasPreviousTargetPosition = true;

        targetPosition += positionOffset;
        follower.position = targetPosition;

        ApplyFollowerRotation(targetRotation, targetVelocity, _snapOnNextUpdate);

        _snapOnNextUpdate = false;
    }

    private void ApplyFollowerRotation(Quaternion targetRotation, Vector3 targetVelocity, bool snapOnEnable)
    {
        if (followDirection && TryGetDirectionRotation(targetVelocity, out Quaternion directionRotation))
        {
            bool snap = snapOnEnable || directionSmoothTime <= 0f;
            float smoothFactor = GetSmoothFactor(directionSmoothTime, snap);
            follower.rotation = snap
                ? directionRotation
                : Quaternion.Slerp(follower.rotation, directionRotation, smoothFactor);
            return;
        }

        if (!followRotation)
            return;

        bool snapRotation = snapOnEnable || rotationSmoothTime <= 0f;
        float rotationFactor = GetSmoothFactor(rotationSmoothTime, snapRotation);
        follower.rotation = snapRotation
            ? targetRotation
            : Quaternion.Slerp(follower.rotation, targetRotation, rotationFactor);
    }

    private static float GetSmoothFactor(float smoothDuration, bool snap)
    {
        return snap ? 1f : Mathf.Clamp01(Time.deltaTime / smoothDuration);
    }

    private static bool TryGetDirectionRotation(Vector3 velocity, out Quaternion rotation)
    {
        var flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
        if (flatVelocity.sqrMagnitude < MinDirectionSpeedSq)
        {
            rotation = Quaternion.identity;
            return false;
        }

        rotation = Quaternion.LookRotation(flatVelocity.normalized, Vector3.up);
        return true;
    }

    private bool TryGetTargetPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (targetScene == TargetScene.EntitiesSubScene)
            return TryGetSubSceneTargetPose(out position, out rotation);

        if (mainSceneTarget == null)
        {
            Debug.LogWarning("[TransformFollowTarget] Main scene target transform is not assigned.", this);
            return false;
        }

        position = mainSceneTarget.position;
        rotation = mainSceneTarget.rotation;
        return true;
    }

    private bool TryGetSubSceneTargetPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (string.IsNullOrWhiteSpace(entitiesSubSceneTargetName))
        {
            Debug.LogWarning("[TransformFollowTarget] Entities subscene target name is not assigned.", this);
            return false;
        }

        if (PlayerFollowObjectPoseBridge.IsValid)
        {
            position = PlayerFollowObjectPoseBridge.Position;
            rotation = PlayerFollowObjectPoseBridge.Rotation;
            _loggedWaitingForSubScene = false;
            return true;
        }

        if (!_loggedWaitingForSubScene)
        {
            string subSceneName = entitiesSubScene != null ? entitiesSubScene.name : "Entities subscene";
            Debug.Log($"[TransformFollowTarget] Waiting for '{entitiesSubSceneTargetName}' in {subSceneName} to load.", this);
            _loggedWaitingForSubScene = true;
        }

        return false;
    }
}
