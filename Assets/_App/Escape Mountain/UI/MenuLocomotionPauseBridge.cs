using Unity.Entities;
using UnityEngine;

/// <summary>
/// Syncs <see cref="UIManager"/> visibility to the <see cref="PlayerLocomotionPaused"/> ECS singleton
/// so Escape Mountain locomotion stops while the menu is open.
/// </summary>
public class MenuLocomotionPauseBridge : MonoBehaviour
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
            _entityManager.SetComponentData(_pausedEntity, new PlayerLocomotionPaused { Value = false });
        }
    }

    void OnVisibilityChanged(bool visible)
    {
        SetPaused(visible);
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

        using var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerLocomotionPaused>());
        if (query.TryGetSingletonEntity<PlayerLocomotionPaused>(out _pausedEntity))
        {
            _hasPausedEntity = true;
            return true;
        }

        _pausedEntity = _entityManager.CreateEntity();
        _entityManager.AddComponentData(_pausedEntity, new PlayerLocomotionPaused { Value = false });
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

        _entityManager.SetComponentData(_pausedEntity, new PlayerLocomotionPaused { Value = paused });
    }
}
