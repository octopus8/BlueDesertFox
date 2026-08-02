using Autohand;
using Autohand.Demo;
using UnityEngine;

/// <summary>
/// Editor/runtime helper that adds AutoHand optical hand-tracking bridge components to XR Hands
/// sample tracking objects. The XR Hands 1.8.0 sample prefabs ship without these components, so
/// bake them onto scene instances (or call this from an editor setup tool) rather than relying on
/// Awake-time wiring.
/// </summary>
public static class AutoHandTrackingSetup
{
    /// <summary>
    /// Ensures the XR Hands sample tracking object carries the AutoHand tracking bridge components and
    /// configures them for the given hand.
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
}
