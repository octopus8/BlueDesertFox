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


    public void LoadScene(int idx)
    {
        SceneSelectUIState sceneSelectUIState = FindFirstObjectByType<SceneSelectUIState>(FindObjectsInactive.Include);
        sceneSelectUIState.LoadScene(sceneList.scenes[idx]);
    }
    
    
}
