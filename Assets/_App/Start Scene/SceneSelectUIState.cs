using System.Collections;
using App.StartScene;
using LiquidForce;
using UnityEngine;
using UnityEngine.AddressableAssets;
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
    [SerializeField] private SceneSelectButton prototypeButton;

    [SerializeField] private SceneLoader sceneLoader;
    
    /// <summary>Test action.</summary>
    private InputAction testAction;
    

    void Start()
    {
        // Create and initialize scene list buttons.
        foreach (SceneListSO.SceneListScene scene in sceneList.scenes)
        {
            SceneSelectButton sceneSelectButton = Instantiate(prototypeButton.gameObject, sceneListContainer.transform)
                .GetComponent<SceneSelectButton>();
            sceneSelectButton.Init(scene, this);
        }

        prototypeButton.gameObject.SetActive(false);
        
        testAction = InputSystem.actions.FindAction("TestAction");
        testAction.Enable();
        
    }

    void Update()
    {
        if (testAction.WasPressedThisFrame())
        {
            LoadScene(sceneList.scenes[0]);
        }

    }
        

    
    
    /// <summary>
    /// Loads the scene associated with the button.
    /// </summary>
    public void LoadScene(SceneListSO.SceneListScene scene)
    {
        uiManager.Hide();
        _ = CameraFader.Instance.FadeCameraOut(1);
        sceneLoader.LoadScene(scene, true);
    }
    
    
    

    
    public void OnBackButton()
    {
        uiManager.PopState();
    }


    
}

