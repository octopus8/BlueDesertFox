using UnityEngine;

public class MainMenuUIState : MonoBehaviour, IUIState {
    
    public void OnEnter() => gameObject.SetActive(true);
    public void OnExit() => gameObject.SetActive(false);
    public void OnResume() => gameObject.SetActive(true);
    
}

