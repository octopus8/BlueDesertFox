using System;
using VRLogConsole.Scripts.Models.Base;

namespace VRLogConsole.Scripts.Services.Locator.Base
{
    public interface IServiceLocator
    {
        event Action OnServicesInitialize;
        event Action OnServicesDisposed;

        TModel Get<TModel>() where TModel : class, IModel;
    }
}
