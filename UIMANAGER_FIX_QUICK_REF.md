# UIManager Input Action Fix - Quick Reference

## Problem
Menu button doesn't work after scene transitions (especially AutoHand Demo scene).

## Solution
Two-part fix:

### 1. Code Fix (Already Complete ✅)
- **UIManager.cs** - Now self-heals when InputSystem.actions becomes null
- **InputSystemActionsInitializer.cs** - New component to initialize InputSystem.actions per scene

### 2. Scene Setup (Action Required ⚠️)

Add `InputSystemActionsInitializer` to scenes that need menu functionality:

#### Quick Steps:
1. Open scene in Unity
2. Create GameObject "Input Manager"
3. Add Component → `InputSystemActionsInitializer`
4. Assign: `Action Asset` = `InputSystem_Actions.inputactions`
5. Save scene

#### Scenes to Update:
- ✅ **Start Scene** - Check if already has input initialization
- ⚠️ **AutoHand Demo** - Needs InputSystemActionsInitializer added
- ⚠️ **UI Interaction Test** - Needs InputSystemActionsInitializer added
- ⚠️ **Any other scene using UIManager** - Add component

## Diagnosis

### Symptom: Menu button doesn't work
**Check console for:**
```
UIManager: InputSystem.actions is null. Menu toggle action cannot be initialized.
```

**Fix:** Add InputSystemActionsInitializer to the scene

### Symptom: No errors but menu still doesn't work
**Check:**
- Is InputSystemActionsInitializer in the scene?
- Is Action Asset field assigned to InputSystem_Actions.inputactions?
- Is the component enabled?

## Testing
After adding InputSystemActionsInitializer:
1. Press menu button → Should display menu
2. Check console → Should have NO warnings about InputSystem.actions
3. Navigate between scenes → Menu should work in all scenes

## Files Changed
- `Assets/_App/Scripts/UI/UIManager.cs` - Modified ✅
- `Assets/_App/Scripts/InputSystemActionsInitializer.cs` - Created ✅
- `AGENTS.md` - Updated ✅
- `UIMANAGER_INPUT_FIX.md` - Detailed docs ✅

## See Also
- Full documentation: `UIMANAGER_INPUT_FIX.md`
- Project guide: `AGENTS.md` (see "Common Pitfalls" section)

