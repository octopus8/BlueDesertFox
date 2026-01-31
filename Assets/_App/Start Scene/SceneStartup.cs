using System.Collections;
using Autohand;
using LiquidForce;
using Unity.Scenes;
using UnityEngine;


namespace App.StartScene
{

    public class SceneStartup : MonoBehaviour
    {
        [SerializeField] private Transform startTransform;


        [SerializeField] private float delayBeforeFadeInSeconds = 0.25f;

        [SerializeField] private float fadeInDurationSeconds = 1f;

        [SerializeField] private UI ui;
        
        [SerializeField] private bool showUIOnStart = false;
        
        [SerializeField] private SubScene subScene;

        void Start()
        {
            DeviceTracking.Instance.TrackingOrigin.SetPositionAndRotation(startTransform.position,
                startTransform.rotation);
            StartCoroutine(FadeIn());
            if (showUIOnStart)
            {
                ui.Show();
            } else {
                ui?.Hide();
            }

            if (subScene != null)
            {
                SceneLoader.Instance.LoadScene(subScene.SceneGUID);
            }
        }


        IEnumerator FadeIn()
        {
            yield return new WaitForSeconds(delayBeforeFadeInSeconds);
            yield return CameraFader.Instance.FadeCameraIn(fadeInDurationSeconds);
            Destroy(this);
        }


    }
}

