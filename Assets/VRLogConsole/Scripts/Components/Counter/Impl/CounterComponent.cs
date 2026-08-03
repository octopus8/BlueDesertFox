using TMPro;

namespace VRLogConsole.Scripts.Components.Counter.Impl
{
    public class CounterComponent : ICounterComponent
    {
        private const int CountLimit = 999;
        
        private readonly TMP_Text _indicator;
        private int _counter;
        
        public CounterComponent(TMP_Text indicator)
        {
            _counter = 0;
            _indicator = indicator;
        }
        
        public void Reset()
        {
            _counter = 0;
            UpdateUI(_counter);
        }

        public void SetCounter(int counter)
        {
            _counter = counter;
            UpdateUI(_counter);
        }
        
        public void IncreaseCounter()
        {
            _counter++;
            UpdateUI(_counter);
        }

        private void UpdateUI(int counter)
        {
            if (counter < CountLimit)
            {
                _indicator.text = $"{counter}";
            }
            else
            {
                _indicator.text = $"{CountLimit}+";
            }
        }
    }
}
