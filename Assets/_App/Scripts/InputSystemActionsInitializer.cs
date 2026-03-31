using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Initializes the global InputSystem.actions reference for scenes that use InputSystem.actions.FindAction().
/// </summary>
/// <remarks>
/// This component should be added to scenes where UIManager or other components rely on InputSystem.actions.
/// Unity's Project-Wide Actions feature requires Input System package 1.11.0+ and must be configured in
/// Project Settings > Input System Package, but this provides a fallback for projects not using that feature.
/// </remarks>
public class InputSystemActionsInitializer : MonoBehaviour
{
    /// <summary>The Input Action Asset to set as the global InputSystem.actions reference.</summary>
    [Tooltip("The Input Action Asset to set as the global InputSystem.actions reference. Usually 'InputSystem_Actions'.")]
    [SerializeField] private InputActionAsset actionAsset;
    
    /// <summary>Whether to enable all actions in the asset on initialization.</summary>
    [Tooltip("Enable all actions in the asset when this component starts.")]
    [SerializeField] private bool enableActionsOnStart = true;
    

    private void Awake()
    {
        // Set the global InputSystem.actions reference if not already set.
        if (InputSystem.actions == null && actionAsset != null)
        {
            InputSystem.actions = actionAsset;
            Debug.Log($"InputSystemActionsInitializer: Set InputSystem.actions to '{actionAsset.name}'");
        }
        else if (InputSystem.actions == null)
        {
            Debug.LogWarning("InputSystemActionsInitializer: No actionAsset assigned. InputSystem.actions will remain null.");
        }
    }


    private void OnEnable()
    {
        // Enable all actions in the asset if requested.
        if (enableActionsOnStart && actionAsset != null)
        {
            actionAsset.Enable();
        }
    }
    
    
    private void OnDisable()
    {
        // Optionally disable actions when this component is disabled.
        // Note: This is commented out because other components may still be using the actions.
        // If you want to disable actions when the scene unloads, uncomment the following:
        // if (actionAsset != null)
        // {
        //     actionAsset.Disable();
        // }
    }
}

