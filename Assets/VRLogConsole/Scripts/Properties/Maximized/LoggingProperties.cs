using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRLogConsole.Scripts.Properties.Base;
using VRLogConsole.Scripts.Properties.LogCell;

namespace VRLogConsole.Scripts.Properties.Maximized
{
    public class LoggingProperties : PropertiesBehaviour
    {
        public Button infoButton;
        public Button warningButton;
        public Button errorButton;
        public Button clearButton;
        public TextMeshProUGUI infoButtonText;
        public TextMeshProUGUI warningButtonText;
        public TextMeshProUGUI errorsButtonText;
        public TextMeshProUGUI infoLogsCounter;
        public TextMeshProUGUI warningLogsCounter;
        public TextMeshProUGUI errorsLogsCounter;
        public LogCellProperties logCellPrefab;
        public GameObject logsContainer;
    }
}
