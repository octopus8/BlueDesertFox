using System;
using UnityEngine;

public class Menu2PreModalUIState : UIState
{
    [SerializeField] private UIState menu2;
    
    public void OnCancelButton()
    {
        uiManager.PopState();
    }


    public void OnConfirmButton()
    {
        uiManager.PopModalPush(menu2);
    }
    
    
    
}
