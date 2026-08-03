using System;
using System.Collections.Generic;
using VRLogConsole.Scripts.Services.RouterService.Exceptions;

namespace VRLogConsole.Scripts.Services.RouterService.Impl
{
    public class RouterCollection
    {
        private readonly Dictionary<Type, object> _collection = new Dictionary<Type, object>();

        public void Subscribe<T>(Action<T> action)
        {
            GetOrCreateRouter<T>().Subscribe(action);
        }

        public void Unsubscribe<T>(Action<T> action)
        {
            var key = typeof (T);
            if (!_collection.ContainsKey(key))
            {
                return;
            }
               
            (_collection[key] as Router<T>).Unsubscribe(action);
        }

        public void UnsubscribeAll<T>()
        {
            var key = typeof (T);
            if (!_collection.ContainsKey(key))
            {
                return;
            }
               
            (_collection[key] as Router<T>).UnsubscribeAll();
        }

        public void Publish<T>(T data)
        {
            Exception exception;
            if (GetOrCreateRouter<T>().TryPublish(data, out exception))
            {
                return;
            }
                
            GetOrCreateRouter<DataRouterException>().Publish(new DataRouterException(typeof (T), exception));
        }

        public void Clear()
        {
            _collection.Clear();
        }

        private Router<T> GetOrCreateRouter<T>()
        {
            var key = typeof (T);
            if (!_collection.ContainsKey(key))
            {
                _collection[key] = new Router<T>();
            }
               
            return _collection[key] as Router<T>;
        }
    }
}