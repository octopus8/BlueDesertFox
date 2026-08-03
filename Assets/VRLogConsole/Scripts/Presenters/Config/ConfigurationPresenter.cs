using UnityEngine;
using VRLogConsole.Scripts.Data.Config.Impl;
using VRLogConsole.Scripts.Models.Settings;
using VRLogConsole.Scripts.Presenters.Base;

namespace VRLogConsole.Scripts.Presenters.Config
{
    public class ConfigurationPresenter : PresenterBehaviour
    {
        [SerializeField] private Configuration configuration;
        
        protected override void Initialize()
        {
            Get<IConfigurationModel>().Initialize(configuration);
        }
    }
}
