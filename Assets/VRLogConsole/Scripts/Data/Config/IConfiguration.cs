using VRLogConsole.Scripts.Types.Lock;
using VRLogConsole.Scripts.Types.Theme;

namespace VRLogConsole.Scripts.Data.Config
{
    public interface IConfiguration
    {
        bool LookAtPlayer { get; }
        LockType LockType { get; }
        bool SaveLogLocally { get; }
        float DistanceFromCamera { get; }
        ThemeType Theme { get; }
        void UpdateLock(LockType lockType);
        void UpdateTheme(ThemeType themeType);
        void UpdateLookAtPlayer(bool value);
    }
}
