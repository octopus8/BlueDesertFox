using System;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Models.Base;

namespace VRLogConsole.Scripts.Models.Log
{
    public interface ILogModel : IModel
    {
        event Action<ILogEntry> NewLogAdded;

        int InfoLogsCount { get; }
        int WarningLogsCount { get; }
        int ErrorLogsCount { get; }
        
        void Clear();
    }
}
