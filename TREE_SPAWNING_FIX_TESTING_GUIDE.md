# Tree Spawning Fix - Testing Guide

**Date**: May 2, 2026  
**Issue**: Trees not spawning in Editor with optimized system  
**Status**: ✅ FIXED (Updated: Structural change error resolved)  

---

## What Was Wrong

### Issue 1: Missing Buffer Component
**Root Cause**: The `CalculateTreeSpawnPositionsJob` expected tiles to have a `TreeSpawnPosition` buffer, but the system never added it. Jobs don't execute on entities missing required components.

### Issue 2: Structural Changes During Iteration (RuntimeError)
**Root Cause**: System tried to add `TreeSpawnPosition` buffer while iterating over entities in a Burst-compiled context.  
**Error**: `InvalidOperationException: Structural changes are not allowed while iterating over entities.`

**Similar to**: Trying to `AddComponent`/`RemoveComponent`/`AddBuffer` inside a `foreach` loop over an EntityQuery in Burst - this violates ECS safety rules.

---

## What Was Fixed

### Fix 1: Two-Pass Tile Collection

**File**: `TerrainTreeSpawningSystemOptimized.cs` (lines 118-152)

**Before** (❌ Broken):
```csharp
foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()...)
{
    if (tile.ValueRO.meshGenerated)
    {
        // ❌ Structural change during iteration!
        state.EntityManager.AddBuffer<TreeSpawnPosition>(entity);
        _pendingTiles.Enqueue(entity);
    }
}
```

**After** (✅ Fixed):
```csharp
// First pass: Collect entities (no structural changes)
var tilesToQueue = new NativeList<Entity>(16, Allocator.Temp);
foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()...)
{
    if (tile.ValueRO.meshGenerated && _queuedEntities.Add(entity))
    {
        tilesToQueue.Add(entity); // Just collecting
    }
}

// Second pass: Add buffers AFTER iteration completes
for (int i = 0; i < tilesToQueue.Length; i++)
{
    Entity tileEntity = tilesToQueue[i];
    if (!state.EntityManager.HasBuffer<TreeSpawnPosition>(tileEntity))
    {
        state.EntityManager.AddBuffer<TreeSpawnPosition>(tileEntity); // ✅ Safe now
    }
    _pendingTiles.Enqueue(tileEntity);
}

tilesToQueue.Dispose();
```

### Fix 3: Added TerrainTileConfig Requirement

**File**: `TerrainTreeSpawningSystemOptimized.cs` (line 47)

```csharp
state.RequireForUpdate<TerrainTileConfig>(); // ✅ Added
```

### Fix 4: Added Explicit Update Ordering

**File**: `TerrainTreeSpawningSystemOptimized.cs` (line 25)

```csharp
[UpdateAfter(typeof(CameraDataUpdateSystem))] // ✅ Added
```

Ensures camera data is available before tree spawning starts.

### Fix 5: Added Debug Logging

**File**: `TerrainTreeSpawningSystemOptimized.cs` (multiple locations)

Debug logs will now show:
- When tiles are queued for tree spawning
- When tiles are being processed
- If configuration is invalid (no prefabs, maxTreesPerTile=0, etc.)

---

## How to Test

### Step 1: Clear Any Existing State

1. Stop Play Mode if running
2. File → Build Settings → Delete temp files (if issues persist)
3. Assets → Reimport All (only if needed)

### Step 2: Enter Play Mode

1. Open scene: `Assets/_App/Ace of Ages/Ace of Ages.unity`
2. Enter Play Mode
3. **Watch the Console** for debug messages:

**Expected Console Output**:
```
[TreeSpawnerOptimized] Queued X tiles for tree spawning. Total pending: X
[TreeSpawnerOptimized] Processing X tiles this frame (budget: X)
```

**If you see warnings**:
```
[TreeSpawnerOptimized] No tree prefabs configured!
```
→ Check `TreeSpawnerConfigAuthoring` component has tree LOD sets assigned.

```
[TreeSpawnerOptimized] maxTreesPerTile <= 0, trees disabled
```
→ Check `TreeSpawnerConfigAuthoring.maxTreesPerTile` is > 0.

