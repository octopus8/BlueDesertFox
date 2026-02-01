using System.Collections;
using App.StartScene;
using LiquidForce;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public class SceneSelectUIState : UIState {


    /// <summary>Scene list.</summary>
    [Tooltip("Scene list.")]
    [SerializeField] private SceneListSO sceneList;

    /// <summary>Scene list button container.</summary>
    [Tooltip("Scene list button container")]
    [SerializeField] private GameObject sceneListContainer;

    /// <summary>Prototype scene list button.</summary>
    [Tooltip("Prototype scene list button.")]
    [SerializeField] private SceneListButton prototypeButton;

    [SerializeField] private SceneLoader sceneLoader;
    
    /// <summary>Test action.</summary>
    private InputAction testAction;
    

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
        
        testAction = InputSystem.actions.FindAction("TestAction");
        testAction.Enable();
        
    }

    void Update()
    {
        if (testAction.WasPressedThisFrame())
        {
            uiManager.Hide();
            _ = CameraFader.Instance.FadeCameraOut(1);

            var loadSceneAsync = SceneManager.LoadSceneAsync("DOTS Scene");
            loadSceneAsync.allowSceneActivation = false;
            StartCoroutine(TestOp(loadSceneAsync));
        }

    }
        
        
    IEnumerator TestOp(AsyncOperation asyncOperation)
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

    
    public void OnBackButton()
    {
        uiManager.PopState();
    }


    
}

