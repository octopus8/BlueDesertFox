using LiquidForce;
using UnityEngine;

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
        
        
        if (sceneLoader == null) 
        {
            Debug.LogError("SceneLoader is null");
        }
    }
    
    
    /// <summary>
    /// Loads the scene associated with the button.
    /// </summary>
    public void LoadScene(SceneListSO.SceneListScene scene)
    {
        uiManager.Hide(resumeGameplay: false);
        _ = CameraFader.Instance.FadeCameraOut(1);
        sceneLoader.LoadScene(scene, true);
    }
    
    
    

    
    public void OnBackButton()
    {
        uiManager.PopState();
    }


    
}

