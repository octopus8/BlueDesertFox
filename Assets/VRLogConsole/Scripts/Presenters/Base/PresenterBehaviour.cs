using System;
using UnityEngine;
using UnityEngine.Assertions;
using VRLogConsole.Scripts.Models.Base;
using VRLogConsole.Scripts.Services.Locator.Base;
using VRLogConsole.Scripts.Services.Locator.Base.Impl;
using VRLogConsole.Scripts.Services.RouterService;
using VRLogConsole.Scripts.Services.RouterService.Impl;

namespace VRLogConsole.Scripts.Presenters.Base
{
    public abstract class PresenterBehaviour : MonoBehaviour
    {
        private static IDataRouterService _dataRouterService;

        private static IDataRouterService DataRouterService
        {
            get
            {
                if (_dataRouterService == null)
                {
                    _dataRouterService = new DataRouterService();
                    Assert.IsNotNull(_dataRouterService, "You must define a " + nameof(IDataRouterService));
                }
                return _dataRouterService;
            }
        }
        
        private IServiceLocator _serviceLocator;
        
        private IServiceLocator ServiceLocator
        {
            get
            {
                if (_serviceLocator == null)
                {
                    _serviceLocator = FindObjectOfType<ServiceLocator>();
                    Assert.IsNotNull(_serviceLocator, "You must define a " + nameof(IServiceLocator));
                }
                return _serviceLocator;
            }
        }

        protected TModel Get<TModel>() where TModel : class, IModel
        {
            return ServiceLocator.Get<TModel>();
        }

        #region DataRouter

        protected static void Publish<TType>(TType data)
        {
            DataRouterService.Publish(data);
        }

        protected static void Subscribe<TType>(Action<TType> action)
        {
            DataRouterService.Subscribe(action);
        }

        protected static void Unsubscribe<TType>(Action<TType> action)
        {
            DataRouterService.Unsubscribe(action);
        }

        #endregion
        
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
        
        #endregion

        #region Virtual Lifecycle

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
}