using System.Collections;
using LiquidForce;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;


/// <summary>
/// This component is responsible for setting up the scene when it starts. It does the following:
/// - Sets the tracking origin to the specified start transform.
/// - Does a fade in effect.
/// - Shows or hides the UI based on the <see cref="showUIOnStart"/> field.
/// - Loads SubScenes that are not set to Auto Load Scene.
/// Headset recenter (menu-button equivalent) is handled by <see cref="TransformFollowTarget"/> when present.
/// </summary>
/// <remarks>
/// Do not unload SubScenes in <see cref="OnDestroy"/>. During Play Mode exit the SubScene GameObject
/// and Default World may already be tearing down; calling <see cref="SceneSystem.UnloadScene"/> then
/// races the Hierarchy's SubScene hook and can leave destroyed SubScenes in
/// <c>SubScene.AllSubScenes</c> (MissingReferenceException in the Hierarchy).
/// SubScene.OnDisable already unloads entity scene content when the component is disabled/destroyed.
/// </remarks>
public class SceneStartupShutdown : MonoBehaviour
{
    [SerializeField] private Transform startTransform;

    [SerializeField] private float delayBeforeFadeInSeconds = 0.25f;

    [SerializeField] private float fadeInDurationSeconds = 1f;

    [SerializeField] private UIManager ui;

    [SerializeField] private bool showUIOnStart = false;

    [SerializeField] private SubScene[] subScenes;


    void Start()
    {
        // Set the origin to the start transform.
        if (startTransform != null)
        {
            DeviceTracking.Instance?.TrackingOrigin.SetPositionAndRotation(startTransform.position,
                startTransform.rotation);
            DeviceTracking.Instance?.UpdateImmediate();
        }
        else
        {
            Debug.LogWarning("No start transform assigned!");
        }

        // Do not call UnityEngine.XR.InputTracking.Recenter() or XRInputSubsystem.TryRecenter() here.
        // Both are no-ops on OpenXR/Meta Quest — only the system menu-button hold recenters the
        // runtime tracking space. Escape Mountain / Ace of Ages use TransformFollowTarget's
        // recenterTrackedPoseOnStart for the equivalent app-level Camera Offset recenter.

        // Do fade in.
        StartCoroutine(FadeIn());

        // Show or hide the UI.
        if (showUIOnStart)
        {
            ui.Show();
        }
        else
        {
            ui?.Hide();
        }

        // Load SubScenes that are not already set to auto-load.
        // Prefer enabling Auto Load Scene on the SubScene component; this path is for explicit control.
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || SubSceneLoader.Instance == null)
            return;

        foreach (SubScene subScene in subScenes)
        {
            if (subScene == null || subScene.AutoLoadScene)
                continue;

            SubSceneLoader.Instance.LoadScene(subScene.SceneGUID);
        }
    }

    IEnumerator FadeIn()
    {
        yield return new WaitForSeconds(delayBeforeFadeInSeconds);
        yield return CameraFader.Instance.FadeCameraIn(fadeInDurationSeconds);
    }
}
