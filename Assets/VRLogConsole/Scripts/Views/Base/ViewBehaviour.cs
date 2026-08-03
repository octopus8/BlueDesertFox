using UnityEngine;
using VRLogConsole.Scripts.Presenters.Base;
using VRLogConsole.Scripts.Properties.Base;

namespace VRLogConsole.Scripts.Views.Base
{
    public class ViewBehaviour : MonoBehaviour
    {
        #region Lifecycle

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            PostInitialize();
        }
        
        private void OnEnable()
        {
            OnEnter();
        }

        private void OnDisable()
        {
            OnExit();
        }

        private void OnDestroy()
        {
            Dispose();
        }
        
        protected virtual void Initialize()
        {
            
        }

        protected virtual void PostInitialize()
        {
            
        }
        
        protected virtual void OnEnter()
        {

        }

        protected virtual void OnExit()
        {

        }

        protected virtual void Dispose()
        {

        }
    
        #endregion
    }
    
    public class ViewBehaviour<TPresenterBehaviour> : ViewBehaviour where TPresenterBehaviour : PresenterBehaviour
    {
        private TPresenterBehaviour _presenter;

        protected TPresenterBehaviour Presenter
        {
            get
            {
                if (_presenter == null)
                {
                    _presenter = GetComponent<TPresenterBehaviour>();
                }
                
                if (_presenter == null)
                {
                    Debug.LogWarning("Warning: No presenter was found on {0}", this);
                }

                return _presenter;
            }
        }
    }
    
    public abstract class ViewBehaviour<T,TW> : ViewBehaviour<T> where T : PresenterBehaviour where TW : PropertiesBehaviour
    {
        private TW _properties;

        protected TW Prefab
        {
            get
            {
                if (_properties == null)
                {
                    _properties = GetComponent<TW>();
                }
                
                return _properties;
            }
        }
    }
}