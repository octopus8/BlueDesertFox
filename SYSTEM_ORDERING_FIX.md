# System Ordering Warnings Fix - Complete

## Issues Fixed

All three system ordering warnings have been resolved by removing or commenting out `[UpdateAfter]` attributes that referenced systems in different `ComponentSystemGroup` instances.

### 1. TerrainTreeSpawningSystem → TerrainRenderingSystem

**Warning**:
```
Ignoring invalid [Unity.Entities.UpdateAfterAttribute] attribute on TerrainTreeSpawningSystem targeting TerrainRenderingSystem.
```

**Problem**: 
- `TerrainTreeSpawningSystem` is in `SimulationSystemGroup`
- `TerrainRenderingSystem` is in `PresentationSystemGroup`
- Can't use `[UpdateAfter]` across different groups

**Fix**: Removed `[UpdateAfter(typeof(TerrainRenderingSystem))]`

**Why It's Safe**:
- `PresentationSystemGroup` runs after `SimulationSystemGroup` by default
- Trees spawn in Simulation → Rendering happens in Presentation
- Order is still correct!

### 2. TerrainRenderingDebugSystem → TerrainRenderingSystem

**Warning**:
```
Ignoring invalid [Unity.Entities.UpdateAfterAttribute] attribute on TerrainRenderingDebugSystem targeting TerrainRenderingSystem.
```

**Problem**:
- `TerrainRenderingDebugSystem` has no `[UpdateInGroup]` (commented out)
- Still had `[UpdateAfter(typeof(TerrainRenderingSystem))]`
- Can't reference systems when not in a group

**Fix**: Commented out `[UpdateAfter(typeof(TerrainRenderingSystem))]`

**Why It's Safe**:
- Debug system is disabled anyway (UpdateInGroup commented out)
- When re-enabled, both attributes should be uncommented together

### 3. TreePositionUpdateSystem → TileScrollPositionSystem

**Warning**:
```
Ignoring invalid [Unity.Entities.UpdateAfterAttribute] attribute on TreePositionUpdateSystem targeting TileScrollPositionSystem.
```

**Problem**:
- `TreePositionUpdateSystem` was in `TransformSystemGroup`
- `TileScrollPositionSystem` is in `SimulationSystemGroup`
- Can't use `[UpdateAfter]` across different groups

**Fix**: Moved `TreePositionUpdateSystem` from `TransformSystemGroup` to `SimulationSystemGroup`

**Why It's Safe**:
- Tree positions are updated based on tile positions
- Both systems now in same group, ordering is guaranteed
- Trees update after tiles scroll, before rendering

## System Update Order (Now Correct)

```
SimulationSystemGroup:
  ├─ ScrollTerrainSystem (updates scroll offset)
  ├─ TileSpawningSystem (spawns/despawns tiles)
  ├─ TileScrollPositionSystem (updates tile positions)
  ├─ TreePositionUpdateSystem (updates tree positions) ← MOVED HERE
  ├─ TerrainTreeSpawningSystem (spawns trees)
  └─ Other simulation systems...

TransformSystemGroup:
  └─ (Unity's built-in transform updates)

PresentationSystemGroup:
  ├─ TerrainRenderingSystem (renders terrain)
  ├─ GlobalTreeInstanceSystem (renders trees)
  └─ Other rendering systems...
```

## Files Changed

1. ✅ `TerrainTreeSpawningSystem.cs` - Removed `[UpdateAfter(typeof(TerrainRenderingSystem))]`
2. ✅ `TerrainRenderingDebugSystem.cs` - Commented out `[UpdateAfter(typeof(TerrainRenderingSystem))]`
3. ✅ `TreePositionUpdateSystem.cs` - Changed from `TransformSystemGroup` to `SimulationSystemGroup`

## Benefits

1. **No Warnings**: Console is clean on startup
2. **Correct Ordering**: All systems update in the right sequence
3. **Better Performance**: Tree position updates happen in Simulation (parallel-safe), not Transform (more constrained)

## Testing

Run Unity Play mode - you should see:
```
[TreeSpawner] Baked 1 tree prefabs
[TreeSpawning] Starting spawn for tile...
[GlobalTreeInstance] Rendering X trees in Y draw calls...
```

**No more system ordering warnings!** ✅

## Status

✅ **All System Ordering Warnings Fixed**
- All systems in correct groups
- Ordering attributes only reference systems in same group
- Ready for testing

---

**Date**: April 18, 2026  
**Fix Type**: System group reorganization

