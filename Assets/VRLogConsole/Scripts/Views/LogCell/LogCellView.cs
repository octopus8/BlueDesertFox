using UnityEngine;
using VRLogConsole.Scripts.Components.Counter;
using VRLogConsole.Scripts.Components.Counter.Impl;
using VRLogConsole.Scripts.Factories.LogIcon;
using VRLogConsole.Scripts.Factories.LogIcon.Impl;
using VRLogConsole.Scripts.Presenters.LogCell;
using VRLogConsole.Scripts.Properties.LogCell;
using VRLogConsole.Scripts.Views.Base;

namespace VRLogConsole.Scripts.Views.LogCell
{
    public class LogCellView : ViewBehaviour<LogCellPresenter,LogCellProperties>
    {
        private const float MINHeight = 30;
        private const float Offset = 10;
        
        private ILogIconFactory _iconFactory;
        private ICounterComponent _counterComponent; 

        protected override void Initialize()
        {
            _iconFactory = new LogIconFactory(Prefab.logIcons);
            _counterComponent = new CounterComponent(Prefab.counterText);

            Presenter.PayloadReceived += OnPayloadReceived;
            Presenter.CounterUpdated += OnCounterUpdated;
            Prefab.openStackTraceButton.onClick.AddListener(OpenStackTrace);
        }
        
        protected override void Dispose()
        {
            Presenter.CounterUpdated -= OnCounterUpdated;
            Presenter.PayloadReceived -= OnPayloadReceived;
            Prefab.openStackTraceButton.onClick.RemoveListener(OpenStackTrace);
        }

        private void OpenStackTrace()
        {
            Presenter.OpenDetails();
        }

        #region events

        private void OnPayloadReceived()
        {
            Prefab.icon.sprite = _iconFactory.Get(Presenter.LogEntry.Type);
            Prefab.logText.text = Presenter.LogEntry.Log;
            
            _counterComponent.SetCounter(Presenter.Count);

            UpdateHeight();
        }

        private void OnCounterUpdated()
        {
            _counterComponent.IncreaseCounter();
        }
        
        #endregion

        private void UpdateHeight()
        {
            var logTextSize = Prefab.logText.GetPreferredValues();
            
            if (logTextSize.y > MINHeight)
            {
                var sizeDelta = Prefab.cellRectTransform.sizeDelta;

                var height = logTextSize.y + Offset;
                Prefab.cellRectTransform.sizeDelta = new Vector2(sizeDelta.x,height);
            }
        }
    }
}
