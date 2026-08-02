using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

/// <summary>
/// Checks input and sets the BulletShooter.doShoot flag on the player ship entity when the fire button is pressed and the fire rate cooldown has elapsed.
/// </summary>
/// <remarks>
/// - Attach this to a GameObject in the main scene (not in SubScene).
/// - Waits for PlayerShip entity to be created from SubScene baking before initializing.
/// - Requires InputSystem.actions to be initialized (add InputSystemActionsInitializer to scene).
/// </remarks>
public class PlayerShootingInput : MonoBehaviour
{
    /// <summary> The player ship entity with the BulletShooter component. This is looked up at runtime from the ECS world after SubScene baking completes, since the entity doesn't exist at Start(). </summary>
    private Entity _playerShipEntity;
    
    /// <summary> Reference to the EntityManager for setting component data on the player ship entity. Cached at runtime after SubScene baking completes. </summary>
    private EntityManager _entityManager;
    
    /// <summary> Reference to the InputAction for firing. Looked up at runtime from InputSystem.actions after SubScene baking completes, since it may not be initialized at Start(). Requires InputSystemActionsInitializer or Project-Wide Actions to be set up in the project. </summary>
    private InputAction _fireAction;
    
    /// <summary> Flag indicating whether the component has completed initialization (found player ship entity and set up input action). Prevents Update() from processing input before ready. Set to true after successful initialization in InitializeWhenPlayerShipReady(). If initialization fails (e.g., player ship entity not found after max retries), the component is disabled and this flag remains false. </summary>
    private bool _initialized = false;

    
    /// <summary>
    /// Starts the coroutine that waits for the <see cref="PlayerShip"/> entity to be baked before binding input.
    /// </summary>
    void Start()
    {
        // Start coroutine to find entity (may take a few frames for SubScene to bake)
        StartCoroutine(InitializeWhenPlayerShipReady());
    }

    
    /// <summary>
    /// Re-enables the <c>Fire</c> input action when the component is enabled.
    /// </summary>
    void OnEnable()
    {
        if (_fireAction != null)
        {
            _fireAction.Enable();
        }
    }
    
    
    /// <summary>
    /// Disables the <c>Fire</c> input action when the component is disabled to prevent phantom input events.
    /// </summary>
    void OnDisable()
    {
        if (_fireAction != null)
        {
            _fireAction.Disable();
        }
    }
    
    
    /// <summary>
    /// Each frame, checks whether the <c>Fire</c> input action was pressed and, if the fire-rate
    /// cooldown has elapsed, sets <see cref="BulletShooter.doShoot"/> on the player ship entity.
    /// </summary>
    void Update()
    {
        if (!_initialized || _fireAction == null)
            return;

        if (GamePausedUtility.IsPaused())
            return;
        
        // Check if fire button was pressed this frame
        if (_fireAction.WasPressedThisFrame())
        {
            // Get current shooter state
            if (!_entityManager.Exists(_playerShipEntity))
            {
                Debug.LogWarning("[PlayerShootingInput] Player ship entity no longer exists");
                enabled = false;
                return;
            }
            
            var shooter = _entityManager.GetComponentData<BulletShooter>(_playerShipEntity);
            
            // Check fire rate limiting
            double currentTime = GamePausedUtility.GetGameplayElapsedTimeFromWorld();
            if (currentTime - shooter.lastFireTime >= shooter.fireRate)
            {
                shooter.doShoot = true;
                _entityManager.SetComponentData(_playerShipEntity, shooter);
            }
        }
    }
    
    
    /// <summary>
    /// Polls the ECS world each frame (up to 300 frames) until a <see cref="BulletShooter"/>/<see cref="PlayerShip"/>
    /// entity pair appears from SubScene baking, then calls <see cref="InitializeInputAction"/> and marks
    /// the component as ready. Disables the component if the entity never arrives.
    /// </summary>
    private IEnumerator InitializeWhenPlayerShipReady()
    {
        // Get ECS World
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            Debug.LogError("[PlayerShootingInput] No default ECS world found");
            enabled = false;
            yield break;
        }
        
        _entityManager = world.EntityManager;
        
        int retryCount = 0;
        const int maxRetries = 300; // 5 seconds at 60fps
        
        while (retryCount < maxRetries)
        {
            // Find player ship entity with BulletShooter component
            var query = _entityManager.CreateEntityQuery(typeof(BulletShooter), typeof(PlayerShip));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            
            if (entities.Length > 0)
            {
                _playerShipEntity = entities[0];
                entities.Dispose();
                query.Dispose();
                
                InitializeInputAction();
                
                _initialized = true;
                yield break;
            }
            
            entities.Dispose();
            query.Dispose();
            
            retryCount++;
            yield return null; // Wait one frame
        }
        
        // Failed to find entity after max retries
        Debug.LogError($"[PlayerShootingInput] Failed to find PlayerShip entity after {maxRetries} frames. " +
            "Make sure PlayerShipAuthoring and BulletShooterAuthoring are in the SubScene.");
        enabled = false;
    }

    
    /// <summary>
    /// Looks up the <c>Fire</c> action from <c>InputSystem.actions</c> and enables it along with
    /// its action map. Logs a warning and returns early if <c>InputSystem.actions</c> is null or
    /// the action is not found — requires <c>InputSystemActionsInitializer</c> in the scene.
    /// </summary>
    private void InitializeInputAction()
    {
        // Check if InputSystem.actions is available
        if (InputSystem.actions == null)
        {
            Debug.LogWarning("[PlayerShootingInput] InputSystem.actions is null. Fire action cannot be initialized. " +
                "Ensure InputSystemActionsInitializer is in the scene or configure Project-Wide Actions in Project Settings.");
            return;
        }
        
        // Find the Fire action
        _fireAction = InputSystem.actions.FindAction("Fire");
        
        if (_fireAction == null)
        {
            Debug.LogWarning("[PlayerShootingInput] 'Fire' action not found in InputSystem.actions. " +
                "Make sure to add a 'Fire' action to the InputSystem_Actions asset and map it to a button (e.g., right controller trigger).");
            return;
        }
        
        InputSystem.actions.Enable();
        _fireAction.actionMap.Enable();
        _fireAction.Enable();
    }
}
