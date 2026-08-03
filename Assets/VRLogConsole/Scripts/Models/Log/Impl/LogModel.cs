using System;
using System.Collections.Generic;
using UnityEngine;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Models.Base.Impl;

namespace VRLogConsole.Scripts.Models.Log.Impl
{
    public class LogModel : Model , ILogModel
    {
        private List<ILogEntry> _infoLogs;
        private List<ILogEntry> _warningLogs;
        private List<ILogEntry> _errorLogs;
        
        public event Action<ILogEntry> NewLogAdded;

        public int InfoLogsCount => _infoLogs.Count;
        public int WarningLogsCount => _warningLogs.Count;
        public int ErrorLogsCount => _errorLogs.Count;
        
        protected override void Initialize()
        {
            _infoLogs = new List<ILogEntry>();
            _warningLogs = new List<ILogEntry>();
            _errorLogs = new List<ILogEntry>();
            
            Application.logMessageReceived += OnLogMessageReceived;
        }

        protected override void Dispose()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
        }
        
        public void Clear()
        {
            _infoLogs.Clear();
            _warningLogs.Clear();
            _errorLogs.Clear();
        }

        #region events

        private void OnLogMessageReceived(string log, string stacktrace, LogType logType)
        {
            var logEntry = new Data.Log.Impl.LogEntry(log,stacktrace,logType);
            
            AssignLog(logEntry);
            
            NewLogAdded?.Invoke(logEntry);
        }

        #endregion

        private void AssignLog(ILogEntry logEntry)
        {
            switch (logEntry.Type)
            {
                case LogType.Log:
                    _infoLogs.Add(logEntry);
                    break;
                case LogType.Warning:
                    _warningLogs.Add(logEntry);
                    break;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    _errorLogs.Add(logEntry);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
