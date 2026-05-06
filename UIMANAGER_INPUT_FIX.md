# UIManager Input Action Fix - Scene Transition Issue

## Problem
UIManager's menu button stopped working after navigating through scene transitions:
1. Start Scene → Press menu (works) 
2. Navigate to UI Interaction Test scene → Press menu (works)
3. Navigate to AutoHand Demo scene → Press menu (FAILS)

## Root Cause
When Unity loads a new scene with `LoadSceneMode.Single`, the previous scene's GameObjects are destroyed. The `InputSystem.actions` static reference can become null if the new scene doesn't have an `InputActionManager` or similar component to initialize it.

**Original Code Issue:**
```csharp
void Start()
{
    menuToggleAction = InputSystem.actions.FindAction("ToggleMenu"); 
    menuToggleAction.Enable();
}

void Update()
{
    if (menuToggleAction.WasPressedThisFrame()) // NullReferenceException if action is null
    {
        ToggleVisibility();
    }
}
```

`Start()` only runs once when the component is created. If `InputSystem.actions` becomes null after scene load, `menuToggleAction` becomes invalid and `Update()` fails silently.

## Solution Implemented

### 1. Moved Input Action Initialization to OnEnable()
Input actions are now initialized in `OnEnable()` instead of `Start()`, following the pattern used in `OpenXRControllerEvent.cs` and `InputActionEnabler.cs`. This ensures re-initialization whenever the component becomes active.

```csharp
private void OnEnable()
{
    InitializeInputAction();
}
```

### 2. Added OnDisable() Cleanup
Properly disable the action when the component is disabled to prevent input consumption when UI is inactive:

```csharp
private void OnDisable()
{
    if (menuToggleAction != null)
    {
        menuToggleAction.Disable();
    }
}
```

### 3. Created InitializeInputAction() Helper Method
Extracted initialization logic with proper null checks and warning messages:

```csharp
private void InitializeInputAction()
{
    if (InputSystem.actions == null)
    {
        Debug.LogWarning("UIManager: InputSystem.actions is null. Menu toggle action cannot be initialized.");
        menuToggleAction = null;
        return;
    }
    
    menuToggleAction = InputSystem.actions.FindAction("ToggleMenu");
    
    if (menuToggleAction == null)
    {
        Debug.LogWarning("UIManager: ToggleMenu action not found in InputSystem.actions.");
        return;
    }
    
    menuToggleAction.Enable();
}
```

### 4. Added Defensive Checks in Update()
Update() now validates and re-initializes the action if it becomes invalid:

```csharp
private void Update()
{
    // Check if the menu toggle action is valid. If not, try to re-initialize it.
    if (menuToggleAction == null || !menuToggleAction.enabled)
    {
        InitializeInputAction();
        
        // If still null after initialization attempt, skip input processing this frame.
        if (menuToggleAction == null)
        {
            return;
        }
    }
    
    // Handle menu toggle action.
    if (menuToggleAction.WasPressedThisFrame())
    {
        // ...existing logic...
    }
}
```

## Benefits

1. **Resilient to Scene Transitions**: UIManager can now survive across scene loads where `InputSystem.actions` might be reset
2. **Self-Healing**: Automatically attempts to re-initialize the action if it becomes invalid
3. **Better Diagnostics**: Clear warning messages when InputSystem is not properly configured
4. **Follows Unity Patterns**: Matches the OnEnable/OnDisable pattern used throughout the XR Interaction Toolkit samples
5. **No Breaking Changes**: Maintains all existing functionality while adding robustness

## Setup Instructions for Scenes

### Option 1: Add InputSystemActionsInitializer Component (Recommended)

For scenes that need UIManager to work (like AutoHand Demo, UI Interaction Test):

1. Open the scene in Unity Editor
2. Find or create a GameObject that persists for the scene lifetime (e.g., "Scene Manager" or add to existing player rig)
3. Add the `InputSystemActionsInitializer` component
4. In the Inspector, assign the `Action Asset` field to `Assets/InputSystem_Actions.inputactions`
5. Ensure `Enable Actions On Start` is checked (default)
6. Save the scene

