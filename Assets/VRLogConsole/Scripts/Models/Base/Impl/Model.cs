using VRLogConsole.Scripts.Services.Locator.Base;

namespace VRLogConsole.Scripts.Models.Base.Impl
{
    public abstract class Model : IModel
    {
        private IServiceLocator _serviceLocator;

        public void Initialize(IServiceLocator serviceLocator)
        {
            _serviceLocator = serviceLocator;

            _serviceLocator.OnServicesInitialize += DoInitialize;
            _serviceLocator.OnServicesDisposed += DoDispose;
        }

        protected TModel Get<TModel>() where TModel : class, IModel
        {
            return _serviceLocator.Get<TModel>();
        }
        
        private void DoInitialize()
        {
            Initialize();
        }

        private void DoDispose()
        {
            _serviceLocator.OnServicesInitialize -= DoInitialize;
            _serviceLocator.OnServicesDisposed -= DoDispose;

            Dispose();
        }
        
        #region Lifecycle

        protected virtual void Initialize()
        {
        }

        protected virtual void Dispose()
        {
        }

        #endregion
    }
}