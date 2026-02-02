using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;


namespace LiquidForce
{
    
    /// <summary>
    /// This component is used to load scenes, both addressable and not.
    /// </summary>
    /// <remarks>
    /// - This class is a MonoBehaviour so it can execute coroutines and easily interact with other coroutines.
    /// </remarks>
    public class SceneLoader : MonoBehaviour
    {
        
        /// <summary>
        /// Loads the specified scene.
        /// </summary>
        /// <param name="scene"></param>
        /// <param name="waitForFadeOut"></param>
        public void LoadScene(SceneListSO.SceneListScene scene, bool waitForFadeOut = false)
        {
            // If the scene is an addressable, then load it using the addressables system.
            if (scene.isAddressable)
            {
                AsyncOperationHandle<SceneInstance> loadSceneHandle = Addressables.LoadSceneAsync(scene.scene.AssetGUID, LoadSceneMode.Single, false);
                StartCoroutine(ActivateAddressableSceneOnLoad(loadSceneHandle));

            }
            
            // Otherwise, load the standard scene.
            else
            {
                var loadSceneAsync = SceneManager.LoadSceneAsync(scene.scenePath);
                if (waitForFadeOut)
                {
                    loadSceneAsync.allowSceneActivation = false;
                    StartCoroutine(ActivateStandardSceneOnLoad(loadSceneAsync));
                }
            }
        }

        
        /// <summary>
        /// Activates the loaded addressable scene upon fully loading and camera fully faded out.
        /// </summary>
        /// <param name="loadSceneHandle"></param>
        /// <returns></returns>
        private IEnumerator ActivateAddressableSceneOnLoad(AsyncOperationHandle<SceneInstance> loadSceneHandle)
        {
            // Wait for the scene to be loaded and the camera to fully fade out.
            while (!loadSceneHandle.IsDone || !CameraFader.Instance.IsCameraFadedOut())
            {
                yield return null;
            }

            // Activate the loaded scene.
            if (loadSceneHandle.Status == AsyncOperationStatus.Succeeded)
            {
                loadSceneHandle.Result.ActivateAsync();
            }
            else
            {
                CameraFader.Instance.SetCameraFadedIn();
                Debug.LogError("Could not load scene: " + loadSceneHandle.Status);
            }
        }

        
        /// <summary>
        /// Activates the loaded standard scene upon fully loading and camera fully faded out.
        /// </summary>
        /// <param name="asyncOperation"></param>
        /// <returns></returns>
        private IEnumerator ActivateStandardSceneOnLoad(AsyncOperation asyncOperation)
        {
            // Wait until done and collect progress as we go.
            while (!asyncOperation.isDone)
            {
                float loadProgress = asyncOperation.progress;

                if (loadProgress >= 0.9f)
                {
                    // Almost done.
                    break;
                }

                yield return null;
            }

            // Wait until the camera has fully faded out.
            while (!CameraFader.Instance.IsCameraFadedOut())
            {
                yield return null;
            }

            // Allow new scene to start.
            asyncOperation.allowSceneActivation = true;
        }
    }
}