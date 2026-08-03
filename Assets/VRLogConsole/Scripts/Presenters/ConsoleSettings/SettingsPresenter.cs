using VRLogConsole.Scripts.Models.Settings;
using VRLogConsole.Scripts.Presenters.Base;
using VRLogConsole.Scripts.Types.Lock;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Presenters.ConsoleSettings
{
    public class SettingsPresenter : PresenterBehaviour
    {
        public LockType LockType => Get<IConfigurationModel>().Configuration.LockType;
        public bool LookToPlayer => Get<IConfigurationModel>().Configuration.LookAtPlayer;

        public void ChangeTheme(ThemeType theme)
        {
            Get<IConfigurationModel>().UpdateTheme(theme);
        }

        public void LockConsole(LockType lockType)
        {
            Get<IConfigurationModel>().UpdateLock(lockType);
        }

        public void LookAtPlayer(bool value)
        {
            Get<IConfigurationModel>().LookAtPlayer(value);
        }
    }
}
