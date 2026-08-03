using System;

namespace VRLogConsole.Scripts.Services.RouterService
{
    public interface IDataRouterService
    {
        void Subscribe<TType>(Action<TType> action);

        void Unsubscribe<TType>(Action<TType> action);

        void Publish<TType>(TType data);

        bool TryPublish<TInput, TOutput>(TInput data) where TOutput : class;
    }
}