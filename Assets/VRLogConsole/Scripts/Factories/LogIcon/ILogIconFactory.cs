using UnityEngine;

namespace VRLogConsole.Scripts.Factories.LogIcon
{
    public interface ILogIconFactory
    {
        Sprite Get(LogType logType);
    }
}
