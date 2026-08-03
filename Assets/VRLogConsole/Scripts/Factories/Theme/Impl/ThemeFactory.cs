using System;
using UnityEngine;
using VRLogConsole.Scripts.Components.Theme;
using VRLogConsole.Scripts.Data.Theme;
using VRLogConsole.Scripts.Libraries.Theme;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Factories.Theme.Impl
{
    public class ThemeFactory : IThemeFactory
    {
        private readonly ThemeLibrary _library;
        private readonly IThemeComponent[] _themeComponents;
        
        public ThemeFactory(ThemeLibrary library,Component rootTransform)
        {
            _library = library;
            _themeComponents = rootTransform.GetComponentsInChildren<IThemeComponent>(true);
        }

        public void SetTheme(ThemeType theme)
        {
            switch (theme)
            {
                case ThemeType.Dark:
                    SetTheme(_library.darkTheme);
                    break;
                case ThemeType.Light:
                    SetTheme(_library.lightTheme);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        private void SetTheme(ThemeColors selectedTheme)
        {
            foreach (var themeComponent in _themeComponents)
            {
                switch (themeComponent.ColorType)
                {
                    case ThemeColorType.Default:
                        themeComponent.SetColor(selectedTheme.defaultColor);
                        break;
                    case ThemeColorType.Text:
                        themeComponent.SetColor(selectedTheme.textColor);
                        break;
                    case ThemeColorType.Contrast:
                        themeComponent.SetColor(selectedTheme.contrastColor);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }
}
