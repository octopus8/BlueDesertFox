# Auto-Scrolling Terrain - Grid Calculation Fix

## Problem
Tiles were being removed properly, but new tiles beyond the initial furthest tiles along Z axis were not being created.

## Root Cause
The **grid coordinate calculation** used the player's actual position (z=0), so the system always checked the same grid range (e.g., grid -5 to +5). As scrolling progressed, tiles at higher grid coordinates (e.g., grid +6, +7, +8) were never checked for spawning.

## Example of the Problem

**Setup:**
- Player at world position z=0
- Tile size = 100m
- View distance = 500m
- Initial tiles: grid z=-5 to z=+5

**After 50m of scroll:**
- Player grid coord calculated from z=0 → grid z=0
- System checks grids -5 to +5 relative to player grid → grids -5 to +5
- Tile at grid z=6 never checked (should be at world z=550m)
- Even though it would enter view range soon!

## Solution Applied

Calculate an **effective player position** that includes scroll offset for determining which grids to check:

```csharp
// Calculate "effective" player position for grid determination
float3 effectivePlayerPosition = playerPosition;
effectivePlayerPosition.z += scrollOffset.accumulatedScrollZ;

// Use effective position to determine which grid tiles to check
int2 playerGridCoord = new int2(
    (int)math.floor(effectivePlayerPosition.x / config.tileSize),
    (int)math.floor(effectivePlayerPosition.z / config.tileSize)
);
```

## How It Works Now

**After 50m of scroll (5 m/s for 10 seconds):**
- Player actual position: z=0
- Effective position: z=0 + 50 = 50
- Player grid coord: grid z=0 (from z=50 with 100m tiles)
- System checks grids relative to grid z=0

**After 600m of scroll:**
- Player actual position: z=0
- Effective position: z=0 + 600 = 600
- Player grid coord: grid z=6 (from z=600 with 100m tiles)
- System checks grids: z=1 to z=11 (with view distance 500m = ±5 tiles)
- Tiles at grid z=11 spawn ahead, tiles at grid z=1 despawn behind

## Complete Logic Flow

```
1. Get actual player position: (0, 0, 0)
2. Get scroll offset: 600m
3. Calculate effective position: (0, 0, 600)
4. Calculate grid from effective position: grid (0, 6)
5. Check grids around effective grid: grid (0, 1) to (0, 11)
6. For each grid:
   a. Calculate scrolled tile position: gridZ * 100 - 600
   b. Check distance from actual player (at z=0)
   c. Spawn if within view distance
7. Result: Tiles continuously spawn ahead and despawn behind
```

## Before vs After

### Before Fix
```
Player at z=0 (always)
→ Player grid = 0 (always)
→ Check grids -5 to +5 (always)
→ No new tiles beyond grid +5
→ STUCK: Tiles run out after initial spawn
```

### After Fix
```
Player at z=0 (actual position)
Scroll offset = 600m
→ Effective position = 600m
→ Player grid = 6 (from effective position)
→ Check grids 1 to 11 (around effective grid)
→ New tiles spawn at grid +11, despawn at grid +1
→ CONTINUOUS: Infinite scrolling works!
```

## Key Insight

**Two different position concepts:**

1. **Effective Position** (for grid calculation):
   - Includes scroll offset
   - Determines WHICH grid tiles to check
   - Moves as terrain scrolls

2. **Actual Position** (for distance calculation):
   - Player's real world position
   - Used for distance checks
   - Stays fixed at player

This hybrid approach ensures the system checks the right grid tiles while keeping the player centered.

## Testing

With this fix, you should see:
1. ✅ Initial tiles spawn around player
2. ✅ All tiles scroll backward smoothly
3. ✅ **NEW**: Tiles continuously spawn ahead
4. ✅ Tiles continuously despawn behind
5. ✅ Endless scrolling effect works indefinitely

Enable scroll and let it run for 30+ seconds - tiles should keep appearing ahead forever!

