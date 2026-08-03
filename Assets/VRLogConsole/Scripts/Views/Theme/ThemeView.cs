using VRLogConsole.Scripts.Factories.Theme;
using VRLogConsole.Scripts.Factories.Theme.Impl;
using VRLogConsole.Scripts.Presenters.Theme;
using VRLogConsole.Scripts.Properties.Theme;
using VRLogConsole.Scripts.Views.Base;

namespace VRLogConsole.Scripts.Views.Theme
{
    public class ThemeView : ViewBehaviour<ThemePresenter,ThemeProperties>
    {
        private IThemeFactory _themeFactory;
        
        protected override void Initialize()
        {
            _themeFactory = new ThemeFactory(Prefab.themeLibrary,Prefab.rootTransform);
            
            Presenter.ThemeUpdated += OnThemeUpdated;
        }

        protected override void Dispose()
        {
            Presenter.ThemeUpdated -= OnThemeUpdated;
        }

        private void OnThemeUpdated()
        {
            _themeFactory.SetTheme(Presenter.Theme);
        }
    }
}
