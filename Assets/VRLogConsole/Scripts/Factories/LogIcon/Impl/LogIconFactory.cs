using System;
using UnityEngine;
using VRLogConsole.Scripts.Libraries.LogIcons;

namespace VRLogConsole.Scripts.Factories.LogIcon.Impl
{
    public class LogIconFactory : ILogIconFactory
    {
        private readonly LogIconsLibrary _library;

        public LogIconFactory(LogIconsLibrary library)
        {
            _library = library;
        }

        public Sprite Get(LogType logType)
        {
            return PickIcon(logType);
        }

        private Sprite PickIcon(LogType logType)
        {
            switch (logType)
            {
                case LogType.Log:
                    return _library.infoIcon;
                case LogType.Warning:
                    return _library.warningIcon;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return _library.errorIcon;
                default:
                    throw new ArgumentOutOfRangeException(nameof(logType), logType, null);
            }
        }
    }
}
