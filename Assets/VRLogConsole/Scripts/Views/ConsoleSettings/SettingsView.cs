using VRLogConsole.Scripts.Presenters.ConsoleSettings;
using VRLogConsole.Scripts.Properties.ConsoleSettings;
using VRLogConsole.Scripts.Types.Lock;
using VRLogConsole.Scripts.Types.Theme;
using VRLogConsole.Scripts.Views.Base;

namespace VRLogConsole.Scripts.Views.ConsoleSettings
{
    public class SettingsView : ViewBehaviour<SettingsPresenter,SettingsProperties>
    {
        private LockType _lockType = LockType.Unlocked;

        private const string LockPositionLabel = "Lock Position";
        private const string UnlockPositionLabel = "Unlock Position"; 
        
        protected override void Initialize()
        {
            Prefab.lockButton.onClick.AddListener(ToggleLockState);
            Prefab.theme.onValueChanged.AddListener(ThemeChanged);
            Prefab.lookAtPlayerToggle.onValueChanged.AddListener(LookAtPlayerChanged);
        }

        protected override void PostInitialize()
        {
            UpdateLockState(Presenter.LockType);
            UpdateLookAtPlayerState(Presenter.LookToPlayer);
        }

        protected override void Dispose()
        {
            Prefab.lockButton.onClick.RemoveListener(ToggleLockState);
            Prefab.theme.onValueChanged.RemoveListener(ThemeChanged);
            Prefab.lookAtPlayerToggle.onValueChanged.RemoveListener(LookAtPlayerChanged);
        }

        private void UpdateLockState(LockType lockType)
        {
            _lockType = lockType;
            
            SwitchLockButtonName(_lockType);
            
            Presenter.LockConsole(_lockType);
        }

        private void UpdateLookAtPlayerState(bool value)
        {
            Prefab.lookAtPlayerToggle.isOn = value;
            
            LookAtPlayerChanged(value);
        }
        
        private void ToggleLockState()
        {
            if (_lockType == LockType.Locked)
            {
                _lockType = LockType.Unlocked;
            }
            else
            {
                _lockType = LockType.Locked;
            }
            
            SwitchLockButtonName(_lockType);
            
            Presenter.LockConsole(_lockType);
        }

        private void SwitchLockButtonName(LockType lockType)
        {
            if (lockType == LockType.Locked)
            {
                Prefab.lockText.text = UnlockPositionLabel;
            }
            else
            {
                Prefab.lockText.text = LockPositionLabel;
            }
        }

        #region events

        private void LookAtPlayerChanged(bool value)
        {
            Presenter.LookAtPlayer(value);
        }
        
        private void ThemeChanged(int index)
        {
            switch (index)
            {
                case 0:
                    Presenter.ChangeTheme(ThemeType.Dark);
                    break;
                case 1:
                    Presenter.ChangeTheme(ThemeType.Light);
                    break;
            }
        }
        
        #endregion
    }
}
