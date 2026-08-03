using UnityEngine;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Components.Theme
{
    public interface IThemeComponent
    {
        ThemeColorType ColorType { get; }
        void SetColor(Color color);
    }
}
