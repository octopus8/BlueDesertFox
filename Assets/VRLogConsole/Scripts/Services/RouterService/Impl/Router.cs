using System;

namespace VRLogConsole.Scripts.Services.RouterService.Impl
{ 
    public class Router<T> : IDataSubscriber<T>
    {
        private event Action<T> Actions;

        public void Subscribe(Action<T> action)
        {
            Actions += action;
        }

        public void Unsubscribe(Action<T> action)
        {
            Actions -= action;
        }

        public void UnsubscribeAll()
        {
            Actions = null;
        }

        public void Publish(T data)
        {
            Actions?.Invoke(data);
        }

        public bool TryPublish(T data, out Exception exception)
        {
            exception = null;
            
            if (Actions != null)
            {
                try
                {
                    Actions(data);
                }
                catch (Exception ex)
                {
                    exception = ex;
                    return false;
                }
            }
            
            return true;
        }
    }
}