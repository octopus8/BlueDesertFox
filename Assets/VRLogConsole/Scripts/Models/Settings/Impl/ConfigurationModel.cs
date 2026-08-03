using System;
using VRLogConsole.Scripts.Data.Config;
using VRLogConsole.Scripts.Models.Base.Impl;
using VRLogConsole.Scripts.Types.Lock;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Models.Settings.Impl
{
    public class ConfigurationModel : Model , IConfigurationModel
    {
        public event Action ConfigInitialized;
        public event Action ThemeUpdated;
        public event Action LockUpdated;
        public event Action LookAtPlayerUpdated;
        
        public IConfiguration Configuration { get; private set; }

        public void Initialize(IConfiguration configuration)
        {
            Configuration = configuration;
            
            ConfigInitialized?.Invoke();
        }

        public void UpdateTheme(ThemeType themeType)
        {
            Configuration.UpdateTheme(themeType);
            
            ThemeUpdated?.Invoke();   
        }

        public void LookAtPlayer(bool value)
        {
            Configuration.UpdateLookAtPlayer(value);
            
            LookAtPlayerUpdated?.Invoke();
        }

        public void UpdateLock(LockType lockType)
        {
            Configuration.UpdateLock(lockType);
            
            LockUpdated?.Invoke();
        }
    }
}
