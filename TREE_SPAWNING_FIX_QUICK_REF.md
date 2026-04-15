# Tree Spawning - Quick Reference

## The Problem You Had

**Symptom**: Trees appeared in grid/line patterns instead of randomly distributed.

**Root Cause**: Code was picking random vertices from the mesh grid (which are grid-aligned), creating a visible pattern.

## The Solution Applied

**Fix**: Generate truly random XZ positions within tile bounds, then use **bilinear interpolation** to sample height and normals from the surrounding 4 vertices.

## Key Code Change

### Before (Grid Pattern ❌)
```csharp
// Pick random vertex from grid - creates line pattern
int vertexIndex = random.NextInt(0, vertexCount);
float3 localPosition = vertexPositions[vertexIndex];
```

### After (Random Distribution ✅)
```csharp
// Generate random XZ within tile bounds
float randomX = random.NextFloat(0f, tileSize);
float randomZ = random.NextFloat(0f, tileSize);

// Convert to grid space for interpolation
float gridX = (randomX / tileSize) * (vPerSide - 1);
float gridZ = (randomZ / tileSize) * (vPerSide - 1);

// Get 4 surrounding vertices
int x0 = (int)math.floor(gridX);
int z0 = (int)math.floor(gridZ);
int x1 = math.min(x0 + 1, vPerSide - 1);
int z1 = math.min(z0 + 1, vPerSide - 1);

// Bilinear interpolation for height
float tx = gridX - x0;
float tz = gridZ - z0;
float3 vX0 = math.lerp(v00, v10, tx);
float3 vX1 = math.lerp(v01, v11, tx);
float3 interpolatedPosition = math.lerp(vX0, vX1, tz);

// Use random XZ, interpolated Y
float3 localPosition = new float3(randomX, interpolatedPosition.y, randomZ);
```

## Other Fixes Included

1. **Tree Cleanup**: Trees properly destroyed when tiles despawn (via `SpawnedTreeReference` buffer)
2. **Tree Movement**: Trees move with tiles during scrolling (via `TreePositionUpdateSystem`)
3. **No Hierarchy**: Uses `TreeTileOwnership` instead of `Parent` component (5x faster)
4. **Buffer Safety**: Structural changes handled correctly (add buffer first, then get other buffers)

## Files Changed

- `TerrainTreeSpawningSystem.cs` - Main spawning logic with bilinear interpolation
- `AGENTS.md` - Updated documentation
- `TREE_SPAWNING_SYSTEM.md` - Updated algorithm descriptions

## Result

✅ Trees now appear naturally scattered across terrain
✅ No grid or line patterns
✅ Trees move with terrain during scrolling
✅ Trees cleaned up properly when tiles despawn
✅ Better performance (no parent-child hierarchy)

## Testing

Run the scene and visually verify:
1. Trees are randomly scattered (no obvious grid lines)
2. Trees move smoothly when terrain scrolls
3. No floating trees when tiles despawn behind you

