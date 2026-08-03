using System;
using VRLogConsole.Scripts.Models.Settings;
using VRLogConsole.Scripts.Presenters.Base;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Presenters.Theme
{
    public class ThemePresenter : PresenterBehaviour
    {
        public event Action ThemeUpdated;
        
        public ThemeType Theme { get; private set; }

        protected override void Initialize()
        {
            Get<IConfigurationModel>().ThemeUpdated += OnThemeUpdated;
        }

        protected override void PostInitialize()
        {
            OnThemeUpdated();
        }

        protected override void Dispose()
        {
            Get<IConfigurationModel>().ThemeUpdated -= OnThemeUpdated;
        }
        
        private void OnThemeUpdated()
        {
            Theme = Get<IConfigurationModel>().Configuration.Theme;
            
            ThemeUpdated?.Invoke();
        }
    }
}
