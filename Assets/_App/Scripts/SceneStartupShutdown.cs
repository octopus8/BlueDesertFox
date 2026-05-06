using System;
using System.Collections;
using LiquidForce;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;


/// <summary>
/// This component is responsible for setting up the scene when it starts. It does the following:
/// - Sets the tracking origin to the specified start transform.
/// - Does a fade in effect.
/// - Shows or hides the UI based on the `showUIOnStart` field.
/// - Loads the specified subscenes.
/// - Unloads the subscenes when the scene is destroyed.
/// </summary>
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
        
        // Do fade in.
        StartCoroutine(FadeIn());
        
        // Show or hide the UI.
        if (showUIOnStart)
        {
            ui.Show();
        } else {
            ui?.Hide();
        }

        // Load subscenes.
        foreach (SubScene subScene in subScenes)
        {
            if (subScene != null)
            {
                SubSceneLoader.Instance.LoadScene(subScene.SceneGUID);
            }
        }
    }

    private void OnDestroy()
    {
        // Unload subscenes.
        foreach (SubScene subScene in subScenes)
        {
            if (subScene != null)
            {
                SceneSystem.UnloadScene(World.DefaultGameObjectInjectionWorld.Unmanaged, subScene.SceneGUID);
            }
        }
    }


    IEnumerator FadeIn()
    {
        yield return new WaitForSeconds(delayBeforeFadeInSeconds);
        yield return CameraFader.Instance.FadeCameraIn(fadeInDurationSeconds);
    }
}
