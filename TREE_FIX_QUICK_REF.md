# Tree Accumulation Fix - Quick Reference 🎯

## What Was Wrong

**Tiles were spawning trees multiple times in the same buffer!**

Example from your logs:
```
Tile int2(-1, 1): length 42  ← First spawn
Tile int2(-1, 1): length 84  ← Spawned AGAIN! (duplicate)
Tile int2(-1, 1): length 126 ← Spawned AGAIN! (triplicate)
```

## Why It Happened

The queue was persistent across frames, and tiles could be enqueued multiple times:
1. Frame 1: Tile 387 enqueued (no `TreesSpawned` tag yet)
2. Frame 2: Tile 387 enqueued AGAIN (still no tag, not processed yet)
3. Processing: Tile 387 processed twice → spawns trees twice!

## The Fix

Added `NativeHashSet<Entity> _queuedEntities` to prevent duplicates:

```csharp
// Only enqueue if not already in queue:
if (_queuedEntities.Add(entity))
{
    _pendingTiles.Enqueue(entity);
}

// Clear when processing:
_queuedEntities.Remove(tileEntity);
```

## Files Changed

1. `TerrainTreeSpawningSystem.cs` - Added duplicate prevention
2. `TerrainMeshGenerationSystem.cs` - Clean up trees on mesh regen

## What to Expect Now

### Console Output:
```
[TreeSpawning] Enqueued tile int2(-1, 1), Entity: 387
[TreeSpawning] Tile int2(-1, 1) spawned 42 trees
[TreeSpawning] Tile int2(-1, 1) already queued, skipping  ← Prevented!
[TreeDebug] Trees: 588, Tiles: 20
[TileSpawning] Found 42 trees to destroy for tile int2(-1, 1)
[TreeDebug] Trees: 546  ← DECREASES! ✅
```

### Tree Count:
- **Before:** Grows forever (1914 → 4238 → 6472 → 8669...)
- **After:** Stabilizes (~500-1000) and decreases when tiles despawn! ✅

## Test It

1. Run scene
2. Watch Console for duplicate prevention messages
3. Verify tree count STABILIZES
4. Move around - tree count should DECREASE as tiles despawn
5. Check buffer lengths match spawn counts (no more 2x, 3x duplicates)

## Success = Tree Count Goes DOWN When Moving! 🎉

That's the key indicator - trees should be cleaned up properly now.

