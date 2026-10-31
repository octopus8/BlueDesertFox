using System.Collections.Generic;
using LiquidForce;
using Unity.Entities;
using Unity.Scenes;
#if UNITY_EDITOR
using Unity.XR.CoreUtils;
#endif
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

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
    const float MinPlanarDirectionSq = 1e-6f;
    const float TeleportSnapDistanceSq = 0.25f; // 0.5m — snap head followers after large follower jumps
#if UNITY_EDITOR
    const float UntrackedHeadHeight = 0.05f;
    const float DefaultEditorEyeHeight = 1.6f;
#endif

    static readonly List<XRInputSubsystem> s_InputSubsystems = new List<XRInputSubsystem>();

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

    [Header("Camera Anchoring")]
    [Tooltip("When enabled, keeps the tracked camera's X/Z aligned over the target by offsetting the follower root.")]
    [SerializeField] private bool alignTrackedCameraXZToTarget;

    [Tooltip("Optional camera transform to anchor over the target. If empty, a camera under the follower is auto-resolved.")]
    [SerializeField] private Transform trackedCamera;

    [Tooltip("On first successful follow, offset the tracked camera parent (Camera Offset) so headset XZ and yaw match the follower. Same visual effect as a Meta menu-button recenter; OpenXR/Quest apps cannot trigger that system recenter via API.")]
    [SerializeField] private bool recenterTrackedPoseOnStart = true;

    [Tooltip("Match the target object's rotation.")]
    [SerializeField] private bool followRotation;

    [Tooltip("Rotate the follower to face the target's movement direction (yaw only).")]
    [SerializeField] private bool followDirection;

    [Tooltip("Rotation smoothing duration when Follow Rotation is enabled. 0 = instant snap.")]
    [SerializeField] private float rotationSmoothTime;

    [Tooltip("Rotation smoothing duration when Follow Direction is enabled. 0 = instant snap.")]
    [SerializeField] private float directionSmoothTime;

#if UNITY_EDITOR
    [Header("Editor Camera")]
    [Tooltip("In Editor play mode, raise the camera to eye height when no headset is reporting a head height. Device builds and a tracked headset are left unchanged.")]
    [SerializeField] private bool applyEditorEyeHeight;

    [Tooltip("Meters above the board. 1.6 is a typical adult standing eye height.")]
    [SerializeField] private float editorEyeHeight = DefaultEditorEyeHeight;

    [Tooltip("In Editor play mode, hide the robot hands and hand-tracking meshes while no headset is tracked.")]
    [SerializeField] private bool hideHandsInEditor;

    [Tooltip("In Editor play mode, pitch the camera down around local X while no headset is tracked.")]
    [SerializeField] private bool applyEditorCameraPitch;

    [Tooltip("Degrees of downward local X pitch. Positive looks down.")]
    [SerializeField] private float editorCameraPitch = 20f;

    Renderer[] _editorHandRenderers;
    bool[] _editorHandRendererWasEnabled;
    bool _editorHandsCached;
    bool _editorHandsHidden;
