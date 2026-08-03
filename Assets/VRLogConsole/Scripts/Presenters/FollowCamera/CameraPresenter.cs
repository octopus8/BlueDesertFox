using VRLogConsole.Scripts.Models.Settings;
using VRLogConsole.Scripts.Presenters.Base;
using VRLogConsole.Scripts.Types.Lock;

namespace VRLogConsole.Scripts.Presenters.FollowCamera
{
    public class CameraPresenter : PresenterBehaviour
    {
        public LockType LockType { get; private set; }
        public bool LookAtPlayer { get; private set; }
        public float DistanceFromCamera => Get<IConfigurationModel>().Configuration.DistanceFromCamera;

        protected override void Initialize()
        {
            Get<IConfigurationModel>().LockUpdated += OnLockUpdated;
            Get<IConfigurationModel>().LookAtPlayerUpdated += OnLookAtPlayerUpdated;
        }

        protected override void PostInitialize()
        {
            OnLockUpdated();
        }

        protected override void Dispose()
        {
            Get<IConfigurationModel>().LockUpdated -= OnLockUpdated;
            Get<IConfigurationModel>().LookAtPlayerUpdated -= OnLookAtPlayerUpdated;
        }
        
        private void OnLockUpdated()
        {
            LockType = Get<IConfigurationModel>().Configuration.LockType;
        }
        
        private void OnLookAtPlayerUpdated()
        {
            LookAtPlayer = Get<IConfigurationModel>().Configuration.LookAtPlayer;
        }
    }
}
