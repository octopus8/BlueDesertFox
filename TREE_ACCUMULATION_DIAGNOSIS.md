# Tree Entity Accumulation Issue - Diagnosis & Fix

## Problem Report

**Symptom**: Tree entity count keeps increasing and never decreases, even when player moves away from tiles.

## Investigation Steps Added

### 1. Debug System (`TreeCleanupDebugSystem.cs`)
Created a monitoring system that logs every 2 seconds:
- Total tree count
- Total tile count
- Tiles with `TreesSpawned` tag
- **Orphaned trees** (trees whose parent tile no longer exists)

### 2. TileSpawningSystem Logging
Added debug logs to track:
- When tiles are despawned
- How many trees are queued for destruction per tile

## Potential Root Causes

### Theory 1: Tiles Never Despawn (MOST LIKELY)
**Hypothesis**: If the player never moves far enough away, tiles never get marked for despawning, so trees never get destroyed.

**Evidence Needed**:
- Check console for "[TileSpawning] Despawning tile..." messages
- If you don't see these messages, tiles aren't despawning
- Tree count would grow linearly with exploration

**Fix If True**: This is actually **working as designed**. Trees accumulate because you're exploring new terrain. The system is memory-efficient up to thousands of tiles.

### Theory 2: Orphaned Trees
**Hypothesis**: Trees are spawned but not tracked in `SpawnedTreeReference` buffer, or the buffer gets cleared somehow.

**Evidence Needed**:
- Check console for "Found X orphaned trees!" warnings
- If orphaned trees > 0, trees exist but their tiles were destroyed without cleaning up trees

**Fix If True**: Need to investigate why SpawnedTreeReference buffer isn't being populated or is being cleared.

### Theory 3: Buffer Not Populated
**Hypothesis**: Trees are spawned but never added to `SpawnedTreeReference` buffer.

**Evidence Needed**:
- Check console logs: "[TileSpawning] Found X trees to destroy for tile..."
- If X is always 0, buffer isn't being populated

**Fix If True**: Bug in `TerrainTreeSpawningSystem` - trees aren't being added to buffer.

### Theory 4: EntityCommandBuffer Timing Issue
**Hypothesis**: ECB playback happens out of order, trees are destroyed before being tracked.

**Evidence Needed**:
- Trees spawn but `SpawnedTreeReference` buffer is empty
- No crashes, but silent failure

**Fix If True**: Need to ensure tree spawning and buffer population happen in correct order.

## How to Diagnose

### Step 1: Run the Scene
1. Start the game in Editor
2. Let it run for 10-15 seconds
3. Watch the Console window

### Step 2: Check Console Output
Look for these messages:

#### Expected Output (Normal Operation):
```
[TreeDebug] Trees: 45, Tiles: 9, Tiles with TreesSpawned tag: 9, Orphaned trees: 0
[TreeDebug] Trees: 58, Tiles: 12, Tiles with TreesSpawned tag: 12, Orphaned trees: 0
```
- Tree count grows as you explore
- Tile count grows with exploration
- **Orphaned trees should be 0**

#### If Tiles Never Despawn (Normal if not moving far):
```
[TreeDebug] Trees: 120, Tiles: 24, Tiles with TreesSpawned tag: 24, Orphaned trees: 0
[TreeDebug] Trees: 135, Tiles: 27, Tiles with TreesSpawned tag: 27, Orphaned trees: 0
```
- No "[TileSpawning] Despawning tile..." messages
- Tree/tile counts keep growing
- **This is normal if player isn't moving far enough**

#### If Cleanup is Broken (BAD):
```
[TreeDebug] Trees: 150, Tiles: 12, Tiles with TreesSpawned tag: 24, Orphaned trees: 138
[TreeDebug WARNING] Found 138 orphaned trees! Trees exist but their parent tiles have been destroyed.
```
- Orphaned trees > 0
- Tree count >> tile count
- **This indicates cleanup failure**

### Step 3: Test Movement
1. Move player far enough that tiles should despawn (> viewDistance)
2. Look for "[TileSpawning] Despawning tile at..." messages
3. Check if tree count decreases

## Expected Behavior

### Normal Operation:
1. **Exploration Phase**: Tree count grows as you explore new areas
2. **Stable Phase**: Once you stay in one area, tree count stabilizes
3. **Cleanup Phase**: When you move far away, old tiles despawn and tree count decreases

### Actual vs Expected:
- **If tree count never decreases**: Either not moving far enough, or cleanup is broken
- **If orphaned trees appear**: Cleanup is definitely broken

## Quick Fixes

### If Orphaned Trees Found:
The cleanup system is not working. Possible fixes:

#### Option A: Add Explicit Cleanup System
Create a system that finds and destroys orphaned trees:

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct OrphanedTreeCleanupSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        
        // Find all trees whose parent tile no longer exists
        foreach (var (ownership, entity) in SystemAPI.Query<RefRO<TreeTileOwnership>>()
            .WithEntityAccess())
        {
            if (!state.EntityManager.Exists(ownership.ValueRO.tileEntity))
            {
                ecb.DestroyEntity(entity);
            }
        }
        
        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
```

#### Option B: Fix SpawnedTreeReference Buffer
Ensure trees are actually being added to the buffer in `TerrainTreeSpawningSystem`.

### If Trees Accumulate Normally:
This might be expected behavior! Check:
1. How far are you moving?
2. What is `config.viewDistance`? (default: 500m)
3. How many tiles have you visited?

**Math**: If viewDistance = 500m and tileSize = 100m, you'll have ~25-30 tiles active at once. With 10 trees/tile average, that's 250-300 trees. This is normal!

## Next Steps

1. **Run the scene** and check console logs
2. **Move around** to trigger tile despawning
3. **Report findings**:
   - Do tiles despawn? (check for log messages)
   - Do orphaned trees appear? (check for warnings)
   - What are the numbers? (trees, tiles, orphaned)

Based on the logs, we can determine if this is:
- ✅ **Normal behavior** (trees accumulate during exploration)
- ❌ **Bug** (trees aren't being cleaned up when tiles despawn)

## Files Modified

1. `TreeCleanupDebugSystem.cs` - NEW debug/monitoring system
2. `TileSpawningSystem.cs` - Added debug logging for despawn events

## Testing Commands

In Unity Editor:
1. Open scene: `Assets/_App/Ace of Ages/Ace of Ages.unity`
2. Press Play
3. Open Console window (Ctrl+Shift+C)
4. Filter console to show "[TreeDebug]" and "[TileSpawning]" messages
5. Move player around and watch the logs

## Expected Console Output

### Initial Spawn:
```
[TreeDebug] Trees: 0, Tiles: 0, Tiles with TreesSpawned tag: 0, Orphaned trees: 0
[TreeDebug] Trees: 15, Tiles: 3, Tiles with TreesSpawned tag: 3, Orphaned trees: 0
[TreeDebug] Trees: 78, Tiles: 9, Tiles with TreesSpawned tag: 9, Orphaned trees: 0
```

### After Moving Far:
```
[TileSpawning] Despawning tile at int2(-2, -1), Entity: 1234
[TileSpawning] Found 8 trees to destroy for tile int2(-2, -1)
[TileSpawning] Despawning tile at int2(-2, 0), Entity: 1235
[TileSpawning] Found 12 trees to destroy for tile int2(-2, 0)
[TreeDebug] Trees: 58, Tiles: 7, Tiles with TreesSpawned tag: 7, Orphaned trees: 0
```

If you see the despawning messages and tree count decreases, **the system is working correctly**!

If you DON'T see despawning messages, you're simply not moving far enough away from the tiles.

