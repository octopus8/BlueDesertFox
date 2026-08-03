using UnityEngine;

namespace VRLogConsole.Scripts.Data.Log.Impl
{
    public class LogEntry : ILogEntry
    {
        public string Log { get; }
        public string StackTrace { get; }
        public LogType Type { get; }

        public LogEntry(string log, string stackTrace, LogType type)
        {
            Log = log;
            StackTrace = stackTrace;
            Type = type;
        }
    }
}
