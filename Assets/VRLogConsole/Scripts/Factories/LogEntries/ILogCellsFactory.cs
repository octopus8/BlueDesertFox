using VRLogConsole.Scripts.Data.Log;
using VRLogConsole.Scripts.Presenters.LogCell;

namespace VRLogConsole.Scripts.Factories.LogEntries
{
    public interface ILogCellsFactory
    {
        ILogCell Build(ILogEntry logEntry);
    }
}
