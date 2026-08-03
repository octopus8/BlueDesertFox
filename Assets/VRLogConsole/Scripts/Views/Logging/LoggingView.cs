using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRLogConsole.Scripts.Components.Log;
using VRLogConsole.Scripts.Components.Log.Impl;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Factories.LogEntries;
using VRLogConsole.Scripts.Factories.LogEntries.Impl;
using VRLogConsole.Scripts.Presenters.LogCell;
using VRLogConsole.Scripts.Presenters.Logging;
using VRLogConsole.Scripts.Properties.Maximized;
using VRLogConsole.Scripts.Views.Base;

namespace VRLogConsole.Scripts.Views.Logging
{
    public class LoggingView : ViewBehaviour<LoggingPresenter,LoggingProperties>
    {
        private ILogCellsFactory _logCellsFactory;

        private ILogContainerComponent _infoLogContainerComponent;
        private ILogContainerComponent _warningLogContainerComponent;
        private ILogContainerComponent _errorLogContainerComponent;

        private List<ILogCell> _logCells;
        
        protected override void Initialize()
        {
            _logCells = new List<ILogCell>();
            
            _infoLogContainerComponent = new LogContainerComponent(LogType.Log,Prefab.infoLogsCounter,Prefab.infoButtonText);
            _warningLogContainerComponent = new LogContainerComponent(LogType.Warning,Prefab.warningLogsCounter,Prefab.warningButtonText);
            _errorLogContainerComponent = new LogContainerComponent(LogType.Error,Prefab.errorsLogsCounter,Prefab.errorsButtonText);
            
            _logCellsFactory = new LogCellsFactory(Prefab.logsContainer.transform, Prefab.logCellPrefab);
            
            AddListeners();
        }
        
        protected override void Dispose()
        {
            RemoveListeners();
        }
        
        private void AddListeners()
        {
            Prefab.clearButton.onClick.AddListener(ClearLogs);
            Prefab.infoButton.onClick.AddListener(_infoLogContainerComponent.ToggleLogs);
            Prefab.warningButton.onClick.AddListener(_warningLogContainerComponent.ToggleLogs);
            Prefab.errorButton.onClick.AddListener(_errorLogContainerComponent.ToggleLogs);
            Presenter.NewLogAdded += OnNewLogAdded;
        }

        private void RemoveListeners()
        {
            Prefab.clearButton.onClick.RemoveListener(ClearLogs);
            Prefab.infoButton.onClick.RemoveListener(_infoLogContainerComponent.ToggleLogs);
            Prefab.warningButton.onClick.RemoveListener(_warningLogContainerComponent.ToggleLogs);
            Prefab.errorButton.onClick.RemoveListener(_errorLogContainerComponent.ToggleLogs);
            Presenter.NewLogAdded -= OnNewLogAdded;
        }

        #region events

        private void OnNewLogAdded(ILogEntry logEntry)
        {
            var existingCell = _logCells.FirstOrDefault(cell => cell.LogEntry.Log == logEntry.Log && cell.LogEntry.StackTrace == logEntry.StackTrace);

            if (existingCell == null)
            {
                var spawnedCell = _logCellsFactory.Build(logEntry);
                _logCells.Add(spawnedCell);
            
                AssignCell(spawnedCell);
            }
            else
            {
                existingCell.IncreaseCount();
            }
        }

        private void ClearLogs()
        {
            Presenter.Clear();
            
            _infoLogContainerComponent.EmptyContainers();
            _warningLogContainerComponent.EmptyContainers();
            _errorLogContainerComponent.EmptyContainers();
        }
        
        #endregion
        
        private void AssignCell(ILogCell logCell)
        {
            switch (logCell.LogEntry.Type)
            {
                case LogType.Log:
                    _infoLogContainerComponent.AddCell(logCell);
                    break;
                case LogType.Warning:
                    _warningLogContainerComponent.AddCell(logCell);
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    _errorLogContainerComponent.AddCell(logCell);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
