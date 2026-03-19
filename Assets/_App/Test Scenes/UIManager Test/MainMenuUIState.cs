using UnityEngine;

public class MainMenuUIState : UIState {
    
    [SerializeField] private UIState menu2PreModal;

    [SerializeField] private GameObject modalOverlay;
    
    [SerializeField] private UIState blueDesertFoxButton;
    
    
    

    public void OnMenu2Button()
    {
        uiManager.PushModal(menu2PreModal);
    }

    public void OnBlueDesertFoxButton()
    {
        uiManager.PushState(blueDesertFoxButton);
    }

    public override void OnModalPushed()
    {
        base.OnModalPushed();
        modalOverlay.SetActive(true);
    }

    public override void OnPushed()
    {
        base.OnPushed();
        modalOverlay.SetActive(false);
    }


    public override void OnPopped()
    {
        base.OnPopped();
        modalOverlay.SetActive(false);
    }
}

