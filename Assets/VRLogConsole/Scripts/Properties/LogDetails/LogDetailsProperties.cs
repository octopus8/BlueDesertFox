using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRLogConsole.Scripts.Libraries.LogIcons;
using VRLogConsole.Scripts.Properties.Base;

namespace VRLogConsole.Scripts.Properties.LogDetails
{
    public class LogDetailsProperties : PropertiesBehaviour
    {
        public LogIconsLibrary iconsLibrary;
        public Button closeButton;
        public Image icon;
        public TextMeshProUGUI logTitle;
        public TextMeshProUGUI stackTraceText;
        public GameObject container;
        public RectTransform headerContainer;
    }
}
