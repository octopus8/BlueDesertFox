# Terrain LOD Center Fix - Quick Reference

## What Changed?
Tile transforms now positioned at **CENTER** instead of **corner** for accurate LOD distance calculations.

## Modified Files
1. `TileSpawningSystem.cs` - Spawn tiles centered
2. `TileScrollPositionSystem.cs` - Update tiles centered  
3. `TerrainMeshGenerationSystem.cs` - Offset vertices by `-halfTileSize`
4. `TerrainTreeSpawningSystem.cs` - Offset tree positions by `-halfTileSize`

## Key Formula Changes

### Transform Position (spawning & scrolling)
```csharp
// OLD: Corner-based
basePos = new float3(gridX * tileSize, 0, gridY * tileSize);

// NEW: Center-based  
basePos = new float3(
    gridX * tileSize + tileSize * 0.5f,
    0,
    gridY * tileSize + tileSize * 0.5f
);
```

### Vertex Positions (mesh generation)
```csharp
// OLD: Relative to corner [0, tileSize]
vertex = new float3(localX, height, localZ);

// NEW: Relative to center [-tileSize/2, +tileSize/2]
vertex = new float3(localX - halfTileSize, height, localZ - halfTileSize);
```

### Tree Positions (content spawning)
```csharp
// OLD: [0, tileSize] range
localPos = new float3(randomX, height, randomZ);

// NEW: [-tileSize/2, +tileSize/2] range
localPos = new float3(randomX - halfTileSize, height, randomZ - halfTileSize);
```

## Distance Calculation
No code changes needed! Already uses `LocalTransform.Position`:
```csharp
float3 tileCenter = transform.ValueRO.Position;
float distance = math.distance(tileCenter2D, playerPos2D);
```

## World Position Formula (unchanged)
```csharp
worldPos = tileTransform.Position + localOffset
```
- **Before**: `(corner) + (offset in [0, size])` = world pos
- **After**: `(center) + (offset in [-size/2, +size/2])` = world pos  
- **Result**: Same world positions! ✅

## Impact on LOD Distances

### Example: 100m tile, player at (50, 0, 50)
| Scenario | Transform Pos | Calc Distance | Error |
|----------|--------------|---------------|-------|
| **OLD (corner)** | (0, 0, 0) | ~70.7m | -29.3m |
| **NEW (center)** | (50, 0, 50) | 0m | 0m ✅ |

### LOD Thresholds (default config)
- **Full Res**: < 150m from tile center  
- **Half Res**: 150m - 300m
- **Quarter Res**: 300m - 450m
- **No Collider**: > 450m

## Testing
1. Enable `TerrainColliderVisualizer` in scene
2. Check LOD colors match distance to player
3. Walk toward distant tile and observe LOD transition
4. Verify transition happens at expected distance (e.g., 150m for full res)

## Debugging
```csharp
// Print tile info at runtime
var tile = EntityManager.GetComponentData<TerrainTile>(entity);
var transform = EntityManager.GetComponentData<LocalTransform>(entity);
var distance = EntityManager.GetComponentData<TerrainTileDistanceToPlayer>(entity);

Debug.Log($"Tile {tile.gridCoordinate}: Pos={transform.Position}, Distance={distance.distance}m, LOD={distance.lodLevel}");
```

## See Also
- `TERRAIN_LOD_CENTER_FIX.md` - Full implementation details
- `AGENTS.md` - Updated project architecture notes
- `Assets/_App/Ace of Ages/Terrain/` - All terrain system files

