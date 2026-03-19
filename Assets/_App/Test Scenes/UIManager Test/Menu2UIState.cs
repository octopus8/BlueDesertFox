using UnityEngine;

public class Menu2UIState : UIState
{
    public void OnBackButton()
    {
        uiManager.PopState();
    }
}
