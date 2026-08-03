using System;
using System.Collections.Generic;
using UnityEngine;
using VRLogConsole.Scripts.Models.Base;

namespace VRLogConsole.Scripts.Services.Locator.Base.Impl
{
    public abstract class ServiceLocator : MonoBehaviour, IServiceLocator
    {
        private bool _modelsCreated;
        private readonly List<IModel> _allServices = new List<IModel>();
        private readonly Dictionary<Type, IModel> _serviceCache = new Dictionary<Type, IModel>();

        public event Action OnServicesInitialize;
        public event Action OnServicesDisposed;
        
        public TModel Get<TModel>() where TModel : class, IModel
        {
            if (!_modelsCreated)
            {
                _modelsCreated = true;
                CreateAndInitializeModels();
            }
            
            var key = typeof(TModel);
            if (_serviceCache.ContainsKey(key))
            {
                return (TModel)_serviceCache[key];
            }
            
            foreach (var model in _allServices)
            {
                if (model is TModel model1)
                {
                    _serviceCache[key] = model1;
                    return model1;
                }
            }

            throw new KeyNotFoundException(key.Name + " not found. Was it added to the ModelLocator?");
        }

        #region Model Lifecycle

        protected abstract void CreateModels();
        
        protected void AddModel<TModel>() where TModel : IModel, new()
        {
            var model = new TModel();
            AddModel(model);
        }

        private void AddModel(IModel model)
        {
            model.Initialize(this);
            _allServices.Add(model);
        }
        
        private void OnDestroy()
        {
            DisposeModels();
        }

        private void CreateAndInitializeModels()
        {
            CreateModels();
            RegisterModelBehaviours();
            InitializeModels();
        }

        private void RegisterModelBehaviours()
        {
            var modelBehaviours = GetComponentsInChildren<IModel>();
            foreach (var model in modelBehaviours)
            {
                AddModel(model);
            }
        }

        private void InitializeModels()
        {
            OnServicesInitialize?.Invoke();
        }

        private void DisposeModels()
        {
            OnServicesDisposed?.Invoke();
        }

        #endregion
    }
}
