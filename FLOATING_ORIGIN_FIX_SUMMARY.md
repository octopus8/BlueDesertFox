# Floating Origin System Fix - Implementation Summary

**Date:** March 15, 2026  
**Issue:** Floating origin offset calculation was incorrect when moving XR Origin along Z-axis only

## Problem Description

When moving the XR Origin GameObject down the Z-axis, the floating origin system was calculating an offset that included X and Y components (e.g., `-(-1.93, 51.36, 2000.19)`) instead of just the Z-axis movement `(0, 0, 2000.19)`.

**Root Cause:** The system was calculating shift offset using the player's absolute position instead of the movement delta from their starting position. This caused shifts to include X and Y components even when only moving along the Z-axis.

## Solution Implemented

### Technical Explanation

The key fix is calculating the shift offset as the **delta movement** from the player's starting position, rather than using their absolute position:

```csharp
// Store starting position on first frame
if (!_initialized)
{
    _lastPlayerPosition = playerPosition;  // e.g., (1.93, 50, 0)
    _initialized = true;
}

// Calculate delta from starting position
float3 deltaFromStart = playerPosition - _lastPlayerPosition;  // e.g., (0, 0, 2000)
float distanceFromStart = math.length(deltaFromStart);         // e.g., 2000

// Check threshold based on distance moved, not absolute position
if (distanceFromStart > config.shiftThreshold)
{
    float3 shiftOffset = deltaFromStart;  // Only shifts the axes that moved!
    // Shift entities and player by delta
    // Player goes from (1.93, 50, 2000) back to (1.93, 50, 0)
}
```

**Why this works:**
- Starting position: `(1.93, 50, 0)` → stored in `_lastPlayerPosition`
- Move to: `(1.93, 50, 2000.18)`
- Delta: `(0, 0, 2000.18)` ← **only Z-axis changed**
- Shift by: `(0, 0, 2000.18)` ← **preserves XY offset**
- After shift: `(1.93, 50, 0)` ← **back to starting position**

### Key Changes

1. **Track Player Position Between Frames**
   - Added `_lastPlayerPosition` and `_initialized` fields to `FloatingOriginSystem`
   - Initialize to player's starting position on first frame
   - Sample position BEFORE any modifications occur

2. **Reorder Shift Operations**
   - Shift player GameObject directly in `FloatingOriginSystem` BEFORE firing event
   - Prevents `ObjectFollower` interference
   - Prevents double-shifting

3. **Exclude Player from GameObject Shifter**
   - `FloatingOriginGameObjectShifter` now retrieves player transform from ECS
   - Automatically skips player transform when shifting
   - Now only shifts non-player GameObjects (terrain decorations, particles, etc.)

4. **Rename Event for Clarity**
   - Renamed `OnOriginShifted` → `OnNonPlayerOriginShifted`
   - Added backwards-compatible obsolete properties
   - Updated documentation to clarify purpose

## Files Modified

### 1. FloatingOriginSystem.cs
- Added private fields: `_lastPlayerPosition`, `_initialized`
- Initialize tracking in `OnCreate()`
- Store starting position on first frame
- **Calculate shift offset as delta from starting position** (line 57-65)
- Shift player GameObject directly before firing event
- Update last position after shift
- Changed event call to `InvokeNonPlayerOriginShifted()`

### 2. FloatingOriginEvents.cs
- Renamed primary event: `OnOriginShifted` → `OnNonPlayerOriginShifted`
- Renamed invoke method: `InvokeOriginShifted()` → `InvokeNonPlayerOriginShifted()`
- Added obsolete wrappers for backwards compatibility
- Updated documentation

### 3. FloatingOriginGameObjectShifter.cs
- Added `using Unity.Entities;`
- Added `_playerTransform` field to cache player reference
- Modified `OnEnable()` to query and exclude player transform from ECS
- Added skip logic in `OnOriginShifted()` to exclude player
- Changed event subscription to `OnNonPlayerOriginShifted`
- Updated documentation and tooltips

## Expected Behavior After Fix

1. **Correct Offset Calculation**
   - When moving XR Origin only along Z-axis by 2000 units
   - System now shifts by `(0, 0, 2000.19)` instead of `(1.93, 51.36, 2000.19)`

2. **No Double-Shifting**
   - Player GameObject shifted once by `FloatingOriginSystem`
   - `FloatingOriginGameObjectShifter` automatically excludes player
   - Other GameObjects (if configured) still shift correctly

3. **Position Tracking**
   - System tracks player's starting position (captured on first frame)
   - Calculates shift based on **delta movement** from starting position, not absolute position
   - Distance check uses `math.length(currentPos - startingPos)` instead of `math.length(currentPos)`
   - Works correctly regardless of starting XY offset
   - After shift, player returns to their starting position (preserving XY offset)

## Testing Instructions

1. **Setup:**
   - Ensure XR Origin has `FloatingOriginGameObjectShifter` component
   - Remove XR Origin from `transformsToShift[]` array in Inspector (if present)
   - Set `debugLog = true` to see detailed logging

2. **Test Case:**
   - Start scene with XR Origin at any position (e.g., `(1.93, 51.36, 0)`)
   - Move XR Origin down Z-axis to exceed threshold (e.g., `Z = 2000`)
   - Observe log message

3. **Expected Log Output:**
   ```
   FloatingOriginSystem: Origin shifted by (0, 0, 2000.19), accumulated offset: (0, 0, 2000.19)
   FloatingOriginGameObjectShifter: Skipping player transform (XR Origin Hands (XR Rig)) - already shifted by FloatingOriginSystem
   ```

4. **Verify:**
   - XR Origin position should be near `(1.93, 51.36, 0)` after shift
   - Only Z-component shifted back toward origin
   - X and Y components preserved

## Backwards Compatibility

The fix maintains backwards compatibility:
- Legacy `OnOriginShifted` event still works (marked obsolete)
- Legacy `InvokeOriginShifted()` method still works (marked obsolete)
- Compiler warnings guide developers to use new names
- No breaking changes for existing subscribers

## Additional Notes

- **Player Transform Detection:** System automatically detects player from ECS `PlayerTransformReference` singleton
- **Manual Configuration:** Remove player from `transformsToShift[]` array in Inspector if previously configured
- **Event Purpose:** `OnNonPlayerOriginShifted` is now for shifting non-player GameObjects only
- **ObjectFollower Timing:** Player shift happens before `DeviceTracking.UpdateImmediate()` call to prevent interference

---

**Implementation Status:** ✅ Complete  
**Compilation Errors:** None (only minor style warnings)  
**Testing Required:** Yes - verify in VR scene with Z-axis movement

