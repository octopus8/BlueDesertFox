using System;
using System.Collections;
using DG.Tweening;
using LiquidForce;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;


namespace App.StartScene
{
    
    public class UI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup uiContainer;

        [SerializeField] private float displaySpeed = 0.5f;
        
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                LoadAutoHandDemoScene();
            }
        }


        public void LoadAutoHandDemoScene()
        {
            Hide();
            CameraFader.Instance.FadeCameraOut(1);
            AsyncOperationHandle<SceneInstance> loadSceneHandle = Addressables.LoadSceneAsync("Assets/_App/StartScene/AutoHand Demo.unity", LoadSceneMode.Single, false);
            StartCoroutine(LoadSceneAsync(loadSceneHandle));
        }

        IEnumerator LoadSceneAsync(AsyncOperationHandle<SceneInstance> loadSceneHandle)
        {
            while (!loadSceneHandle.IsDone || !CameraFader.Instance.IsCameraFadedOut())
            {
                yield return null;
            }
            if (loadSceneHandle.Status == AsyncOperationStatus.Succeeded)
            {
                loadSceneHandle.Result.ActivateAsync();
            }
            
        }

        public void Hide()
        {
            uiContainer.transform.DOScale(new Vector3(0, 0, 1), displaySpeed).onComplete += () => uiContainer.gameObject.SetActive(false);
            uiContainer.DOFade(0, displaySpeed);
        }

        public void Show()
        {
            uiContainer.gameObject.SetActive(true);
            uiContainer.DOFade(1, displaySpeed);
            uiContainer.transform.DOScale(new Vector3(1, 1, 1), displaySpeed);
        }
    }
    
}

