# Tile Spawning ECB Fix

## Problem: InvalidOperationException when moving

### Error Message
```
InvalidOperationException: playbackState.CreateEntityBatch passed to SelectEntity is null 
(likely due to an ECB command recording an invalid temporary Entity).
```

Occurred at: `TileSpawningSystem.cs:169` during ECB playback

### Root Cause

The system was storing **temporary ECB entities** in the `_activeTiles` hashmap before the ECB was played back:

```csharp
// OLD CODE (WRONG):
Entity tileEntity = ecb.CreateEntity();
// ... add components ...
_activeTiles.Add(gridCoord, tileEntity);  // ❌ Storing temporary entity!

// Later when moving:
ecb.DestroyEntity(tileEntity);  // ❌ Trying to destroy a temporary entity that doesn't exist!
```

### The Problem Explained

1. When tiles are first spawned, `ecb.CreateEntity()` returns a **temporary placeholder entity**
2. This temporary entity was stored in `_activeTiles`
3. When the player moves, old tiles need to be despawned
4. The system tried to use `ecb.DestroyEntity()` with the temporary entity from `_activeTiles`
5. **ECB commands cannot operate on temporary entities from the same or previous ECBs**
6. Result: InvalidOperationException during ECB playback

### The Solution

**Store real entities only after ECB playback:**

```csharp
// NEW CODE (CORRECT):
// 1. Create entities via ECB (don't store yet)
foreach (var gridCoord in tilesToSpawn)
{
    Entity tileEntity = ecb.CreateEntity();
    // ... add components ...
    // ✅ DO NOT add to _activeTiles yet
}

// 2. Play back ECB to create actual entities
ecb.Playback(state.EntityManager);

// 3. Query for newly created tiles and store REAL entities
if (tilesToSpawn.Length > 0)
{
    var query = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TerrainTile>());
    var allTiles = query.ToEntityArray(Allocator.Temp);
    
    foreach (var entity in allTiles)
    {
        var tile = state.EntityManager.GetComponentData<TerrainTile>(entity);
        if (tilesToSpawn.Contains(tile.gridCoordinate) && !_activeTiles.ContainsKey(tile.gridCoordinate))
        {
            _activeTiles.Add(tile.gridCoordinate, entity);  // ✅ Storing real entity!
        }
    }
    
    allTiles.Dispose();
}
```

## Changes Made

### File: `TileSpawningSystem.cs`

1. **Removed** `_activeTiles.Add(gridCoord, tileEntity);` from inside tile spawning loop
2. **Added** post-playback entity registration:
   - Query all TerrainTile entities
   - Match newly created tiles by gridCoordinate
   - Store real entities in `_activeTiles`

## Why This Works

- **Before ECB playback**: Entities are temporary placeholders, cannot be used in other ECB commands
- **After ECB playback**: Entities are real, can be safely stored and used in future frames
- When despawning tiles on next update, `_activeTiles` contains valid entities that can be destroyed

## Benefits

✅ No more InvalidOperationException when moving
✅ Proper entity lifecycle management
✅ ECB usage follows best practices
✅ Tiles spawn and despawn correctly

## Testing

The fix ensures:
1. Initial tile spawning works correctly
2. Moving the player spawns new tiles without errors
3. Old tiles are properly despawned when out of range
4. No temporary entity references are stored

## Performance Note

The post-playback query adds minimal overhead:
- Only runs when new tiles are spawned (`tilesToSpawn.Length > 0`)
- Query is filtered to TerrainTile components only
- Temp allocation is properly disposed

This is the standard pattern for working with EntityCommandBuffer entity creation and storage.

