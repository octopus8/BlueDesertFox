using Autohand;
using Autohand.Demo;
using UnityEngine;

/// <summary>
/// Wires AutoHand RobotHands to the XRI Hands rig at runtime. Controller follow offsets are used as a
/// fallback; <see cref="OpenXRAutoHandTracking"/> drives wrist/finger poses when XR hand tracking is active.
/// </summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class AceOfAgesAutoHandRigBootstrap : MonoBehaviour
{
    const string XrRigName = "XR Origin Hands (XR Rig)";
    const string HandTrackingParentName = "Hand Tracking";
    const string LeftHandInstanceName = "RobotHand (L)";
    const string RightHandInstanceName = "RobotHand (R)";
    const string LeftHandTrackingInstanceName = "Left Hand Tracking";
    const string RightHandTrackingInstanceName = "Right Hand Tracking";
    const string AutoHandPlayerInstanceName = "Auto Hand Player";

    [SerializeField] private GameObject robotHandLeftPrefab;
    [SerializeField] private GameObject robotHandRightPrefab;
    [SerializeField] private GameObject autoHandPlayerPrefab;
    [SerializeField] private GameObject leftHandTrackingPrefab;
    [SerializeField] private GameObject rightHandTrackingPrefab;

    [SerializeField] private float handMaxFollowDistance = 1.5f;
    [Tooltip("Hide the XR Hands sample skinned mesh so only RobotHand visuals are shown.")]
    [SerializeField] private bool hideHandTrackingVisuals = true;

    private bool _initialized;

    private void Awake()
    {
        if (_initialized)
            return;

        var xrOrigin = FindXrOrigin();
        if (xrOrigin == null)
        {
            Debug.LogError("[AceOfAgesAutoHandRigBootstrap] XR Origin Hands rig not found.", this);
            return;
        }

        DisableXriConflicts(xrOrigin.transform);

        var cameraOffset = FindChildRecursive(xrOrigin.transform, "Camera Offset");
        var leftController = FindChildRecursive(xrOrigin.transform, "Left Controller");
        var rightController = FindChildRecursive(xrOrigin.transform, "Right Controller");
        var mainCamera = FindChildRecursive(xrOrigin.transform, "Main Camera");

        if (cameraOffset == null || leftController == null || rightController == null || mainCamera == null)
        {
            Debug.LogError("[AceOfAgesAutoHandRigBootstrap] XR rig is missing Camera Offset or controllers.", this);
            return;
        }

        var leftOffset = GetOrCreateFollowOffset(leftController, "AutoHand Follow Offset (L)",
            new Vector3(-0.05f, 0f, 0f), Quaternion.Euler(16f, -8f, 8f));
        var rightOffset = GetOrCreateFollowOffset(rightController, "AutoHand Follow Offset (R)",
            new Vector3(0.05f, 0f, 0f), Quaternion.Euler(16f, 8f, 8f));

        var leftHand = GetOrCreateRobotHand(cameraOffset, robotHandLeftPrefab, LeftHandInstanceName);
        var rightHand = GetOrCreateRobotHand(cameraOffset, robotHandRightPrefab, RightHandInstanceName);

        WireHand(leftHand, leftOffset, isLeft: true);
        WireHand(rightHand, rightOffset, isLeft: false);

        WireHandTracking(xrOrigin.transform, leftHand, rightHand);

        GetOrCreateAutoHandPlayer(xrOrigin.transform, autoHandPlayerPrefab);
        ConfigureAutoHandPlayer(xrOrigin.transform, leftHand, rightHand, mainCamera, cameraOffset);

        _initialized = true;
    }

    void WireHandTracking(Transform xrOrigin, GameObject leftHand, GameObject rightHand)
    {
        if (leftHandTrackingPrefab == null || rightHandTrackingPrefab == null)
        {
            Debug.LogWarning("[AceOfAgesAutoHandRigBootstrap] Hand tracking prefabs are not assigned. " +
                "Controllers will work, but XR hand tracking will not drive AutoHand.", this);
            return;
        }

        var handTrackingParent = GetOrCreateChild(xrOrigin, HandTrackingParentName);

        WireHandTrackingSide(handTrackingParent, leftHand, leftHandTrackingPrefab, LeftHandTrackingInstanceName, isLeft: true);
        WireHandTrackingSide(handTrackingParent, rightHand, rightHandTrackingPrefab, RightHandTrackingInstanceName, isLeft: false);
    }

    void WireHandTrackingSide(Transform parent, GameObject robotHand, GameObject trackingPrefab, string instanceName, bool isLeft)
    {
        if (robotHand == null || trackingPrefab == null)
            return;

        var hand = robotHand.GetComponentInChildren<Hand>(true);
        if (hand == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandRigBootstrap] No Hand on {robotHand.name} for hand tracking.", robotHand);
            return;
        }

        var trackingObject = GetOrCreateTrackingObject(parent, trackingPrefab, instanceName);
        trackingObject.SetActive(false);

        var controllerLink = robotHand.GetComponentInChildren<OpenXRHandControllerLink>(true);
        EnsureHandTracking(trackingObject, hand, controllerLink, isLeft);

        if (hideHandTrackingVisuals)
            HideHandTrackingVisuals(trackingObject);

        trackingObject.SetActive(true);
    }

    /// <summary>
    /// Ensures the XR Hands sample tracking object carries the AutoHand tracking bridge components and
    /// configures them for the given hand. The XR Hands 1.8.0 sample prefabs ship without these
    /// components (the customized 1.7.1 prefabs that had them were overwritten by the sample upgrade),
    /// so they are added and configured in code. This keeps optical hand tracking working even after a
    /// future XR Hands sample reimport wipes the prefabs again.
    /// </summary>
    public static void EnsureHandTracking(GameObject trackingObject, Hand hand,
        OpenXRHandControllerLink controllerLink, bool isLeft)
    {
        if (trackingObject == null || hand == null)
            return;

        var handTracking = trackingObject.GetComponent<OpenXRAutoHandTracking>();
        if (handTracking == null)
            handTracking = trackingObject.AddComponent<OpenXRAutoHandTracking>();

        handTracking.hand = hand;
        handTracking.controllerLink = controllerLink;
        handTracking.upAxis = isLeft ? AxisEnum.left : AxisEnum.right;
        handTracking.forwardAxis = AxisEnum.up;
        handTracking.handOffset = new Vector3(isLeft ? -0.01f : 0.01f, 0f, 0.1f);
        handTracking.handRotationOffset = new Vector3(0f, 0f, isLeft ? -90f : 90f);
        handTracking.handPoseSmoothingSpeed = 0.03f;
        handTracking.followPositionSmoothing = 0.333333f;
        handTracking.followRotationSmoothing = 0.5f;

        var grabber = trackingObject.GetComponent<OpenXRAutoHandTrackingGrabber>();
        if (grabber == null)
            grabber = trackingObject.AddComponent<OpenXRAutoHandTrackingGrabber>();

        grabber.handTracker = handTracking;
        grabber.allowHeldFingerMovement = true;
        grabber.releaseGrabDelay = 0.35f;
        grabber.fingerTipRadiusMultiplier = 2f;
        grabber.useFingerTouchGrabbing = true;
        grabber.useFingerTouchReleasing = true;
        grabber.useTouchHoldingWithHeldPose = true;
        grabber.usePoseGrabbing = true;
        grabber.minPoseGrabCloseness = 0.25f;
        grabber.maxPoseGrabCloseness = 0.9f;
        grabber.minDeltaPoseActivation = 0.01f;
        grabber.maxDeltaPoseActivation = 0.035f;
        grabber.usePoseRelease = true;
        grabber.minPoseReleaseOpenness = 0f;
        grabber.maxPoseReleaseOpenness = 0.4f;
        grabber.requiredDeltaPoseReleaseOpenness = 0.15f;
        grabber.usePoseSqueezing = true;
        grabber.squeezeUnsqueezeDelay = 0.5f;
        grabber.squeezePoseSensitvityMultiplier = 1.6f;

        var gestureTracker = trackingObject.GetComponent<HandFingerGestureTracker>();
        if (gestureTracker == null)
            gestureTracker = trackingObject.AddComponent<HandFingerGestureTracker>();

        gestureTracker.handTracking = handTracking;
        gestureTracker.fingerTipScale = 2f;
        gestureTracker.fingerTouchEventDelay = 0.05f;
    }

    static GameObject GetOrCreateTrackingObject(Transform parent, GameObject prefab, string instanceName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == instanceName)
                return child.gameObject;
        }

        var instance = Instantiate(prefab, parent);
        instance.name = instanceName;
        return instance;
    }

    static void HideHandTrackingVisuals(GameObject trackingObject)
    {
        foreach (Transform child in trackingObject.transform)
        {
            if (child.name.Contains("Visual"))
                child.gameObject.SetActive(false);
        }

        foreach (var renderer in trackingObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderer.enabled = false;
    }

    static Transform GetOrCreateChild(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null)
            return existing;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject FindXrOrigin()
    {
        var xrOrigin = GameObject.Find(XrRigName);
        if (xrOrigin != null)
            return xrOrigin;

        foreach (var deviceTracking in FindObjectsByType<LiquidForce.DeviceTracking>(FindObjectsSortMode.None))
        {
            if (deviceTracking.TrackingOrigin != null)
                return deviceTracking.TrackingOrigin.gameObject;
        }

        return null;
    }

    static void DisableXriConflicts(Transform xrOrigin)
    {
        SetActiveChild(xrOrigin, "Locomotion", false);
        SetActiveChild(xrOrigin, "Left Hand", false);
        SetActiveChild(xrOrigin, "Right Hand", false);
        SetActiveChild(xrOrigin, "Hand Visualizer", false);
        DisableHandVisualizerMeshes(xrOrigin);
    }

    static void DisableHandVisualizerMeshes(Transform xrOrigin)
    {
        foreach (var behaviour in xrOrigin.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "HandVisualizer")
                continue;

            var drawMeshesField = behaviour.GetType().GetField("m_DrawMeshes");
            drawMeshesField?.SetValue(behaviour, false);
            behaviour.enabled = false;
        }
    }

    static void SetActiveChild(Transform root, string childName, bool active)
    {
        var child = FindChildRecursive(root, childName);
        if (child != null)
            child.gameObject.SetActive(active);
    }

    static Transform GetOrCreateFollowOffset(Transform parent, string name, Vector3 localPos, Quaternion localRot)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            existing.localPosition = localPos;
            existing.localRotation = localRot;
            return existing;
        }

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        return go.transform;
    }

    static GameObject GetOrCreateRobotHand(Transform parent, GameObject prefab, string instanceName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == instanceName)
                return child.gameObject;
        }

        if (prefab == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandRigBootstrap] Missing prefab for {instanceName}.", parent);
            return null;
        }

        var instance = Instantiate(prefab, parent);
        instance.name = instanceName;
        return instance;
    }

    void WireHand(GameObject handObject, Transform followTarget, bool isLeft)
    {
        if (handObject == null || followTarget == null)
            return;

        var hand = handObject.GetComponentInChildren<Hand>(true);
        if (hand == null)
        {
            Debug.LogError($"[AceOfAgesAutoHandRigBootstrap] No Hand component on {handObject.name}.", handObject);
            return;
        }

        hand.left = isLeft;
        hand.follow = followTarget;
        SetLayerRecursively(handObject, LayerMask.NameToLayer("Hand"));

        var handFollow = handObject.GetComponentInChildren<HandFollow>(true);
        if (handFollow != null)
            handFollow.maxFollowDistance = handMaxFollowDistance;
    }

    static void GetOrCreateAutoHandPlayer(Transform xrOrigin, GameObject autoHandPlayerPrefab)
    {
        foreach (Transform child in xrOrigin)
        {
            if (child.name == AutoHandPlayerInstanceName)
                return;
        }

        if (autoHandPlayerPrefab == null)
            return;

        var instance = Instantiate(autoHandPlayerPrefab, xrOrigin);
        instance.name = AutoHandPlayerInstanceName;
    }

    static void ConfigureAutoHandPlayer(Transform xrOrigin, GameObject leftHand, GameObject rightHand,
        Transform mainCamera, Transform cameraOffset)
    {
        AutoHandPlayer player = null;
        foreach (Transform child in xrOrigin)
        {
            if (child.name != AutoHandPlayerInstanceName)
                continue;

            player = child.GetComponent<AutoHandPlayer>();
            break;
        }

        if (player == null)
            return;

        player.useMovement = false;
        player.useGrounding = false;
        player.headCamera = mainCamera.GetComponent<Camera>();
        player.forwardFollow = mainCamera;
        player.trackingContainer = cameraOffset;
        player.handLeft = leftHand?.GetComponentInChildren<Hand>(true);
        player.handRight = rightHand?.GetComponentInChildren<Hand>(true);

        foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Name == "OpenXRHandPlayerControllerLink")
                behaviour.enabled = false;
        }
    }

    static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0)
            return;

        foreach (var transform in go.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = layer;
    }
}
