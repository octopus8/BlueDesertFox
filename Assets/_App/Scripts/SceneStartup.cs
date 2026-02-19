using System.Collections;
using LiquidForce;
using Unity.Scenes;
using UnityEngine;


public class SceneStartup : MonoBehaviour
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


    IEnumerator FadeIn()
    {
        yield return new WaitForSeconds(delayBeforeFadeInSeconds);
        yield return CameraFader.Instance.FadeCameraIn(fadeInDurationSeconds);
        Destroy(gameObject);
    }
}
