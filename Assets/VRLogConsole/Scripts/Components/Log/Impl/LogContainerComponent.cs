using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VRLogConsole.Scripts.Components.Counter;
using VRLogConsole.Scripts.Components.Counter.Impl;
using VRLogConsole.Scripts.Presenters.LogCell;

namespace VRLogConsole.Scripts.Components.Log.Impl
{
    public class LogContainerComponent : ILogContainerComponent
    {
        private const string Hide = "Hide";
        private const string Show = "Show";

        private readonly LogType _logType;
        private readonly List<ILogCell> _logCells;
        private readonly TMP_Text _buttonText;
        private readonly ICounterComponent _counterComponent;

        private bool _showLogs = true;

        public LogContainerComponent(LogType logType,TMP_Text counter,TMP_Text buttonText)
        {
            _counterComponent = new CounterComponent(counter);
            _logCells = new List<ILogCell>();

            _logType = logType;
            _buttonText = buttonText;

            ChangeLabel(_showLogs,_buttonText);
        }

        public void ToggleLogs()
        {
            _showLogs = !_showLogs;
         
            ChangeLabel(_showLogs,_buttonText);
            DisableGameObjects(_showLogs,_logCells);
        }

        public void AddCell(ILogCell logCell)
        {
            _logCells.Add(logCell);
            _counterComponent.IncreaseCounter();
        }

        public void EmptyContainers()
        {
            for (var i = 0; i < _logCells.Count; i++)
            {
                Object.Destroy(_logCells[i].GameObject);
            }
            
            _logCells.Clear();
            _counterComponent.Reset();
        }
        
        private void ChangeLabel(bool showLogs,TMP_Text textButton)
        {
            var show = $"{Show} {_logType.ToString()}";
            var hide = $"{Hide} {_logType.ToString()}";
            
            textButton.text = showLogs ? hide : show;
        }

        private static void DisableGameObjects(bool showLogs,IList<ILogCell> logCells)
        {
            for (var i = 0; i < logCells.Count; i++)
            {
                logCells[i].GameObject.SetActive(showLogs);
            }
        }
    }
}
