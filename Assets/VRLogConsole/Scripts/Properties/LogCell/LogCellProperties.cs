using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRLogConsole.Scripts.Libraries.LogIcons;
using VRLogConsole.Scripts.Properties.Base;

namespace VRLogConsole.Scripts.Properties.LogCell
{
    public class LogCellProperties : PropertiesBehaviour
    {
        public Button openStackTraceButton;
        public LogIconsLibrary logIcons;
        public Image icon;
        public TextMeshProUGUI counterText;
        public TextMeshProUGUI logText;
        public RectTransform cellRectTransform;
    }
}
