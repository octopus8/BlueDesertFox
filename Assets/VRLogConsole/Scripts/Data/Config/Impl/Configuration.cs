using System;
using UnityEngine;
using VRLogConsole.Scripts.Types.Lock;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Data.Config.Impl
{
    [Serializable]
    public class Configuration : IConfiguration
    {
        [SerializeField] private ThemeType theme;
        [SerializeField] private LockType lockType;
        [SerializeField] private float distanceFromCamera = 3.0f;
        [SerializeField] private bool lookAtPlayer;
        [SerializeField] private bool saveLogLocally;

        public LockType LockType => lockType;
        public bool LookAtPlayer => lookAtPlayer;
        public bool SaveLogLocally => saveLogLocally;
        public float DistanceFromCamera => distanceFromCamera;
        public ThemeType Theme => theme;
        
        public void UpdateTheme(ThemeType themeType)
        {
            theme = themeType;
        }

        public void UpdateLock(LockType lockState)
        {
            lockType = lockState;
        }

        public void UpdateLookAtPlayer(bool value)
        {
            lookAtPlayer = value;
        }
        
    }
}
