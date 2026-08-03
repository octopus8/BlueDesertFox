using UnityEngine;
using VRLogConsole.Scripts.Data.Log;

namespace VRLogConsole.Scripts.Presenters.LogCell
{
    public interface ILogCell
    {
        ILogEntry LogEntry { get; }
        GameObject GameObject { get; }
        void SetPayload(ILogEntry logEntry);
        void IncreaseCount();
    }
}
