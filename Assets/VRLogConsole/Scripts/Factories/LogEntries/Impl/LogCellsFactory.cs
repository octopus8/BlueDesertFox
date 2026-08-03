using UnityEngine;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Presenters.LogCell;
using VRLogConsole.Scripts.Properties.LogCell;

namespace VRLogConsole.Scripts.Factories.LogEntries.Impl
{
    public class LogCellsFactory : ILogCellsFactory
    {
        private readonly Transform _logsContainer;
        private readonly LogCellProperties _logCellPrefab;
        
        public LogCellsFactory(Transform logsContainer, LogCellProperties logCellPrefab)
        {
            _logsContainer = logsContainer;
            _logCellPrefab = logCellPrefab;
        }

        public ILogCell Build(ILogEntry logEntry)
        {
            return CreateLogEntry(logEntry,_logsContainer);
        }

        private ILogCell CreateLogEntry(ILogEntry logEntry,Transform container)
        {
           var spawnedPrefab = Object.Instantiate(_logCellPrefab,container);
           var logCell = spawnedPrefab.GetComponent<ILogCell>();
           spawnedPrefab.name = logEntry.Type.ToString();
           logCell.SetPayload(logEntry);

           return logCell;
        }
    }
}
