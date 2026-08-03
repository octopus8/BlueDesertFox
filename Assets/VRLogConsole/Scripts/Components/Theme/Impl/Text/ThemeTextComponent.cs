using TMPro;
using UnityEngine;
using VRLogConsole.Scripts.Components.Theme.Impl.Base;

namespace VRLogConsole.Scripts.Components.Theme.Impl.Text
{
    public class ThemeTextComponent : ThemeComponent
    {
        [SerializeField] private TextMeshProUGUI text;
        
        public override void SetColor(Color color)
        {
            text.color = color;
        }
    }
}
