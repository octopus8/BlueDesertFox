using System.Collections;
using LiquidForce;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadScene(string sceneName, bool waitForFadeOut = false)
    {
        var loadSceneAsync = SceneManager.LoadSceneAsync("DOTS Scene");
        if (waitForFadeOut)
        {
            loadSceneAsync.allowSceneActivation = false;
            StartCoroutine(WaitForFadeOut(loadSceneAsync));
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