#endif

    private bool _snapOnNextUpdate = true;
    private bool _pendingTrackedPoseRecenter;
    private bool _loggedWaitingForSubScene;
    private bool _loggedMissingTrackedCamera;
    private Vector3 _previousTargetPosition;
    private bool _hasPreviousTargetPosition;
    /// <summary>
    /// True after the first successful follow update and any pending start recenter has finished.
    /// Suppresses Quest/OpenXR trackingOriginUpdated spam during scene load.
    /// </summary>
    private bool _acceptTrackingOriginRecenter;
    private bool _subscribedTrackingOriginUpdated;

    private void OnEnable()
    {
        _snapOnNextUpdate = true;
        _pendingTrackedPoseRecenter = recenterTrackedPoseOnStart;
        _loggedWaitingForSubScene = false;
        _loggedMissingTrackedCamera = false;
        _hasPreviousTargetPosition = false;
        _acceptTrackingOriginRecenter = false;
        _subscribedTrackingOriginUpdated = false;

        // Static bridge outlives scene reloads (ECS Default World persists). Drop any
        // previous-session pose before the first LateUpdate can consume it.
        PlayerFollowObjectPoseBridge.Clear();

        TrySubscribeTrackingOriginUpdated();
#if UNITY_EDITOR
        Application.onBeforeRender += ApplyEditorPlayModeAdjustments;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        Application.onBeforeRender -= ApplyEditorPlayModeAdjustments;
#endif
        UnsubscribeTrackingOriginUpdated();
        _acceptTrackingOriginRecenter = false;
        _subscribedTrackingOriginUpdated = false;
        PlayerFollowObjectPoseBridge.Clear();
    }

    private void LateUpdate()
    {
        if (!_subscribedTrackingOriginUpdated)
            TrySubscribeTrackingOriginUpdated();

        if (follower == null)
        {
            Debug.LogWarning("[TransformFollowTarget] Follower transform is not assigned.", this);
            return;
        }

        ApplyEditorPlayModeAdjustments();

        if (!TryGetTargetPose(out Vector3 targetPosition, out Quaternion targetRotation))
            return;

        Vector3 targetVelocity = Vector3.zero;
        if (_hasPreviousTargetPosition)
            targetVelocity = targetPosition - _previousTargetPosition;

        _previousTargetPosition = targetPosition;
        _hasPreviousTargetPosition = true;

        targetPosition += positionOffset;

        if (alignTrackedCameraXZToTarget && TryGetTrackedCameraOffset(out Vector3 cameraOffsetFromFollower))
        {
            // Keep the camera centered over the target in XZ while preserving existing Y behavior.
            targetPosition.x -= cameraOffsetFromFollower.x;
            targetPosition.z -= cameraOffsetFromFollower.z;
        }

        bool didInitialSnap = _snapOnNextUpdate;
        bool wasPendingRecenter = _pendingTrackedPoseRecenter;
        Vector3 previousFollowerPosition = follower.position;

        follower.position = targetPosition;

        ApplyFollowerRotation(targetRotation, targetVelocity, _snapOnNextUpdate);

        _snapOnNextUpdate = false;

        if (_pendingTrackedPoseRecenter)
            TryRecenterTrackedPose();

        // After the first successful follow (and any start recenter), OS tracking-origin
        // updates may re-request a Camera Offset recenter.
        if (!_acceptTrackingOriginRecenter && !_pendingTrackedPoseRecenter)
            _acceptTrackingOriginRecenter = true;

        // Snap head followers after XR Origin / Camera Offset teleports so they do not lerp
        // from a stale or pre-teleport pose (e.g. leftover static bridge data on scene reload).
        float followerDeltaSq = (follower.position - previousFollowerPosition).sqrMagnitude;
        if (didInitialSnap ||
            (wasPendingRecenter && !_pendingTrackedPoseRecenter) ||
            followerDeltaSq >= TeleportSnapDistanceSq)
        {
            DeviceTracking.Instance?.UpdateImmediate();
        }
    }

    private void ApplyEditorPlayModeAdjustments()
    {
#if UNITY_EDITOR
        ApplyEditorEyeHeight();
        ApplyEditorCameraPitch();
        ApplyEditorHandVisibility();
#endif
    }

    /// <summary>
    /// Floor tracking leaves the camera on the board in the Editor, because no headset reports eye height.
    /// Raises the XR Origin camera-floor offset once a tracked head height is absent. A real headset already
    /// includes that height, and player builds never run this.
    /// </summary>
    private void ApplyEditorEyeHeight()
    {
#if UNITY_EDITOR
        if (!applyEditorEyeHeight || editorEyeHeight <= 0f || follower == null)
            return;

        XROrigin origin = follower.GetComponent<XROrigin>();
        if (origin == null || origin.Camera == null || origin.CameraFloorOffsetObject == null)
            return;

        if (origin.CameraInOriginSpaceHeight > UntrackedHeadHeight)
            return;

        Transform offsetTransform = origin.CameraFloorOffsetObject.transform;
        Vector3 localPosition = offsetTransform.localPosition;
        if (Mathf.Abs(localPosition.y - editorEyeHeight) <= 0.001f)
            return;

        localPosition.y = editorEyeHeight;
        offsetTransform.localPosition = localPosition;
#endif
    }

    /// <summary>
    /// Untracked editor play leaves the camera looking along the board. Pitches it down around local X
    /// without writing yaw or roll, so the view drops and head-tilt steering stays on the arrow keys.
    /// </summary>
    private void ApplyEditorCameraPitch()
    {
#if UNITY_EDITOR
        if (!applyEditorCameraPitch || follower == null || EditorHeadIsTracked())
            return;

        XROrigin origin = follower.GetComponent<XROrigin>();
        if (origin == null || origin.Camera == null)
            return;

        Transform cameraTransform = origin.Camera.transform;
        Vector3 localEuler = cameraTransform.localEulerAngles;
        if (Mathf.Abs(Mathf.DeltaAngle(localEuler.x, editorCameraPitch)) <= 0.05f)
            return;

        localEuler.x = editorCameraPitch;
        cameraTransform.localEulerAngles = localEuler;
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// Untracked editor play leaves the robot hands in front of the camera. Disables those renderers
    /// until a headset reports a tracked head.
    /// </summary>
    private void ApplyEditorHandVisibility()
    {
        if (!hideHandsInEditor || follower == null)
            return;

        if (!_editorHandsCached)
            CacheEditorHandRenderers();

        bool hide = !EditorHeadIsTracked();
        if (hide == _editorHandsHidden)
        {
            if (!hide)
                return;

            for (int i = 0; i < _editorHandRenderers.Length; i++)
            {
                Renderer renderer = _editorHandRenderers[i];
                if (renderer != null && renderer.enabled)
                    renderer.enabled = false;
            }

            return;
        }

        for (int i = 0; i < _editorHandRenderers.Length; i++)
        {
            Renderer renderer = _editorHandRenderers[i];
            if (renderer == null)
                continue;

            if (hide)
                renderer.enabled = false;
            else
                renderer.enabled = _editorHandRendererWasEnabled[i];
        }

        _editorHandsHidden = hide;
    }

    private void CacheEditorHandRenderers()
    {
        var renderers = new List<Renderer>();
        Transform[] transforms = follower.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform child = transforms[i];
            if (!IsEditorHandRoot(child.name))
                continue;

            Renderer[] childRenderers = child.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < childRenderers.Length; r++)
            {
                if (!renderers.Contains(childRenderers[r]))
                    renderers.Add(childRenderers[r]);
            }
        }

        _editorHandRenderers = renderers.ToArray();
        _editorHandRendererWasEnabled = new bool[_editorHandRenderers.Length];
        for (int i = 0; i < _editorHandRenderers.Length; i++)
            _editorHandRendererWasEnabled[i] = _editorHandRenderers[i] != null && _editorHandRenderers[i].enabled;

        _editorHandsCached = true;
        _editorHandsHidden = false;
    }

    private static bool IsEditorHandRoot(string objectName)
    {
        return objectName.StartsWith("RobotHand")
            || objectName.Contains("Hand Tracking")
            || objectName == "Left Hand"
            || objectName == "Right Hand";
    }

    private static bool EditorHeadIsTracked()
    {
        InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!head.isValid)
            return false;

        if (!head.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) || !isTracked)
            return false;

        return !head.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position)
            || position.sqrMagnitude > 0.0001f;
    }
