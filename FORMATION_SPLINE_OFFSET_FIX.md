# Formation Spline Offset Fix

## Problem

When enemies transition to the `FollowingSpline` phase on a **non-closed spline**, enemies positioned behind the lead enemy (with negative `forwardOffset` values) incorrectly snapped to the spline start position, then moved alongside the lead enemy before spreading out into formation.

### Root Cause

In `SplineFollowerJob.Execute`, the formation offset calculation:

```csharp
adjustedDistanceRatio = splineFollower.distanceRatio + (formationPos.forwardOffset / spline.totalLength);
```

For enemies behind the lead:
- `splineFollower.distanceRatio` = 0.0 (lead position)
- `formationPos.forwardOffset` = -2.0, -4.0, -6.0, etc. (negative values for rear positions)
- Result: `adjustedDistanceRatio` becomes **negative** (-0.02, -0.04, -0.06, etc.)

For non-closed splines, the code then clamped this to 0:

```csharp
adjustedDistanceRatio = math.clamp(adjustedDistanceRatio, 0f, 1f);
```

This caused all rear enemies to evaluate at ratio 0 (spline start), collapsing the formation.

## Solution

Instead of clamping out-of-bounds ratios to `[0, 1]`, the fix **extends enemies beyond spline bounds along the spline tangent direction**:

### For Enemies Behind Spline Start (`rawAdjustedRatio < 0`)

1. Calculate how far behind the start: `offsetDistance = -rawAdjustedRatio * spline.totalLength`
2. Evaluate spline at ratio 0 to get start position and tangent
3. Extend **backward** along reversed tangent: `targetPosition = startPosition - tangent * offsetDistance`

### For Enemies Ahead of Spline End (`rawAdjustedRatio > 1`)

1. Calculate how far ahead of the end: `offsetDistance = (rawAdjustedRatio - 1.0) * spline.totalLength`
2. Evaluate spline at ratio 1 to get end position and tangent
3. Extend **forward** along tangent: `targetPosition = endPosition + tangent * offsetDistance`

### Key Changes

**File**: `Assets/_App/Ace of Ages/Splines/SplineFollowerSystem.cs`

1. Added `forwardOffsetDistance` variable to track the offset distance for out-of-bounds calculations
2. Added logic block after spline evaluation to handle non-closed splines with formation offsets
3. Calculate `rawAdjustedRatio` (unclamped) to detect out-of-bounds conditions
4. Override `targetPosition` and `sample` when extending beyond bounds

## Behavior

### Before Fix
```
Spline start ━━━━━━━━━━━━━━━→

Enemies at transition (all snap to start):
  [0][1][2][3][4][5][6][7][8][9]  ← All at ratio 0

After lead moves forward:
              [0]
             [1][2]
           [3][4][5]
         [6][7][8][9]  ← Formation spreads out
```

### After Fix
```
Spline start ━━━━━━━━━━━━━━━→

Enemies at transition (formation maintained):
       [0]
      [1][2]
    [3][4][5]
  [6][7][8][9]  ← Rear enemies positioned behind spline start

After lead moves forward:
              [0]
             [1][2]
           [3][4][5]
         [6][7][8][9]  ← Formation shape preserved
```

## Performance Impact

- **Minimal**: Additional calculations only execute for:
  - Entities with `FormationPosition` component
  - On non-closed splines
  - With out-of-bounds adjusted ratios

- **Overhead**: Two additional checks + one extra `spline.Evaluate()` call (at ratio 0 or 1) per out-of-bounds entity
- **Burst-compiled**: All logic remains within the Burst-compiled job

## Testing

1. **Scene**: Ace of Ages scene with non-closed spline
2. **Spawn formation**: Trigger enemy spawn with 10-pin bowling formation
3. **Expected behavior**: 
   - All enemies maintain formation spacing from spawn
   - Rear enemies start behind spline start, positioned correctly
   - No snapping or sudden movement during phase transitions
   - Formation flows smoothly onto and along the spline

## Edge Cases Handled

✅ Enemies behind spline start (negative offsets)  
✅ Enemies ahead of spline end (offsets beyond 1.0)  
✅ Lateral offset calculation uses correct tangent direction  
✅ Rotation faces spline tangent direction consistently  
✅ Backward compatibility: Entities without `FormationPosition` unaffected  
✅ Closed splines: Uses original wrapping logic (unchanged)

## Related Files

- `SplineFollowerSystem.cs` - Main fix implementation
- `FormationPosition.cs` - Formation offset component definition
- `EnemySpawnerSystem.cs` - Calculates initial formation offsets
- `FormationMovementSystem.cs` - Manages phase transitions
- `BOWLING_PIN_FORMATION.md` - Formation system documentation

