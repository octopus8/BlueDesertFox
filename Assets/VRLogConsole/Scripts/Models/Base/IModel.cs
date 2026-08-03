using VRLogConsole.Scripts.Services.Locator.Base;

namespace VRLogConsole.Scripts.Models.Base
{
    public interface IModel
    {
        void Initialize(IServiceLocator serviceLocator);
    }
}
