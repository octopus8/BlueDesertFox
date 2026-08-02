using Unity.Entities;

/// <summary>
/// Singleton flag set by <see cref="MenuGamePauseBridge"/> when the in-game menu is open.
/// Gameplay systems early-out while <see cref="Value"/> is true. Pause duration is tracked so
/// TTL and cooldown clocks can use wall-clock-independent gameplay elapsed time.
/// </summary>
public struct GamePaused : IComponentData
{
    public bool Value;

    /// <summary>ElapsedTime when the current pause began, or -1 when not paused.</summary>
    public double PauseStartedAt;

    /// <summary>Total time spent paused across completed pause intervals.</summary>
    public double AccumulatedPauseDuration;
}

/// <summary>
/// Helpers for reading pause state and gameplay-relative elapsed time.
/// </summary>
public static class GamePausedUtility
{
    /// <summary>
    /// Returns wall elapsed time with accumulated (and active) pause duration subtracted,
    /// so cooldowns and lifetimes freeze while the menu is open.
    /// </summary>
    public static double GetGameplayElapsedTime(double wallElapsedTime, in GamePaused paused)
    {
        double pausedDuration = paused.AccumulatedPauseDuration;
        if (paused.Value && paused.PauseStartedAt >= 0.0)
            pausedDuration += wallElapsedTime - paused.PauseStartedAt;
        return wallElapsedTime - pausedDuration;
    }

    /// <summary>
    /// MonoBehaviour-safe read of gameplay elapsed time from the default world.
    /// Falls back to <see cref="UnityEngine.Time.timeAsDouble"/> if the world or singleton is missing.
    /// </summary>
    public static double GetGameplayElapsedTimeFromWorld()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return UnityEngine.Time.timeAsDouble;

        double t = world.Time.ElapsedTime;
        using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GamePaused>());
        if (!query.TryGetSingleton(out GamePaused p))
            return t;

        return GetGameplayElapsedTime(t, p);
    }

    /// <summary>
    /// Returns true when the gameplay pause singleton exists and is set.
    /// </summary>
    public static bool IsPaused()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<GamePaused>());
        return query.TryGetSingleton(out GamePaused p) && p.Value;
    }
}
