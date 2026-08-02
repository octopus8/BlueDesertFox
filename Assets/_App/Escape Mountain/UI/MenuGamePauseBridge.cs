using Unity.Entities;
using UnityEngine;

/// <summary>
/// Syncs <see cref="UIManager"/> visibility to the <see cref="GamePaused"/> ECS singleton
/// so gameplay freezes while the menu is open (Ace of Ages and Escape Mountain).
/// </summary>
public class MenuGamePauseBridge : MonoBehaviour
{
    [SerializeField] private UIManager uiManager;

    EntityManager _entityManager;
    Entity _pausedEntity;
    bool _hasPausedEntity;
    bool _subscribed;

    void OnEnable()
    {
        ResolveUiManager();
        TrySubscribe();
        SyncFromUi();
    }

    void Start()
    {
        // ECS DefaultWorld may not exist yet during OnEnable.
        ResolveUiManager();
        TrySubscribe();
        SyncFromUi();
    }

    void OnDisable()
    {
        if (_subscribed && uiManager != null)
        {
            uiManager.VisibilityChanged -= OnVisibilityChanged;
            _subscribed = false;
        }

        if (_hasPausedEntity && World.DefaultGameObjectInjectionWorld is { IsCreated: true } world
            && world.EntityManager == _entityManager
            && _entityManager.Exists(_pausedEntity))
        {
            ClearPauseKeepingAccumulated(world.Time.ElapsedTime);
        }
    }

    void OnVisibilityChanged(bool visible, bool resumeGameplay)
    {
        if (visible)
            SetPaused(true);
        else if (resumeGameplay)
            SetPaused(false);
    }

    void ResolveUiManager()
    {
        if (uiManager == null)
            uiManager = FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
    }

    void TrySubscribe()
    {
        if (_subscribed || uiManager == null)
            return;

        uiManager.VisibilityChanged += OnVisibilityChanged;
        _subscribed = true;
    }

    void SyncFromUi()
    {
        if (uiManager == null)
            return;

        SetPaused(uiManager.IsVisible);
    }

    bool EnsurePausedEntity()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        _entityManager = world.EntityManager;

        using var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<GamePaused>());
        if (query.TryGetSingletonEntity<GamePaused>(out _pausedEntity))
        {
            _hasPausedEntity = true;
            return true;
        }

        _pausedEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(_pausedEntity, new GamePaused
        {
            Value = false,
            PauseStartedAt = -1.0,
            AccumulatedPauseDuration = 0.0
        });
        _hasPausedEntity = true;
        return true;
    }

    void SetPaused(bool paused)
    {
        if (!_hasPausedEntity)
        {
            if (!EnsurePausedEntity())
                return;
        }

        if (!_entityManager.Exists(_pausedEntity))
        {
            _hasPausedEntity = false;
            if (!EnsurePausedEntity())
                return;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        double now = world.Time.ElapsedTime;
        var current = _entityManager.GetComponentData<GamePaused>(_pausedEntity);

        if (paused)
        {
            if (!current.Value)
            {
                current.Value = true;
                current.PauseStartedAt = now;
            }
        }
        else
        {
            if (current.Value)
            {
                if (current.PauseStartedAt >= 0.0)
                    current.AccumulatedPauseDuration += now - current.PauseStartedAt;
                current.Value = false;
                current.PauseStartedAt = -1.0;
            }
        }

        _entityManager.SetComponentData(_pausedEntity, current);
    }

    void ClearPauseKeepingAccumulated(double now)
    {
        var current = _entityManager.GetComponentData<GamePaused>(_pausedEntity);
        if (current.Value && current.PauseStartedAt >= 0.0)
            current.AccumulatedPauseDuration += now - current.PauseStartedAt;
        current.Value = false;
        current.PauseStartedAt = -1.0;
        _entityManager.SetComponentData(_pausedEntity, current);
    }
}
