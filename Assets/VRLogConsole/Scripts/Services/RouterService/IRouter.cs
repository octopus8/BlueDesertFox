using System;

namespace VRLogConsole.Scripts.Services.RouterService
{
    public interface IRouter
    {
        void Subscribe<TMessage, TPayload>(TMessage message, Action<TPayload> handler);
        void UnSubscribe<TMessage, TPayload>(TMessage message, Action<TPayload> handler);
        void SendMessage<TMessage, TPayload>(TMessage message, TPayload payload);
        void Dispose();
    }
}
