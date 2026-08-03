using UnityEngine;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Components.Theme.Impl.Base
{
    public abstract class ThemeComponent : MonoBehaviour, IThemeComponent
    {
        [SerializeField] private ThemeColorType themeColorType;
        public ThemeColorType ColorType => themeColorType;
        
        public abstract void SetColor(Color color);
    }
}