**Where to add it:**
- **AutoHand Demo scene**: Add to "Auto Hand Player Container" GameObject or create new "Input Manager" object
- **UI Interaction Test scene**: Add to any persistent GameObject or the player rig
- **Any custom scene with UIManager**: Add to scene root or player object

### Option 2: Configure Project-Wide Actions (Alternative)

Alternatively, configure Unity's Project-Wide Actions feature:

1. Open **Edit → Project Settings → Input System Package**
2. In the "Input Settings" section, set **"Input Actions"** to `Assets/InputSystem_Actions.inputactions`
3. This automatically sets `InputSystem.actions` for all scenes globally
4. Requires Input System package 1.11.0 or higher

**Note:** Option 1 is simpler and gives per-scene control. Option 2 is more automatic but requires specific Unity/package versions and global configuration.

### Quick Fix for Existing Scenes

If you encounter the warning "InputSystem.actions is null" in console:

1. Create an empty GameObject in the scene (name it "Input Manager")
2. Add `InputSystemActionsInitializer` component
3. Drag `Assets/InputSystem_Actions.inputactions` to the `Action Asset` field
4. Menu button will now work correctly

## Testing Checklist

### Before Testing (Required Setup)
- [ ] Add `InputSystemActionsInitializer` component to AutoHand Demo scene (see Setup Instructions above)
- [ ] Add `InputSystemActionsInitializer` component to UI Interaction Test scene if not already present
- [ ] Verify InputSystem_Actions.inputactions is assigned in each InputSystemActionsInitializer

### Menu Button Functionality Tests
- [ ] Start Scene → Press menu button (should display menu)
- [ ] Navigate to UI Interaction Test → Press menu button (should display menu)
- [ ] Navigate to AutoHand Demo → Press menu button (should display menu) ✓ FIXED
- [ ] Verify NO warnings in console about InputSystem.actions being null
- [ ] Test multiple scene transitions back and forth (Start → UI Test → AutoHand → UI Test → Start)
- [ ] Verify menu button works immediately after scene load (not just after first failed press)
- [ ] Test menu button in each scene multiple times to ensure consistency

## Files Modified

- `Assets/_App/Scripts/UI/UIManager.cs`
  - Added `OnEnable()` method for input action initialization
  - Added `OnDisable()` method for cleanup
  - Added `InitializeInputAction()` helper method
  - Updated `Start()` to remove input initialization (now in OnEnable)
  - Updated `Update()` to check action validity and re-initialize if needed

## Files Created

- `Assets/_App/Scripts/InputSystemActionsInitializer.cs`
  - New component to set `InputSystem.actions` global reference at scene startup
  - Required for scenes that don't have Project-Wide Actions configured
  - Can be added to any scene that uses UIManager or other components relying on `InputSystem.actions`

## Related Issues Prevented

This fix also prevents similar issues with:
- UIManager persisting via DontDestroyOnLoad (if added in the future)
- Component being disabled/re-enabled during gameplay
- Input System package being reloaded during development
- Multiple UIManager instances across different scenes

## Technical Notes

**Why OnEnable vs Start?**
- `Start()` only runs once when the component is first created
- `OnEnable()` runs every time the component (or its GameObject) is enabled
- This ensures actions are re-initialized after scene loads, GameObject activations, or component re-enabling

**Why check `!menuToggleAction.enabled` in Update?**
- Input actions can be disabled externally by other systems
- The enabled check ensures we catch cases where the action exists but was disabled
- Prevents the need to wait for the action to become null before re-initialization

**InputSystem.actions Global Reference:**
- Set by Unity's Project-Wide Input Actions feature (Input System Package settings)
- Requires an `InputActionAsset` assigned in Project Settings
- Scenes may need an `InputActionEnabler` component to ensure actions are enabled on scene load




