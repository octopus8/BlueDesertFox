using System;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Messages.LogDetails;
using VRLogConsole.Scripts.Presenters.Base;

namespace VRLogConsole.Scripts.Presenters.LogDetails
{
    public class LogDetailsPresenter : PresenterBehaviour
    {
        public event Action LogReceived;
        
        public ILogEntry LogEntry { get; private set; }
        
        protected override void Initialize()
        {
            Subscribe<LogDetailsMessage>(LogDetailsReceived);
        }

        protected override void Dispose()
        {
            Unsubscribe<LogDetailsMessage>(LogDetailsReceived);
        }

        private void LogDetailsReceived(LogDetailsMessage message)
        {
            LogEntry = message.LogEntry;
            
            LogReceived?.Invoke();
        }
    }
}
