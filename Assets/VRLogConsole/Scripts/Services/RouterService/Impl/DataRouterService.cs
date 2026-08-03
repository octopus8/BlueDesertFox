using System;

namespace VRLogConsole.Scripts.Services.RouterService.Impl
{
  public class DataRouterService : IDataRouterService
  {
    private static readonly RouterCollection Routers = new RouterCollection();

    public void Subscribe<TType>(Action<TType> action)
    {
      Routers.Subscribe(action);
    }

    public void Unsubscribe<TType>(Action<TType> action)
    {
      Routers.Unsubscribe(action);
    }

    public void Publish<TType>(TType data)
    {
      Routers.Publish(data);
    }

    public bool TryPublish<TInput, TOutput>(TInput data) where TOutput : class
    {
      if (!((object) data is TOutput))
      {
        return false;
      }
        
      Routers.Publish((object) data as TOutput);
      return true;
    }

    public void UnsubscribeAll<TType>()
    { 
      Routers.UnsubscribeAll<TType>();
    }

    public void UnsubscribeAll()
    {
      Routers.Clear();
    }
  }
}