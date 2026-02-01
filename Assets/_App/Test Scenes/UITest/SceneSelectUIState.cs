using System.Collections;
using App.StartScene;
using LiquidForce;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public class SceneSelectUIState : MonoBehaviour, IUIState {


    /// <summary>Scene list.</summary>
    [Tooltip("Scene list.")]
    [SerializeField] private SceneListSO sceneList;

    /// <summary>Scene list button container.</summary>
    [Tooltip("Scene list button container")]
    [SerializeField] private GameObject sceneListContainer;

    /// <summary>Prototype scene list button.</summary>
    [Tooltip("Prototype scene list button.")]
    [SerializeField] private SceneListButton prototypeButton;
    
    [SerializeField] private UIManager uiManager;


    void Start()
    {
        // Create and initialize scene list buttons.
        foreach (SceneListSO.SceneListScene scene in sceneList.scenes)
        {
            SceneListButton sceneListButton = Instantiate(prototypeButton.gameObject, sceneListContainer.transform)
                .GetComponent<SceneListButton>();
            sceneListButton.Init(scene.sceneName, scene.scene);
        }

        prototypeButton.gameObject.SetActive(false);
    }
    
    
    /// <summary>
    /// Loads the scene associated with the button.
    /// </summary>
    /// <param name="button"></param>
    public void LoadScene(SceneListButton button)
    {
        uiManager.Hide();
        _ = CameraFader.Instance.FadeCameraOut(1);
  
        SceneManager.LoadScene("DOTS Scene Not Addressable");
            
//            AsyncOperationHandle<SceneInstance> loadSceneHandle = Addressables.LoadSceneAsync( button.GetAssetReference(), LoadSceneMode.Single, false);
//            StartCoroutine(ActivateLoadedSceneOnLoad(loadSceneHandle));
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


    public void OnEnter() => gameObject.SetActive(true);
    public void OnExit() => gameObject.SetActive(false);
    public void OnResume() => gameObject.SetActive(true);
    
}

