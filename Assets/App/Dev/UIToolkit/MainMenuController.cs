using UnityEngine;
using UnityEngine.UIElements;

public class MainMenuController : MonoBehaviour
{
    public VisualElement ui;

    public Button testButton1;
    public Button testButton2;
    public Button testButton3;


    private void Awake()
    {
        ui = GetComponent<UIDocument>().rootVisualElement;
    }

    private void OnEnable()
    {
        testButton1 = ui.Q<Button>("TestButton1");
        testButton1.clicked += OnTestButton1Clicked;
        testButton2 = ui.Q<Button>("TestButton2");
        testButton2.clicked += OnTestButton2Clicked;
        testButton3 = ui.Q<Button>("TestButton3");
        testButton3.clicked += OnTestButton3Clicked;
    }

    private void OnTestButton1Clicked()
    {
        OnTestButtonClicked(1);
    }

    private void OnTestButton2Clicked()
    {
        OnTestButtonClicked(2);
    }

    private void OnTestButton3Clicked()
    {
        OnTestButtonClicked(3);
    }

    private void OnTestButtonClicked(int id)
    {
        Debug.Log("Button clicked: " + id);
    }


}
