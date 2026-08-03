using UnityEngine;

namespace VRLogConsole.Scripts.Data.Log
{
    public interface ILogEntry
    {
        string Log { get; }
        string StackTrace { get; }
        LogType Type { get; }
    }
}
