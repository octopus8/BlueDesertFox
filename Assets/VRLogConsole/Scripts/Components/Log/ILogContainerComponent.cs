using VRLogConsole.Scripts.Presenters.LogCell;

namespace VRLogConsole.Scripts.Components.Log
{
    public interface ILogContainerComponent
    {
        void EmptyContainers();
        void ToggleLogs();
        void AddCell(ILogCell logCell);
    }
}