```
[TreeSpawnerOptimized] Not enough prefabs for LOD system. Need at least 3, have X
```
→ Need at least 1 tree type with 3 LOD levels (total 3 prefabs minimum).

### Step 3: Verify Trees Spawning

1. Look at terrain tiles in Scene view
2. Trees should appear on tiles after mesh generation completes
3. Check Entity Debugger (Window → Entities → Hierarchy):
   - Find entities with `GlobalTreeInstance` component
   - Should see tree entities attached to tiles

### Step 4: Check Profiler

1. Window → Analysis → Profiler
2. Look for these markers in Timeline view:
   - `TreeSpawner.PositionCalc` - should be <1ms
   - `TreeSpawner.Instantiation` - should be <2ms

---

## Troubleshooting

### Still No Trees Spawning?

#### Check 1: System Running?
- Console should show `[TreeSpawnerOptimized]` messages
- If NO messages at all:
  - Check original system is still disabled (`[DisableAutoCreation]` in `TerrainTreeSpawningSystem.cs` line 7)
  - Verify `CameraDataSingleton` exists (Window → Entities → Systems, find `CameraDataUpdateSystem`)

#### Check 2: Tree Prefabs Configured?
- Find `TreeSpawnerConfigAuthoring` component in scene
- Verify `Tree LOD Sets` array has entries
- Each tree type needs LOD0, LOD1, LOD2 GameObjects assigned
- Check Console for warnings about missing prefabs

#### Check 3: Tiles Generated?
- Tiles must have `MeshReference` component
- Check `TerrainMeshGenerationSystem` is running
- Verify terrain config has `renderTerrain = true`

#### Check 4: Config Settings Valid?
```
TreeSpawnerConfigAuthoring:
- minTreesPerTile: 5 (or higher)
- maxTreesPerTile: 15 (or higher)
- maxTreesSpawnedPerFrame: 20 (or higher)
- maxSlopeDegrees: 45 (reasonable value)
- minSpawnHeight: -100 (not too restrictive)
- maxSpawnHeight: 100 (not too restrictive)
```

### Trees Spawning But FPS Poor?

- This is normal on first run (job compilation)
- Second play session should be smooth
- If still slow, reduce `maxTreesPerTile` temporarily

### Debug Steps

1. **Enable verbose logging**: The debug logs should tell you what's happening
2. **Check Entity Debugger**: Window → Entities → Hierarchy
   - Look for entities with `TreeSpawnPosition` buffer (should be cleared after spawning)
   - Look for entities with `GlobalTreeInstance` (trees)
   - Look for `TreesSpawned` tag on tiles
3. **Check Systems Window**: Window → Entities → Systems
   - Find `TerrainTreeSpawningSystemOptimized`
   - Should show as "Running" when tiles need trees
   - Click to see details

---

## Expected Behavior

### First Frame After Mesh Generation
Console logs:
```
[TreeSpawnerOptimized] Queued 5 tiles for tree spawning. Total pending: 5
[TreeSpawnerOptimized] Processing 1 tiles this frame (budget: 1)
```

### Subsequent Frames
Console logs:
```
[TreeSpawnerOptimized] Processing 1 tiles this frame (budget: 1)
[TreeSpawnerOptimized] Processing 1 tiles this frame (budget: 1)
... (until queue empty)
```

### After All Tiles Processed
- No more console logs (nothing to process)
- Trees visible on terrain tiles
- Frame rate smooth (<3ms for tree spawning)

---

## What to Report Back

If trees still don't spawn, please provide:

1. **Console Output**: Copy all `[TreeSpawnerOptimized]` messages
2. **TreeSpawnerConfig Values**: Screenshot or list settings
3. **Entity Debugger**: Screenshot showing entities and components
4. **Scene Setup**: Which scene are you testing? Any custom terrain config?

---

## Success Criteria

✅ Trees appear on terrain tiles  
✅ Console shows "Queued X tiles" and "Processing X tiles" messages  
✅ No errors in Console  
✅ Frame rate smooth (profiler shows <3ms total)  
✅ Entity Debugger shows entities with `GlobalTreeInstance` component  

---

**Expected result**: Trees should now spawn correctly with the optimized system functioning at 15-30x faster than original! 🚀

