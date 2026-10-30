using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Charge-release jump for the Escape Mountain player follow object. Hold the Jump action
/// (right controller A) to charge for up to two seconds, then release to apply a world-up
/// velocity impulse. Strength scales linearly with hold time up to <see cref="maxJumpSpeed"/>.
/// </summary>
/// <remarks>
/// Attach to a GameObject in the main scene (not in a SubScene). Requires
/// <c>InputSystemActionsInitializer</c> or Project-Wide Actions so <c>InputSystem.actions</c>
/// is set. The impulse is consumed by <see cref="PlayerFollowObjectGroundContactSystem"/>.
/// </remarks>
public class PlayerJumpInput : MonoBehaviour
{
    [Tooltip("World-up speed (m/s) added at full charge.")]
    [Min(0f)]
    [SerializeField] private float maxJumpSpeed = 8f;

    [Tooltip("Hold duration in seconds that reaches full jump strength.")]
    [Min(0.01f)]
    [SerializeField] private float maxChargeTime = 2f;

    InputAction _jumpAction;
    EntityQuery _followObjectQuery;
    bool _hasFollowObjectQuery;
    bool _charging;
    float _chargeTime;
    bool _loggedInitWarning;

    void OnEnable()
    {
        TryInitializeJumpAction();
    }

    void OnDisable()
    {
        _charging = false;
        _chargeTime = 0f;

        if (_jumpAction != null)
            _jumpAction.Disable();

        DisposeFollowObjectQuery();
    }

    void Update()
    {
        if (_jumpAction == null)
            TryInitializeJumpAction();
        if (_jumpAction == null)
            return;

        if (GamePausedUtility.IsPaused())
        {
            if (_charging && _jumpAction.WasReleasedThisFrame())
            {
                _charging = false;
                _chargeTime = 0f;
            }
            return;
        }

        if (_jumpAction.WasPressedThisFrame())
        {
            _charging = true;
            _chargeTime = 0f;
        }

        if (!_charging)
            return;

        if (_jumpAction.IsPressed())
            _chargeTime = Mathf.Min(_chargeTime + Time.deltaTime, maxChargeTime);

        if (!_jumpAction.WasReleasedThisFrame())
            return;

        _charging = false;
        TryApplyJump();
        _chargeTime = 0f;
    }

    void TryApplyJump()
    {
        if (maxJumpSpeed <= 0f || maxChargeTime <= 0f)
            return;

        if (!TryGetFollowObject(out EntityManager em, out Entity entity))
            return;

        var brake = em.GetComponentData<PlayerFollowObjectBrakeState>(entity);
        if (brake.active != 0)
            return;

        var motion = em.GetComponentData<PlayerFollowObjectMotionState>(entity);
        if (motion.inContact == 0)
            return;

        float charge01 = Mathf.Clamp01(_chargeTime / maxChargeTime);
        float jumpSpeed = charge01 * maxJumpSpeed;
        if (jumpSpeed <= 0f)
            return;

        motion.pendingJumpSpeed = jumpSpeed;
        em.SetComponentData(entity, motion);
    }

    void TryInitializeJumpAction()
    {
        if (_jumpAction != null)
            return;

        if (InputSystem.actions == null)
        {
            if (!_loggedInitWarning)
            {
                Debug.LogWarning("[PlayerJumpInput] InputSystem.actions is null. Jump cannot be initialized. " +
                    "Ensure InputSystemActionsInitializer is in the scene or configure Project-Wide Actions.");
                _loggedInitWarning = true;
            }
            return;
        }

        _jumpAction = InputSystem.actions.FindAction("Jump");
        if (_jumpAction == null)
        {
            if (!_loggedInitWarning)
            {
                Debug.LogWarning("[PlayerJumpInput] 'Jump' action not found in InputSystem.actions. " +
                    "Map Jump to the right controller primary button (A).");
                _loggedInitWarning = true;
            }
            return;
        }

        _jumpAction.actionMap?.Enable();
        _jumpAction.Enable();
    }

    bool TryGetFollowObject(out EntityManager em, out Entity entity)
    {
        em = default;
        entity = Entity.Null;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        em = world.EntityManager;
        EnsureFollowObjectQuery(em);
        if (!_hasFollowObjectQuery || _followObjectQuery.IsEmptyIgnoreFilter)
            return false;

        entity = _followObjectQuery.GetSingletonEntity();
        return true;
    }

    void EnsureFollowObjectQuery(EntityManager em)
    {
        if (_hasFollowObjectQuery && _followObjectQuery != default)
            return;

        _followObjectQuery = em.CreateEntityQuery(
            ComponentType.ReadOnly<PlayerFollowObjectTag>(),
            ComponentType.ReadWrite<PlayerFollowObjectMotionState>(),
            ComponentType.ReadOnly<PlayerFollowObjectBrakeState>());
        _hasFollowObjectQuery = true;
    }

    void DisposeFollowObjectQuery()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        bool worldOk = world != null && world.IsCreated;

        if (_hasFollowObjectQuery && worldOk && _followObjectQuery != default)
            _followObjectQuery.Dispose();

        _followObjectQuery = default;
        _hasFollowObjectQuery = false;
    }
}
