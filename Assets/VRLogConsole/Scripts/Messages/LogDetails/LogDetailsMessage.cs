using VRLogConsole.Scripts.Data.Log;

namespace VRLogConsole.Scripts.Messages.LogDetails
{
    public class LogDetailsMessage
    {
        public ILogEntry LogEntry { get; }
        
        public LogDetailsMessage(ILogEntry logEntry)
        {
            LogEntry = logEntry;
        }
    }
}
