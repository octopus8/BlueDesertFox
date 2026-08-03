using System;
using UnityEngine;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Messages.LogDetails;
using VRLogConsole.Scripts.Presenters.Base;

namespace VRLogConsole.Scripts.Presenters.LogCell
{
    public class LogCellPresenter : PresenterBehaviour, ILogCell
    {
        public event Action PayloadReceived;
        public event Action CounterUpdated;
        public ILogEntry LogEntry { get; private set; }
        public int Count { get; private set; }
        public GameObject GameObject => gameObject;

        public void SetPayload(ILogEntry logEntry)
        {
            LogEntry = logEntry;
            
            Count = 1;
            
            PayloadReceived?.Invoke();
        }

        public void IncreaseCount()
        {
            Count++;
            
            CounterUpdated?.Invoke();
        }

        public void OpenDetails()
        {
            Publish(new LogDetailsMessage(LogEntry));
        }
    }
}
