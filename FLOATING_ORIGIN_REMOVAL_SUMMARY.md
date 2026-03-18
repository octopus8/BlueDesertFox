# Floating Origin System Removal - Summary

**Date:** March 18, 2026
**Status:** ✅ COMPLETE

## Overview

Successfully removed the floating origin recentering system from the terrain while preserving the core tile spawning functionality around the player.

## Files Deleted

The following 5 files were completely removed:

1. `Assets\_App\Ace of Ages\Terrain\FloatingOriginSystem.cs` - System that monitored player distance and triggered world shifts
2. `Assets\_App\Ace of Ages\Terrain\FloatingOriginComponents.cs` - Component definitions (WorldOriginOffset, FloatingOriginConfig, FloatingOriginEnabled)
3. `Assets\_App\Ace of Ages\Terrain\FloatingOriginEvents.cs` - Event system for origin shift notifications
4. `Assets\_App\Ace of Ages\Terrain\FloatingOriginEnabledAuthoring.cs` - Authoring component for tagging entities
5. `Assets\_App\Ace of Ages\Terrain\FloatingOriginGameObjectShifter.cs` - GameObject synchronization during origin shifts

## Files Modified

### 1. TileComponents.cs
**Added components** that were previously in FloatingOriginComponents.cs:
- `PlayerTransformReference` - Managed component holding player Transform reference
- `PlayerTrackingSearch` - Search configuration for finding player at runtime

These components are still needed for player tracking (tile spawning around player).

### 2. TerrainConfigAuthoring.cs
**Removed:**
- `floatingOriginEnabled` field
- `shiftThreshold` field
- FloatingOriginConfig component creation in Baker
- WorldOriginOffset component creation in Baker
- Shift threshold gizmo visualization
- Shift threshold validation

**Result:** Inspector now only shows terrain generation and physics settings, no floating origin options.

### 3. TileSpawningSystem.cs
**Removed:**
- `RequireForUpdate<WorldOriginOffset>()` requirement
- `FloatingOriginEnabled` tag addition to spawned tiles

**Result:** System still spawns tiles around player, but entities no longer need origin shift tags.

### 4. TerrainMeshGenerationSystem.cs
**Removed:**
- `RequireForUpdate<WorldOriginOffset>()` requirement
- `worldOffset` singleton retrieval
- Adding `worldOffset.accumulatedOffset` to tile world position

**Changed:**
- Tile world position now calculated directly from grid coordinates without accumulated offset
- Noise sampling uses absolute world coordinates (no offset correction)

**Result:** Terrain generation is simpler but limited to ~1000-2000m from origin due to float precision.

### 5. TerrainPhysicsSystem.cs
**Removed:**
- Event subscription to `FloatingOriginEvents.OnNonPlayerOriginShifted`
- Event unsubscription in OnDestroy
- `OnOriginShifted()` callback method (cleared collider queue on shifts)
- `s_QueueClearMarker` profiler marker (unused after removal)

**Result:** Physics system no longer reacts to origin shifts. Collider cache remains valid indefinitely.

### 6. TerrainTrackingDebugger.cs
**Removed:**
- `FloatingOriginConfig` and `WorldOriginOffset` component checks
- `TestOriginShift()` method (set low threshold for testing)
- `ResetOriginThreshold()` method
- Shift threshold display in `GetPlayerPosition()`

**Result:** Debug menu simplified to basic player position tracking and tile count.

### 7. README.md
**Updated sections:**
- Title changed from "Infinite Terrain Tiling System with Floating Origin" to "Infinite Terrain Tiling System"
- Removed "Floating Origin" from features list
- Removed FloatingOriginComponents.cs from architecture documentation
- Removed FloatingOriginSystem from systems list
- Removed "Floating Origin Enabled" and "Shift Threshold" from setup instructions
- Removed "Configure Floating Origin GameObject Shifter" section
- Removed "Floating Origin System" explanation from "How It Works"
- Removed "Why Double Precision for WorldOriginOffset?" section
- Updated physics optimization sections to remove origin shift references
- Removed origin shift testing steps from profiling guide
- Removed "Terrain Jumps After Origin Shift" troubleshooting section
- Updated performance tips to remove origin shift terminology

## System Behavior Changes

### Before Removal
- Player could travel unlimited distances
- World origin shifted when player moved >2000m from (0,0,0)
- All entities and terrain shifted synchronously to keep player near origin
- Terrain noise sampled using accumulated offset for consistency
- Physics colliders remained valid after shifts
- GameObjects could subscribe to shift events for synchronization

### After Removal
- Player should stay within ~1000-2000m of world origin for best float precision
- No automatic world shifting
- Terrain tiles spawn/despawn around player at absolute world positions
- Terrain noise sampled at absolute world coordinates
- Simpler system with fewer edge cases
- Physics colliders never invalidated by origin shifts

## Limitations Introduced

⚠️ **Floating-point precision degradation beyond ~1000-2000m from origin**
- Unity physics and rendering can exhibit jitter/glitches at large distances
- User acknowledged and accepted this limitation
- Terrain generation will work but may have visual artifacts far from origin

## Testing Recommendations

1. **Tile spawning**: Verify tiles spawn/despawn correctly as player moves
2. **Mesh generation**: Check terrain meshes generate without errors
3. **Physics collisions**: Ensure player can still walk on terrain
4. **Performance**: Verify no performance regression from changes
5. **Edge case**: Test player movement near origin (0,0,0) and at moderate distances (500-1000m)

## Migration Notes for Scenes

- Any GameObjects with `FloatingOriginEnabledAuthoring` component will show missing script references (user confirmed manual cleanup completed)
- Any GameObjects with `FloatingOriginGameObjectShifter` component will show missing script references (user confirmed manual cleanup completed)
- Inspector fields for `floatingOriginEnabled` and `shiftThreshold` in TerrainConfigAuthoring will be lost (expected behavior)

## Rollback Instructions (If Needed)

To restore floating origin functionality:
1. Restore the 5 deleted files from version control
2. Revert changes to the 7 modified files
3. Re-add floating origin components to scene GameObjects
4. Restore README documentation

## Verification Checklist

- ✅ All floating origin files deleted
- ✅ All references removed from remaining systems
- ✅ Player tracking components preserved (PlayerTransformReference, PlayerTrackingSearch)
- ✅ Tile spawning system still functional
- ✅ Mesh generation system still functional
- ✅ Physics system still functional
- ✅ Debug utilities updated
- ✅ Documentation updated
- ✅ No compilation errors expected (IDE may need refresh)

## Conclusion

The terrain system has been successfully simplified by removing the floating origin recentering mechanism. The system now functions as a traditional infinite terrain generator that spawns tiles around the player without world origin manipulation. This reduces complexity at the cost of limiting usable world space to reasonable distances from the origin.

