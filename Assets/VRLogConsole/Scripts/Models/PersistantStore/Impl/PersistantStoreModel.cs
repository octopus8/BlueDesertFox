using System;
using System.Text;
using UnityEngine;
using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Models.Base.Impl;
using VRLogConsole.Scripts.Models.Log;
using VRLogConsole.Scripts.Models.PersistantStore.Impl.Reader;
using VRLogConsole.Scripts.Models.PersistantStore.Impl.Reader.Impl;
using VRLogConsole.Scripts.Models.Settings;

namespace VRLogConsole.Scripts.Models.PersistantStore.Impl
{
    public class PersistantStoreModel : Model, IPersistantStoreModel
    {
        private IFileReader _fileReader;
        
        private bool _saveLogs;
        private string _fileName;
        private readonly string _path = Application.persistentDataPath + "/Logs/";

        protected override void Initialize()
        { 
            _fileReader = new FileReader();

            _fileName = GetFileNameWithTimeStamp();
            
            Get<ILogModel>().NewLogAdded += OnNewLogAdded;
            Get<IConfigurationModel>().ConfigInitialized += OnConfigInitialized;
        }

        private void OnConfigInitialized()
        {
            _saveLogs = Get<IConfigurationModel>().Configuration.SaveLogLocally;
        }

        protected override void Dispose()
        {
            Get<ILogModel>().NewLogAdded -= OnNewLogAdded;
            Get<IConfigurationModel>().ConfigInitialized -= OnConfigInitialized;
        }

        private void OnNewLogAdded(ILogEntry logEntry)
        {
            if (_saveLogs)
            {
                var logItem = BuildLog(logEntry);
                Save(logItem);
            }
        }

        private static string GetFileNameWithTimeStamp()
        {
            var timeStamp = DateTime.Now.ToString("HH-mm_dd-MM-yyyy");
            return $"Log_{timeStamp}.log";
        }
        
        private async void Save(string content)
        {
            await _fileReader.WriteFileAsync(_path, _fileName, content);
        }

        private static string BuildLog(ILogEntry logEntry)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append(logEntry.Log);
            stringBuilder.Append(Environment.NewLine);
            stringBuilder.Append(logEntry.StackTrace);
            
            return stringBuilder.ToString();
        }
    }
}
