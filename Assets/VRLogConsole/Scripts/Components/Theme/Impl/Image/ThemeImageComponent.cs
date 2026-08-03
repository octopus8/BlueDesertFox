using UnityEngine;
using VRLogConsole.Scripts.Components.Theme.Impl.Base;

namespace VRLogConsole.Scripts.Components.Theme.Impl.Image
{
    public class ThemeImageComponent : ThemeComponent
    {
        [SerializeField] private UnityEngine.UI.Image image;
        
        public override void SetColor(Color color)
        {
            image.color = color;
        }
    }
}
