using System;

namespace VRLogConsole.Scripts.Services.RouterService
{
    public interface IDataSubscriber<T>
    {
        void Subscribe(Action<T> action);

        void Unsubscribe(Action<T> action);

        void UnsubscribeAll();
    }
}
