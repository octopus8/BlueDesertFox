using System;
using VRLogConsole.Scripts.Data.Config;
using VRLogConsole.Scripts.Models.Base;
using VRLogConsole.Scripts.Types.Lock;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Models.Settings
{
    public interface IConfigurationModel : IModel
    {
        event Action ConfigInitialized;
        event Action ThemeUpdated;
        event Action LockUpdated;
        event Action LookAtPlayerUpdated;
        
        IConfiguration Configuration { get; }

        void Initialize(IConfiguration configuration);
        void UpdateLock(LockType lockType);
        void UpdateTheme(ThemeType themeType);
        void LookAtPlayer(bool value);
    }
}
