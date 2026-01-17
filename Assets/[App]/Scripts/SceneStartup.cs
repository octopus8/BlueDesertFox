using System.Collections;
using Autohand;
using LiquidForce;
using UnityEngine;

public class SceneStartup : MonoBehaviour
{
    [SerializeField] private Transform startTransform;
    
    
    [SerializeField] private float delayBeforeFadeInSeconds = 0.25f;
    
    [SerializeField] private float fadeInDurationSeconds = 1f;
    
    
    void Start()
    {
        DeviceTracking.Instance.TrackingOrigin.SetPositionAndRotation(startTransform.position, startTransform.rotation);
        StartCoroutine(FadeIn());
    }


    IEnumerator FadeIn()
    {
        yield return new WaitForSeconds(delayBeforeFadeInSeconds);
        yield return CameraFader.Instance.FadeCameraIn(fadeInDurationSeconds);
        Destroy(this);
    }

    
}
