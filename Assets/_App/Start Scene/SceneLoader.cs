using System.Collections;
using LiquidForce;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadScene(SceneListSO.SceneListScene scene, bool waitForFadeOut = false)
    {
        if (scene.isAddressable)
        {
            AsyncOperationHandle<SceneInstance> loadSceneHandle = Addressables.LoadSceneAsync( scene.scene.AssetGUID, LoadSceneMode.Single, false);
            StartCoroutine(ActivateLoadedSceneOnLoad(loadSceneHandle));
            
        }
        else
        {
            var loadSceneAsync = SceneManager.LoadSceneAsync(scene.scenePath);
            if (waitForFadeOut)
            {
                loadSceneAsync.allowSceneActivation = false;
                StartCoroutine(WaitForFadeOut(loadSceneAsync));
            }
        }
    }
    
    
    
    /// <summary>
    /// Activates the loaded scene upon fully loading and camera fully faded out.
    /// </summary>
    /// <param name="loadSceneHandle"></param>
    /// <returns></returns>
    private IEnumerator ActivateLoadedSceneOnLoad(AsyncOperationHandle<SceneInstance> loadSceneHandle)
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
    
    
    
    IEnumerator WaitForFadeOut(AsyncOperation asyncOperation)
    {
        // Wait until done and collect progress as we go.
        while( !asyncOperation.isDone )
        {
            float loadProgress = asyncOperation.progress;
				
            if( loadProgress >= 0.9f )
            {
                // Almost done.
                break;
            }

            yield return null;
        }

        
        while (!CameraFader.Instance.IsCameraFadedOut())
        {
            yield return null;
        }
            
        // Allow new scene to start.
        asyncOperation.allowSceneActivation = true;            
            
    }
    
    
    
}
