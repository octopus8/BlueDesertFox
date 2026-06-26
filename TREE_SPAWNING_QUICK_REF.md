# Tree Spawning System - Quick Reference

## Quick Setup (5 Minutes)

1. **Add Component**:
   - Find GameObject with `TerrainConfigAuthoring`
   - Add Component → `TreeSpawnerConfigAuthoring`

2. **Assign Tree Prefabs**:
   - Drag tree GameObjects to `treePrefabs[]` array
   - Must have mesh renderer and material

3. **Configure Basic Settings**:
   ```
   minTreesPerTile: 5
   maxTreesPerTile: 15
   minTreeScale: 0.8
   maxTreeScale: 1.2
   maxSlopeDegrees: 45
   maxTreesSpawnedPerFrame: 20
   ```

4. **Play**: Trees spawn automatically on terrain tiles

## Component Quick Reference

| Component | Type | Purpose |
|-----------|------|---------|
| `TreeSpawnerConfig` | Singleton | Configuration data |
| `TreePrefabElement` | Buffer | Tree prefab entities |
| `TreesSpawned` | Tag | Marks tile with trees |
| `SpawnedTreeReference` | Buffer | Tracks trees for cleanup |

## System Quick Reference

| System | Update After | Purpose |
|--------|-------------|---------|
| `TerrainTreeSpawningSystemOptimized` | `TileScrollPositionSystem` | Spawns static objects on tiles (Burst) |

## Common Settings

### Sparse Forest
```
minTreesPerTile: 3
maxTreesPerTile: 8
```

### Dense Forest
```
minTreesPerTile: 15
maxTreesPerTile: 30
```

### Mountainous (Steep Terrain)
```
maxSlopeDegrees: 30  // Steeper = fewer valid spots
```

### Plains (Flat Terrain)
```
maxSlopeDegrees: 60  // More permissive
```

## Performance Settings by Platform

| Platform | maxTreesSpawnedPerFrame | maxTreesPerTile |
|----------|------------------------|-----------------|
| Quest 2 | 10 | 8 |
| Quest 3 | 20 | 15 |
| PCVR (RTX 3070) | 20 | 15 |
| PCVR (RTX 4080+) | 50 | 20 |

## Troubleshooting

### No Trees Spawning?
1. Check `treePrefabs[]` array is populated
2. Console should show: `[TreeSpawner] Baked N tree prefabs`
3. Try setting `maxSlopeDegrees = 90` to disable slope filter

### Trees Spawn Too Slowly?
- Increase `maxTreesSpawnedPerFrame` (default: 20 → try 50)

### Performance Issues?
- Decrease `maxTreesSpawnedPerFrame` (20 → 10)
- Decrease `maxTreesPerTile` (15 → 8)
- Use simpler tree models (fewer polygons)

## Files Reference

| File | Location |
|------|----------|
| Components | `Assets/_App/Ace of Ages/Terrain/TileComponents.cs` |
| Authoring | `Assets/_App/Ace of Ages/Terrain/StaticObjectSpawnerConfigAuthoring.cs` |
| System | `Assets/_App/Ace of Ages/Terrain/TerrainStaticObjectSpawningSystemOptimized.cs` |
| Cleanup | `Assets/_App/Ace of Ages/Terrain/TileSpawningSystem.cs` |
| Full Docs | `Assets/_App/Ace of Ages/Terrain/STATIC_OBJECT_SPAWNING_SYSTEM.md` |
| Implementation Summary | `TREE_SPAWNING_IMPLEMENTATION.md` |

## Code Snippets

### Check if Trees are Enabled
```csharp
var configQuery = EntityManager.CreateEntityQuery(typeof(TreeSpawnerConfig));
if (configQuery.CalculateEntityCount() > 0)
{
    var config = EntityManager.GetComponentData<TreeSpawnerConfig>(configQuery.GetSingletonEntity());
    Debug.Log($"Trees enabled: {config.maxTreesPerTile} max per tile");
}
```

### Get Tree Count for Tile
```csharp
if (EntityManager.HasBuffer<SpawnedTreeReference>(tileEntity))
{
    var trees = EntityManager.GetBuffer<SpawnedTreeReference>(tileEntity);
    Debug.Log($"Tile has {trees.Length} trees");
}
```

### Manually Trigger Tree Spawn
```csharp
// Remove TreesSpawned tag to trigger re-spawn
if (EntityManager.HasComponent<TreesSpawned>(tileEntity))
{
    EntityManager.RemoveComponent<TreesSpawned>(tileEntity);
}
```

## Inspector Validation

All values automatically validated in `OnValidate()`:
- `minTreesPerTile` ≥ 0
- `maxTreesPerTile` ≥ `minTreesPerTile`
- `minTreeScale` ≥ 0.1
- `maxTreeScale` ≥ `minTreeScale`
- `maxSlopeDegrees` clamped 0-90
- `maxTreesSpawnedPerFrame` ≥ 1

## Profiler Markers

Monitor in Unity Profiler:
- `TerrainTrees.Spawning` - Overall update time
- `TerrainTrees.Enqueue` - Finding tiles
- `TerrainTrees.Spawn` - Actual spawning

**Target**: <5ms total per frame

## Key Design Decisions

- ✅ **Parent-Child Hierarchy**: Trees parented to tiles (automatic cleanup)
- ✅ **Deterministic**: Same tile = same layout
- ✅ **Frame Budgeted**: No stuttering
- ✅ **Filter-Based**: Slope filtering (and trail exclusion in optimized spawner)
- ✅ **Random Variation**: Rotation + scale

