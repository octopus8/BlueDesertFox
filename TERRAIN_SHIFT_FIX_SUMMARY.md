# Terrain Visual Glitch Fix - Origin Shift Synchronization

## Problem
The terrain was disappearing for at least one frame during floating origin shifts when moving the XR Origin down the Z-axis.

## Root Cause
The `FloatingOriginSystem` runs **after** `LocalToWorldSystem` in the `TransformSystemGroup`:
1. `LocalToWorldSystem` updates all `LocalToWorld` rendering matrices based on current `LocalTransform` positions
2. `FloatingOriginSystem` then modifies `LocalTransform` positions (subtracting the shift offset)
3. The `LocalToWorld` matrices are now out of sync with the shifted `LocalTransform` positions
4. `PresentationSystemGroup` renders the terrain using the outdated `LocalToWorld` matrices (showing old positions)
5. **Next frame**: `LocalToWorldSystem` updates matrices again, terrain reappears at correct position

This one-frame delay caused visible flickering/disappearing of terrain during shifts.

## Solution
Modified `ShiftWorldOriginJob` in `FloatingOriginSystem.cs` to **immediately update `LocalToWorld` matrices** in the same job that shifts positions:

```csharp
public void Execute(ref LocalTransform transform, ref LocalToWorld localToWorld)
{
    // Shift the local position
    transform.Position -= offset;
    
    // Immediately update LocalToWorld matrix to prevent one-frame visual glitch
    localToWorld.Value = float4x4.TRS(
        transform.Position,
        transform.Rotation,
        transform.Scale
    );
}
```

### Key Changes
1. Added `ref LocalToWorld localToWorld` parameter to `Execute()` method
2. Manually recalculate the world matrix using `float4x4.TRS()` after position shift
3. Updated documentation to reflect the synchronous matrix update

## Technical Details
- The job already runs synchronously via `.Run()` (not `.ScheduleParallel()`), so there's no performance penalty
- The matrix calculation uses `TRS` (Translation, Rotation, Scale) which is correct for root-level terrain tile entities without parents
- The `[WithAll(typeof(FloatingOriginEnabled))]` attribute ensures only entities tagged for floating origin shifts are processed
- Both `LocalTransform` and `LocalToWorld` are now updated atomically in the same job execution

## Testing
To verify the fix:
1. Open the "Ace of Ages" scene in Unity
2. Enter Play mode
3. Move the XR Origin GameObject down the Z-axis (towards negative Z)
4. Observe terrain tiles as origin shift occurs (at shiftThreshold distance)
5. Terrain should remain visible throughout the shift (no flickering or disappearing)
6. Check Console logs to confirm shift offset values show correct Z-only movement

## Files Modified
- `Assets/_App/Ace of Ages/Terrain/FloatingOriginSystem.cs` (lines 99-125)

## Date
March 15, 2026

