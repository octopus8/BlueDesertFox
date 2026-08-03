namespace VRLogConsole.Scripts.Components.Counter
{
    public interface ICounterComponent
    {
        void Reset();
        void IncreaseCounter();
        void SetCounter(int counter);
    }
}
