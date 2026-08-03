using System;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Models.Log;
using VRLogConsole.Scripts.Presenters.Base;

namespace VRLogConsole.Scripts.Presenters.Logging
{
    public class LoggingPresenter : PresenterBehaviour
    {
        public event Action<ILogEntry> NewLogAdded;

        protected override void Initialize()
        {
            Get<ILogModel>().NewLogAdded += OnNewLogAdded;
        }

        protected override void Dispose()
        {
            Get<ILogModel>().NewLogAdded -= OnNewLogAdded;
        }

        public void Clear()
        {
            Get<ILogModel>().Clear();
        }
        
        #region events
        
        private void OnNewLogAdded(ILogEntry logEntry)
        {
            NewLogAdded?.Invoke(logEntry);
        }
        
        #endregion
    }
}
