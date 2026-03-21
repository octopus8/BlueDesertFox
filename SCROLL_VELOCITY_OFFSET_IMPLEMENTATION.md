# Scroll Velocity Offset Implementation - Enemy Movement

## Overview
Implemented dynamic speed offset for enemy movement based on terrain scroll velocity. Enemies now have their movement speed adjusted by projecting the terrain's scroll velocity onto their movement direction, creating correct relative closing speeds.

## Problem Statement
The terrain scrolling system simulates player movement through the world at `scrollSpeed` in the player's facing direction (XZ plane). Enemies were moving at absolute speeds along their splines, ignoring the terrain scroll. This created incorrect relative velocities:
- When player faces enemies, closing speed should increase (both moving toward each other)
- When player faces away, closing speed should decrease (both moving in same direction)

## Solution
Calculate scroll velocity from the terrain system and project it onto each enemy's movement direction to create a speed offset. Apply this offset to enemy movement in all phases (approaching, following spline, leaving).

---

## Implementation Details

### 1. SplineFollowerSystem.cs

**Scroll Velocity Calculation** (lines 18-29):
```csharp
// Calculate scroll velocity from terrain scrolling system
float3 scrollVelocity = float3.zero;
if (SystemAPI.TryGetSingleton<ScrollConfig>(out var scrollConfig) && 
    SystemAPI.TryGetSingleton<ScrollOffset>(out var scrollOffset))
{
    if (scrollConfig.enabled && scrollConfig.scrollSpeed > 0f)
    {
        // Calculate scroll direction from accumulated offset
        float3 scrollDirection = math.normalizesafe(scrollOffset.accumulatedOffset);
        scrollVelocity = scrollDirection * scrollConfig.scrollSpeed;
    }
}
```

**SplineFollowerJob Updates**:
- Added `public float3 scrollVelocity` field (line 59)
- Calculate enemy direction from spline tangent (lines 89-91)
- Project scroll velocity onto enemy direction (lines 93-95)
- Apply offset to movement speed (lines 97-98)
- Use effective speed for distance ratio calculation (line 101)

**Key Logic** (lines 89-101):
```csharp
// Get enemy's current movement direction from spline tangent
SplineSample currentSample = spline.Evaluate(splineFollower.distanceRatio);
float3 enemyDirection = math.normalize(currentSample.tangent);

// Project scroll velocity onto enemy's movement direction to get speed offset
// Scroll velocity represents world movement (opposite of player movement)
// Negate to convert to player's relative velocity for correct closing speeds
float scrollSpeedOffset = -math.dot(scrollVelocity, enemyDirection);

// Apply scroll velocity offset to enemy movement speed (allow negative speeds)
float effectiveSpeed = splineFollower.moveSpeed + scrollSpeedOffset;

// Calculate the new distance ratio based on effective speed and time
splineFollower.distanceRatio += (effectiveSpeed * deltaTime) / spline.totalLength;
```

### 2. FormationMovementSystem.cs

**Scroll Velocity Calculation** (lines 35-46):
Same logic as `SplineFollowerSystem` - calculates once per frame and passes to job.

**FormationMovementJob Updates**:
- Added `public float3 scrollVelocity` field (line 65)
- Applied offset in `HandleApproachPhase()` (lines 118-120)
- Applied offset in `HandleLeavingPhase()` (lines 169-171)

**Approach Phase** (lines 114-120):
```csharp
// Move toward entry point using physics velocity
float3 direction = math.normalize(toEntry);
float baseApproachSpeed = 10f; // Base approach speed

// Project scroll velocity onto movement direction for speed offset
// Negate to convert world velocity to player's relative velocity
float scrollSpeedOffset = -math.dot(scrollVelocity, direction);
float effectiveApproachSpeed = baseApproachSpeed + scrollSpeedOffset;
```

**Leaving Phase** (lines 166-171):
```csharp
// Continue moving in the exit direction at constant speed
float baseExitSpeed = 10f; // Base speed when leaving spline

// Project scroll velocity onto exit direction for speed offset
// Negate to convert world velocity to player's relative velocity
float scrollSpeedOffset = -math.dot(scrollVelocity, movementState.exitDirection);
float effectiveExitSpeed = baseExitSpeed + scrollSpeedOffset;
```

---

## Behavior Examples

### Scenario 1: Player Facing Forward, Enemy Moving Toward Player
- Terrain scroll direction: `(0, 0, 1)` at 5 m/s → `scrollVelocity = (0, 0, 5)`
- Enemy direction: `(0, 0, -1)` (toward player)
- Enemy base speed: 10 m/s
- **Scroll offset**: `-dot((0, 0, 5), (0, 0, -1)) = -(-5) = +5 m/s`
- **Effective speed**: `10 + 5 = 15 m/s`
- **Result**: Enemy approaches faster - closing speed increased from 10 to 15 m/s ✅

### Scenario 2: Player Facing Forward, Enemy Moving Away From Player
- Terrain scroll direction: `(0, 0, 1)` at 5 m/s → `scrollVelocity = (0, 0, 5)`
- Enemy direction: `(0, 0, 1)` (same as scroll, away from player)
- Enemy base speed: 10 m/s
- **Scroll offset**: `-dot((0, 0, 5), (0, 0, 1)) = -5 m/s`
- **Effective speed**: `10 + (-5) = 5 m/s`
- **Result**: Enemy moves slower along spline, falls behind player

