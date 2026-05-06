# Tree Buffer Issue - Diagnostic Results

## Problem Confirmed

**Symptom**: Tree entities keep accumulating (1914 → 12969+) and never decrease.

**Root Cause Found**: The `SpawnedTreeReference` buffer is **EMPTY** on all tiles!

## Evidence from Logs

```
[TileSpawning] Despawning tile at int2(4, -6), Entity: 447
[TileSpawning] Found 0 trees to destroy for tile int2(4, -6)
```

**Every single despawned tile reports 0 trees in the buffer**, yet:
- Tree count is growing: 1914 → 4238 → 6472 → 8669 → 10800 → 12969
- Tiles with `TreesSpawned` tag growing: 21 → 30 → 39 → 45 → 51 → 57
- **Orphaned trees: 0** (trees still have valid `TreeTileOwnership` references)

## The Issue

Trees ARE being spawned and tracked via `TreeTileOwnership`, but they're **NOT being added to the `SpawnedTreeReference` buffer** for cleanup!

This means:
1. ✅ Trees spawn correctly
2. ✅ `TreeTileOwnership` component is added
3. ✅ `TreesSpawned` tag is added to tiles
4. ❌ **`SpawnedTreeReference` buffer stays EMPTY**
5. ❌ Trees never get cleaned up when tiles despawn

## Next Steps

### Added Debug Logging

Modified `TerrainTreeSpawningSystem.cs` to log:
1. When spawning starts for each tile
2. How many trees should spawn
3. How many actually spawn
4. Buffer state before/after adding trees
5. If all trees were filtered out

### Run the Scene Again

**Watch for these log messages:**
```
[TreeSpawning] Starting spawn for tile int2(X, Y)...
[TreeSpawning] Tile int2(X, Y) will spawn N trees...
[TreeSpawning] Tile int2(X, Y) spawned N trees, adding to buffer...
[TreeSpawning] Buffer after adding trees - length: N
```

### Possible Root Causes

1. **All trees filtered out**: Height/slope filters might be too restrictive
   - Look for: `[TreeSpawning] NO TREES SPAWNED! All filtered out.`

2. **Buffer add code not executing**: Logic error preventing buffer population

3. **Config issue**: `minTreesPerTile` = `maxTreesPerTile` = 0
   - Check `TreeSpawnerConfigAuthoring` Inspector values

4. **Prefab issue**: Tree prefabs not set up correctly

## What to Look For

### Good Output (Working):
```
[TreeSpawning] Tile int2(0, 0) spawned 8 trees, adding to buffer...
[TreeSpawning] Buffer after adding trees - length: 8
[TileSpawning] Found 8 trees to destroy for tile int2(0, 0)
```

### Bad Output (Problem):
```
[TreeSpawning] Tile int2(0, 0) - NO TREES SPAWNED! All filtered out.
```
OR
```
[TreeSpawning] Tile int2(0, 0) spawned 0 trees, adding to buffer...
```

## Immediate Actions

1. **Run the scene**
2. **Check Console** for new `[TreeSpawning]` messages
3. **Report findings**: How many trees per tile? Are they being filtered out?

## Files Modified

- `TerrainTreeSpawningSystem.cs` - Added comprehensive debug logging
- `TREE_BUFFER_ISSUE_DIAGNOSIS.md` - This document

## Expected Resolution

Once we see the logs, we'll know if:
- Trees aren't spawning due to filters → Adjust filter settings
- Buffer isn't being populated → Fix buffer add code
- Config is wrong → Fix TreeSpawnerConfigAuthoring settings

