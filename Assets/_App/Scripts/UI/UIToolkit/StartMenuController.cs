using LiquidForce;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Binds the Start menu UI Toolkit button and loads Escape Mountain via SceneSelectUIState.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class StartMenuController : MonoBehaviour
{
    const string StartButtonName = "StartButton";
    const int DefaultEscapeMountainSceneIndex = 1;

    [SerializeField] SceneListSO sceneList;
    [SerializeField] int sceneIndex = DefaultEscapeMountainSceneIndex;

    Button _startButton;

    void Start()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null)
            return;

        VisualElement root = doc.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("StartMenuController: UIDocument rootVisualElement is null.");
            return;
        }

        _startButton = root.Q<Button>(StartButtonName);
        if (_startButton == null)
        {
            Debug.LogError($"StartMenuController: button '{StartButtonName}' not found.");
            return;
        }

        _startButton.clicked += OnStartClicked;
    }

    void OnDestroy()
    {
        if (_startButton != null)
            _startButton.clicked -= OnStartClicked;
    }

    void OnStartClicked()
    {
        if (sceneList == null || sceneIndex < 0 || sceneIndex >= sceneList.scenes.Count)
        {
            Debug.LogError("StartMenuController: invalid SceneListSO or scene index.");
            return;
        }

        SceneSelectUIState sceneSelect = FindFirstObjectByType<SceneSelectUIState>(FindObjectsInactive.Include);
        if (sceneSelect == null)
        {
            Debug.LogError("StartMenuController: SceneSelectUIState not found.");
            return;
        }

        sceneSelect.LoadScene(sceneList.scenes[sceneIndex]);
    }
}
