



using App.StartScene;
using UnityEngine;

public interface IUIState
{
    void OnEnter();
    void OnExit();

    void OnPushed();
    
    void OnModalPushed();
    
    void OnPopped();
}
