using LiquidForce;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class TestAction : MonoBehaviour
{
    [SerializeField] private SceneListSO sceneList;

    [SerializeField]
    UnityEvent actions;

    
    /// <summary>Test action.</summary>
    private InputAction testAction;

    
    void Start()
    {
        testAction = InputSystem.actions.FindAction("TestAction");
        testAction.Enable();
    }

    // Update is called once per frame
    void Update()
    {
        if (testAction.WasPressedThisFrame())
        {
            actions.Invoke();
        }
    }


    public void LoadScene()
    {
        
        SceneSelectUIState sceneSelectUIState = FindFirstObjectByType<SceneSelectUIState>();
        sceneSelectUIState.LoadScene(sceneList.scenes[0]);
    }
    
    
}
