# Tree Multi-Spawn Diagnosis

## Problem Found

From the console logs, we can see tiles being processed MULTIPLE TIMES:

```
[TreeSpawning] Starting spawn for tile int2(-1, 1), Entity: 387
[TreeSpawning] Buffer after adding trees - length: 42

[TreeSpawning] Starting spawn for tile int2(-1, 1), Entity: 387  ← SAME TILE!
[TreeSpawning] Buffer capacity before: 64, length: 42  ← Already had 42!
[TreeSpawning] Buffer after adding trees - length: 84  ← Now double!
```

## Root Cause Hypothesis

The `TerrainTreeSpawningSystem` query (line 87-96) runs at the start of `OnUpdate()`:

```csharp
foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
    .WithAll<MeshReference>()
    .WithNone<TreesSpawned>()  ← Should prevent duplicates
    .WithEntityAccess())
{
    if (tile.ValueRO.meshGenerated)
    {
        _pendingTiles.Enqueue(entity);  ← Enqueue to process later
    }
}
```

The tag is added AFTER processing (line 120):
```csharp
EntityManager.AddComponent<TreesSpawned>(tileEntity);
```

### Possible Causes:

1. **System runs multiple times per frame** - unlikely but possible
2. **Query caching issue** - query results cached before tags added?
3. **Tag not persisting** - something removes it immediately?
4. **Pending queue accumulates across frames** - tiles stay queued even after being processed

## Most Likely Cause: Queue Accumulation

The `_pendingTiles` queue is a **persistent queue** (`Allocator.Persistent`). If tiles aren't fully processed due to frame budget limits, they stay in the queue.

BUT - the query runs EVERY frame and could re-enqueue the same tile if:
- The tile is still in the queue from a previous frame
- The `TreesSpawned` tag hasn't been added yet (because it's still queued)

### The Bug:

Frame 1:
- Query finds tile 387 without `TreesSpawned` tag
- Enqueue tile 387
- Process some tiles, but NOT tile 387 (frame budget)

Frame 2:
- Query runs AGAIN, finds tile 387 STILL without tag (not processed yet)
- Enqueue tile 387 AGAIN ← **DUPLICATE!**
- Process tile 387 ONCE, add tag
- Process tile 387 AGAIN from the duplicate in queue ← **SPAWNS TWICE!**

## Fix

Check if the entity is already queued before enqueuing it again. But `NativeQueue` doesn't support Contains().

**Better fix:** Use a `NativeHashSet` to track queued entities.


