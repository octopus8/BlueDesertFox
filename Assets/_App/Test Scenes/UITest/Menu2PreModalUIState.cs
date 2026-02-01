using System;
using UnityEngine;

public class Menu2PreModalUIState : UIState
{
    public void OnCancelButton()
    {
        uiManager.PopState();
    }
    
    
    
}
