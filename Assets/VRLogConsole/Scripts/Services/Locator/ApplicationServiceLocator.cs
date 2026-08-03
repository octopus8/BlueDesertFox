using VRLogConsole.Scripts.Models.Log.Impl;
using VRLogConsole.Scripts.Models.PersistantStore.Impl;
using VRLogConsole.Scripts.Models.Settings.Impl;
using VRLogConsole.Scripts.Services.Locator.Base.Impl;

namespace VRLogConsole.Scripts.Services.Locator
{
    public class ApplicationServiceLocator : ServiceLocator
    {
        protected override void CreateModels()
        {
            AddModel<ConfigurationModel>();
            AddModel<LogModel>();
            AddModel<PersistantStoreModel>();
        }
    }
}
