using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// MonoBehaviour that handles player shooting input.
/// Attach this to the player ship GameObject in the main scene (not in SubScene).
/// Requires InputSystem.actions to be initialized (add InputSystemActionsInitializer to scene).
/// </summary>
public class PlayerShootingInput : MonoBehaviour
{
    private Entity _playerShipEntity;
    private EntityManager _entityManager;
    private InputAction _fireAction;
    private bool _initialized = false;
    
    void Start()
    {
        // Get ECS World
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            Debug.LogError("[PlayerShootingInput] No default ECS world found");
            enabled = false;
            return;
        }
        
        _entityManager = world.EntityManager;
        
        // Find player ship entity with BulletShooter component
        var query = _entityManager.CreateEntityQuery(typeof(BulletShooter), typeof(PlayerShip));
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        if (entities.Length == 0)
        {
            Debug.LogError("[PlayerShootingInput] No entity found with BulletShooter + PlayerShip components. Make sure BulletShooterAuthoring is attached to the player ship.");
            entities.Dispose();
            enabled = false;
            return;
        }
        
        _playerShipEntity = entities[0];
        entities.Dispose();
        query.Dispose();
        
        // Initialize input action
        InitializeInputAction();
        
        _initialized = true;
        Debug.Log("[PlayerShootingInput] Initialized successfully");
    }
    
    void OnEnable()
    {
        if (_fireAction != null)
        {
            _fireAction.Enable();
        }
    }
    
    void OnDisable()
    {
        if (_fireAction != null)
        {
            _fireAction.Disable();
        }
    }
    
    void Update()
    {
        if (!_initialized || _fireAction == null)
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
            double currentTime = Time.timeAsDouble;
            if (currentTime - shooter.lastFireTime >= shooter.fireRate)
            {
                // Trigger shoot
                shooter.doShoot = true;
                _entityManager.SetComponentData(_playerShipEntity, shooter);
                
                Debug.Log($"[PlayerShootingInput] Fire button pressed - triggered shoot");
            }
            else
            {
                // Still in cooldown
                float remainingCooldown = (float)(shooter.fireRate - (currentTime - shooter.lastFireTime));
                Debug.Log($"[PlayerShootingInput] Fire button pressed but still in cooldown ({remainingCooldown:F2}s remaining)");
            }
        }
    }
    
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
        
        _fireAction.Enable();
        Debug.Log("[PlayerShootingInput] Fire action initialized successfully");
    }
}