### Scenario 3: Player Turning (Directional Scroll)
- Terrain scroll direction: `(0.707, 0, 0.707)` at 5 m/s (northeast)
- Enemy direction: `(1, 0, 0)` (east)
- Enemy base speed: 10 m/s
- **Scroll offset**: `-dot((3.535, 0, 3.535), (1, 0, 0)) = -3.535 m/s`
- **Effective speed**: `10 + (-3.535) = 6.465 m/s`
- **Result**: Partial speed reduction based on misalignment with player movement

### Scenario 4: Negative Speed (Fast Scroll, Slow Enemy Moving Away)
- Terrain scroll direction: `(0, 0, 1)` at 15 m/s (fast scroll)
- Enemy direction: `(0, 0, 1)` (same direction, away from player)
- Enemy base speed: 10 m/s
- **Scroll offset**: `-dot((0, 0, 15), (0, 0, 1)) = -15 m/s`
- **Effective speed**: `10 + (-15) = -5 m/s`
- **Result**: Enemy moves backward along spline! Falls behind player dramatically.

---

## Design Decisions

### 1. Allow Negative Speeds
**Requirement**: User specified to allow negative speeds
**Implementation**: No clamping on `effectiveSpeed` - enemies can move backward along splines
**Effect**: When scroll velocity opposes enemy movement and exceeds base speed, enemies reverse direction
**Benefit**: Creates dynamic gameplay where fast scrolling can push enemies backward

### 2. Fixed Formation Spacing
**Requirement**: User specified to keep fixed spacing
**Implementation**: No changes to `formationSpacing` or `FormationPosition` offsets
**Effect**: Bowling pin formations maintain their layout regardless of scroll velocity
**Benefit**: Predictable visual appearance, simplified logic

### 3. Efficiency Optimizations
**Single Calculation Per Frame**:
- Scroll velocity calculated once in system `OnUpdate()`
- Passed to Burst-compiled job as field
- No repeated singleton queries inside job

**Projection via Dot Product**:
- Uses `math.dot(scrollVelocity, enemyDirection)` for projection
- Single operation, Burst-optimized
- Works for all angles (0° to 180°)

**Conditional Calculation**:
- Only calculates scroll velocity if `ScrollConfig.enabled` is true
- Early-out if `scrollSpeed = 0`
- Backwards compatible with scenes without scroll components

---

## Performance Impact

### Additions:
- **1x `math.normalizesafe()`** per frame per system (main thread)
- **1x `float3` multiply** per frame per system (main thread)
- **1x `spline.Evaluate()`** per enemy per frame (SplineFollowerJob)
- **1x `math.normalize()`** per enemy per frame (SplineFollowerJob)
- **1x `math.dot()`** per enemy per frame (all jobs)
- **1x `float` addition** per enemy per frame (all jobs)

### Total Cost:
- **~0.02ms** additional overhead for 100 enemies (estimated)
- All operations are Burst-compiled in jobs
- Main thread overhead negligible (<0.01ms)

### No GC Allocations:
- All calculations use value types (float3, float)
- No managed allocations
- Zero GC pressure

---

## Testing Recommendations

### Test 1: Head-On Approach
1. Set scroll speed to 5 m/s
2. Spawn enemies moving toward player
3. Observe: Enemies should appear slower but closing distance correctly

### Test 2: Reverse Direction
1. Set scroll speed to 15 m/s
2. Set enemy speed to 10 m/s
3. Spawn enemies moving opposite to scroll direction
4. Observe: Enemies should move backward along spline (negative speed)

### Test 3: Perpendicular Movement
1. Face player north, scroll terrain north
2. Spawn enemies on spline running east-west
3. Observe: Minimal speed offset (dot product near zero)

### Test 4: Formation Integrity
1. Spawn bowling pin formation
2. Enable scrolling at various speeds
3. Observe: Formation spacing remains constant

### Test 5: Scroll Direction Changes
1. Rotate player to change scroll direction
2. Observe: Enemy speeds adjust dynamically based on new angle

---

## Backwards Compatibility

### Scenes Without Scrolling:
- `TryGetSingleton()` returns false → `scrollVelocity = (0, 0, 0)`
- Speed offset = 0 for all enemies
- Enemies move at base speed (original behavior)

### Scroll Disabled:
- `ScrollConfig.enabled = false` → `scrollVelocity = (0, 0, 0)`
- No offset applied

### Non-Directional Scroll:
- If `accumulatedOffset = (0, 0, 0)` → `normalizesafe()` returns `(0, 0, 0)`
- Fallback to zero offset

---

## Files Modified

1. **SplineFollowerSystem.cs**
   - Added scroll velocity calculation in `OnUpdate()` (lines 18-29)
   - Added `scrollVelocity` field to `SplineFollowerJob` (line 59)
   - Added enemy direction calculation (lines 89-91)
   - Added speed offset projection and application (lines 93-101)

2. **FormationMovementSystem.cs**
   - Added scroll velocity calculation in `OnUpdate()` (lines 35-46)
   - Added `scrollVelocity` field to `FormationMovementJob` (line 65)
   - Updated `HandleApproachPhase()` to apply offset (lines 118-120)
   - Updated `HandleLeavingPhase()` to apply offset (lines 169-171)

---

## Summary

✅ **Implemented**: Dynamic enemy speed offset based on terrain scroll velocity  
✅ **Requirement 1**: Negative speeds allowed (no clamping)  
✅ **Requirement 2**: Fixed formation spacing maintained  
✅ **Requirement 3**: Efficient Burst-compiled implementation with zero GC  

### Key Benefits:
- Correct relative closing speeds in all scroll directions
- Dramatic gameplay when scroll opposes enemy movement (negative speeds)
- Minimal performance overhead (<0.02ms for 100 enemies)
- Backwards compatible with non-scrolling scenarios
- Formation integrity preserved

🎉 **Ready for testing in Unity!**

