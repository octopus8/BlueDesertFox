using LiquidForce;
using UnityEngine;
using UnityEngine.UI;

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
        // Create and initialize scene list buttons from the inactive prototype template.
        foreach (SceneListSO.SceneListScene scene in sceneList.scenes)
        {
            SceneSelectButton sceneSelectButton = Instantiate(prototypeButton.gameObject, sceneListContainer.transform)
                .GetComponent<SceneSelectButton>();
            sceneSelectButton.Init(scene, this);
        }

        // Remove the template so it can never reappear in the layout.
        Destroy(prototypeButton.gameObject);

        // ScrollRect AutoHide previously expanded the viewport at runtime. With scrollbars
        // removed, force the viewport to fill so baked collapsed anchors cannot hide the list.
        EnsureViewportFillsScrollView();
        
        
        if (sceneLoader == null) 
        {
            Debug.LogError("SceneLoader is null");
        }
    }

    private void EnsureViewportFillsScrollView()
    {
        ScrollRect scrollRect = sceneListContainer != null
            ? sceneListContainer.GetComponentInParent<ScrollRect>(true)
            : null;
        if (scrollRect == null || scrollRect.viewport == null)
            return;

        RectTransform viewport = scrollRect.viewport;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
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