#endif

    public void SetAlignTrackedCameraXZToTarget(bool value)
    {
        alignTrackedCameraXZToTarget = value;
    }

    /// <summary>
    /// Queues an app-level tracked-pose recenter on the next successful follow update.
    /// </summary>
    public void RequestTrackedPoseRecenter()
    {
        _pendingTrackedPoseRecenter = true;
    }

    private void TrySubscribeTrackingOriginUpdated()
    {
        if (_subscribedTrackingOriginUpdated)
            return;

        s_InputSubsystems.Clear();
        SubsystemManager.GetSubsystems(s_InputSubsystems);
        if (s_InputSubsystems.Count == 0)
            return;

        for (int i = 0; i < s_InputSubsystems.Count; i++)
        {
            XRInputSubsystem subsystem = s_InputSubsystems[i];
            if (subsystem != null)
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
        }

        _subscribedTrackingOriginUpdated = true;
    }

    private void UnsubscribeTrackingOriginUpdated()
    {
        if (!_subscribedTrackingOriginUpdated)
            return;

        s_InputSubsystems.Clear();
        SubsystemManager.GetSubsystems(s_InputSubsystems);
        for (int i = 0; i < s_InputSubsystems.Count; i++)
        {
            XRInputSubsystem subsystem = s_InputSubsystems[i];
            if (subsystem != null)
                subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
        }
    }

    /// <summary>
    /// Quest OS menu-button recenter updates the OpenXR tracking origin but leaves any Camera Offset
    /// compensation from <see cref="TryRecenterTrackedPose"/>. Re-apply that compensation so the
    /// headset maps back onto the follower (same pose as load-in).
    /// </summary>
    private void OnTrackingOriginUpdated(XRInputSubsystem _)
    {
        if (!_acceptTrackingOriginRecenter)
            return;

        RequestTrackedPoseRecenter();
    }

    private bool TryGetTrackedCameraOffset(out Vector3 offset)
    {
        offset = Vector3.zero;
        if (follower == null)
            return false;

        if (trackedCamera == null)
        {
            var followerCamera = follower.GetComponentInChildren<Camera>(true);
            if (followerCamera != null)
                trackedCamera = followerCamera.transform;
        }

        if (trackedCamera == null && Camera.main != null && Camera.main.transform.IsChildOf(follower))
            trackedCamera = Camera.main.transform;

        if (trackedCamera == null)
        {
            if (!_loggedMissingTrackedCamera)
            {
                Debug.LogWarning("[TransformFollowTarget] Camera anchoring is enabled, but no tracked camera was found under follower.", this);
                _loggedMissingTrackedCamera = true;
            }

            return false;
        }

        _loggedMissingTrackedCamera = false;
        offset = trackedCamera.position - follower.position;
        return true;
    }

    /// <summary>
    /// Compensates via the camera parent (Camera Offset) so current headset XZ/yaw map onto the
    /// follower. Adjusting Camera Offset persists across follower pose updates; moving the XR Origin
    /// would be overwritten each LateUpdate.
    /// </summary>
    private void TryRecenterTrackedPose()
    {
        if (!TryGetTrackedCameraOffset(out _))
            return;

        // Wait for a valid tracked head when an XR device is present; Editor/no-HMD proceeds immediately.
        var headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (headDevice.isValid &&
            headDevice.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) &&
            !isTracked)
        {
            return;
        }

        Transform cameraTransform = trackedCamera;
        Transform trackingSpace = cameraTransform.parent != null ? cameraTransform.parent : follower;

        Vector3 up = Vector3.up;
        Vector3 cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, up);
        Vector3 followerForward = Vector3.ProjectOnPlane(follower.forward, up);
        if (cameraForward.sqrMagnitude > MinPlanarDirectionSq &&
            followerForward.sqrMagnitude > MinPlanarDirectionSq)
        {
            float yawDegrees = Vector3.SignedAngle(cameraForward.normalized, followerForward.normalized, up);
            trackingSpace.RotateAround(cameraTransform.position, up, yawDegrees);
        }

        Vector3 planarOffset = cameraTransform.position - follower.position;
        planarOffset.y = 0f;
        trackingSpace.position -= planarOffset;

        _pendingTrackedPoseRecenter = false;
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

        if (!IsEntitiesSubSceneLoaded())
        {
            if (!_loggedWaitingForSubScene)
            {
                string subSceneName = entitiesSubScene != null ? entitiesSubScene.name : "Entities subscene";
                Debug.Log($"[TransformFollowTarget] Waiting for '{entitiesSubSceneTargetName}' in {subSceneName} to load.", this);
                _loggedWaitingForSubScene = true;
            }

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

    private bool IsEntitiesSubSceneLoaded()
    {
        if (entitiesSubScene == null)
            return true;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        Entity sceneEntity = SceneSystem.GetSceneEntity(world.Unmanaged, entitiesSubScene.SceneGUID);
        return sceneEntity != Entity.Null && SceneSystem.IsSceneLoaded(world.Unmanaged, sceneEntity);
    }
}
