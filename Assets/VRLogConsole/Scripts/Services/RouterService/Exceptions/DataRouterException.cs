using System;

namespace VRLogConsole.Scripts.Services.RouterService.Exceptions
{
    public class DataRouterException : Exception
    {
        public DataRouterException(Type type, Exception innerException)
            : base($"Uncaught error routing data of type {(object) type}", innerException)
        {
        }
    }
}