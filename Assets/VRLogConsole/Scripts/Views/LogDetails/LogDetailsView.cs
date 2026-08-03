using UnityEngine;
using VRLogConsole.Scripts.Factories.LogIcon;
using VRLogConsole.Scripts.Factories.LogIcon.Impl;
using VRLogConsole.Scripts.Presenters.LogDetails;
using VRLogConsole.Scripts.Properties.LogDetails;
using VRLogConsole.Scripts.Views.Base;

namespace VRLogConsole.Scripts.Views.LogDetails
{
    public class LogDetailsView : ViewBehaviour<LogDetailsPresenter,LogDetailsProperties>
    {
        private const float Offset = 10;
        
        private ILogIconFactory _iconFactory;
        
        protected override void Initialize()
        { 
            _iconFactory = new LogIconFactory(Prefab.iconsLibrary);
            
            Prefab.closeButton.onClick.AddListener(CloseButtonPressed);
            Presenter.LogReceived += OnLogReceived;
        }

        protected override void Dispose()
        {
            Prefab.closeButton.onClick.RemoveListener(CloseButtonPressed);
            Presenter.LogReceived -= OnLogReceived;
        }

        private void CloseButtonPressed()
        {
            Prefab.container.SetActive(false);
        }

        #region events

        private void OnLogReceived()
        {
            Prefab.logTitle.text = Presenter.LogEntry.Log;
            Prefab.stackTraceText.text = Presenter.LogEntry.StackTrace;
            Prefab.icon.sprite = _iconFactory.Get(Presenter.LogEntry.Type);

            Prefab.container.SetActive(true);
            
            UpdateHeight();
        }

        #endregion
        
        private void UpdateHeight()
        {
            var logTextSize = Prefab.logTitle.GetPreferredValues();
            
            var sizeDelta = Prefab.headerContainer.sizeDelta;

            var height = logTextSize.y + Offset;
            Prefab.headerContainer.sizeDelta = new Vector2(sizeDelta.x,height);
        }
    }
}
