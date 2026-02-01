using System;
using App.StartScene;
using UnityEngine;

public class UIState : MonoBehaviour, IUIState
{
    [SerializeField] protected UIManager uiManager;
    
    public virtual void OnEnter() => gameObject.SetActive(true);
    public virtual void OnExit() => gameObject.SetActive(false);
    public virtual void OnPushed() => gameObject.SetActive(false);

    public virtual void OnModalPushed(){}

    public virtual void OnPopped() => gameObject.SetActive(true);
    
    
    
}
