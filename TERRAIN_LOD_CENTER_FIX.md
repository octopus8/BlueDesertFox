# Terrain LOD Center Fix - Implementation Summary

## Issue
Terrain collision LOD distance calculations were using the **bottom-left corner** of tiles instead of the **tile center**, causing incorrect LOD transitions and potential visual/performance issues.

## Root Cause
- `TileSpawningSystem` placed tile `LocalTransform.Position` at the grid origin (bottom-left corner)
- `TileScrollPositionSystem` maintained corner-based positioning during scrolling
- `TerrainDistanceTrackingSystem` used `LocalTransform.Position` for distance calculations
- Result: Distance measured from corner, not center (up to ~71m error on 100m tiles)

## Solution
**Centered Transform Approach**: Move the tile's `LocalTransform.Position` to the tile center and offset all child geometry accordingly.

### Changes Made

#### 1. TileSpawningSystem.cs (Lines 127-136)
**Before:**
```csharp
float3 basePosition = new float3(
    gridCoord.x * config.tileSize,
    0,
    gridCoord.y * config.tileSize
);
```

**After:**
```csharp
// Tile transform is placed at the CENTER of the tile for accurate LOD distance calculations
float3 basePosition = new float3(
    gridCoord.x * config.tileSize + config.tileSize * 0.5f,
    0,
    gridCoord.y * config.tileSize + config.tileSize * 0.5f
);
```

#### 2. TileScrollPositionSystem.cs (Lines 29-42)
**Before:**
```csharp
float3 basePosition = new float3(
    tile.ValueRO.gridCoordinate.x * tileConfig.tileSize,
    0,
    tile.ValueRO.gridCoordinate.y * tileConfig.tileSize
);
```

**After:**
```csharp
// Tile transform is placed at the CENTER of the tile, not the corner
float3 basePosition = new float3(
    tile.ValueRO.gridCoordinate.x * tileConfig.tileSize + tileConfig.tileSize * 0.5f,
    0,
    tile.ValueRO.gridCoordinate.y * tileConfig.tileSize + tileConfig.tileSize * 0.5f
);
```

#### 3. TerrainMeshGenerationSystem.cs (Lines 361-383)
**Before:**
```csharp
float stepSize = data.tileSize / (data.verticesPerSide - 1);

// ...vertex generation...
allVertices[flatIndex] = new float3(localX, height, localZ);
```

**After:**
```csharp
float stepSize = data.tileSize / (data.verticesPerSide - 1);
float halfTileSize = data.tileSize * 0.5f;

// ...vertex generation...
// Store vertex position (relative to tile center, not corner)
// Offset by -halfTileSize so vertices are centered around tile transform
allVertices[flatIndex] = new float3(localX - halfTileSize, height, localZ - halfTileSize);
```

#### 4. TerrainTreeSpawningSystem.cs (Lines 160-197)
**Before:**
```csharp
int vPerSide = terrainConfig.verticesPerSide;
float tileSize = terrainConfig.tileSize;

// ...tree placement...
float3 localPosition = new float3(randomX, interpolatedPosition.y, randomZ);
```

**After:**
```csharp
int vPerSide = terrainConfig.verticesPerSide;
float tileSize = terrainConfig.tileSize;
float halfTileSize = tileSize * 0.5f;

// ...tree placement...
// Local position relative to tile center (vertices are now centered around origin)
// randomX/randomZ are in range [0, tileSize], offset by -halfTileSize to match vertex space
float3 localPosition = new float3(randomX - halfTileSize, interpolatedPosition.y, randomZ - halfTileSize);
```

### Systems NOT Changed (Automatic Fix)

#### TerrainDistanceTrackingSystem.cs
Already correctly uses `LocalTransform.Position` (line 52):
```csharp
float3 tileCenter = transform.ValueRO.Position;
```
Now automatically gets the centered position with no code changes needed!

#### TerrainPhysicsSystem.cs
Uses prepared vertex data from mesh buffers - automatically inherits centered vertex positions.

#### TerrainColliderVisualizer.cs
Uses `transform.Position + vertices[].value` - automatically works correctly with centered transform and offset vertices.

## Impact Analysis

### ✅ Benefits
- **Accurate LOD distances**: Tiles transition at correct distances (measured from center)
- **Consistent behavior**: All distance-based systems now measure from the same point
- **No performance impact**: Same number of calculations, just different offset values
- **Backward compatible**: Existing scenes will automatically recalculate tile positions on load

### ⚠️ Migration Notes
- **Visual shift on first load**: Existing running scenes will see tiles reposition when systems update
- **Physics colliders**: Will regenerate with correct positions (handled by existing invalidation logic)
- **Trees**: Will respawn at correct positions relative to new centered transforms
- **No data migration needed**: All positioning is calculated at runtime

## Verification

### Before Fix
```
Tile at grid (0,0) with 100m size:
  - Transform Position: (0, 0, 0)       // Corner
  - Actual Center: (50, 0, 50)
  - Distance calc: Uses (0, 0, 0)       // ❌ Error up to ~71m
```

### After Fix
```
Tile at grid (0,0) with 100m size:
  - Transform Position: (50, 0, 50)     // Center
  - Actual Center: (50, 0, 50)
  - Distance calc: Uses (50, 0, 50)     // ✅ Correct
```

## Testing Checklist
- [x] Code compiles without errors
- [ ] Terrain renders correctly in play mode
- [ ] LOD transitions occur at expected distances (use TerrainColliderVisualizer)
- [ ] Trees spawn at correct positions on tiles
- [ ] Physics colliders align with visual mesh
- [ ] Auto-scroll terrain maintains correct positioning

## Related Systems
- **Distance Tracking**: TerrainDistanceTrackingSystem
- **Tile Management**: TileSpawningSystem, TileScrollPositionSystem
- **Rendering**: TerrainMeshGenerationSystem, TerrainRenderingSystem
- **Physics**: TerrainPhysicsSystem, TerrainColliderPreparationSystem
- **Content**: TerrainTreeSpawningSystem, TreePositionUpdateSystem

## Date
April 24, 2026

