# Static Object Spawning System - Documentation
**Version:** 3.0  
**Last Updated:** May 4, 2026

## Overview

The Static Object Spawning System procedurally places static object entities on terrain tiles after mesh generation, with configurable density, variation, and performance budgeting.

**Related Documentation:**
- **[Tree Rendering System](Documentation/TREE_RENDERING_SYSTEM.md)** - Instanced rendering, LOD, and culling (v3.0)
- **[System Reference](Documentation/SYSTEM_REFERENCE.md)** - Complete system APIs
- **[Performance Guide](Documentation/PERFORMANCE.md)** - Optimization strategies

## Features

- **Random Placement**: objects placed at truly random XZ positions within tile bounds (not grid-aligned)
- **Bilinear Interpolation**: Height and normals sampled from mesh vertices using bilinear interpolation
- **Deterministic**: Same tile always gets same tree layout (uses grid coordinate hash as seed)
- **Scale Variation**: Random scale multipliers for visual variety
- **Rotation Variation**: Random Y-axis rotation for each tree
- **Height Filtering**: Only spawn trees within specified height range
- **Slope Filtering**: Avoid spawning on steep terrain
- **Frame Budgeting**: Limits trees spawned per frame to prevent stuttering
- **Non-Hierarchical**: Uses `StaticObjectTileOwnership` component instead of parent-child hierarchy for better performance

## Components

### StaticObjectSpawnerConfig (Singleton)
Configuration for static object spawning behavior.

**Fields:**
- `minObjectsPerTile` - Minimum trees per tile
- `maxObjectsPerTile` - Maximum trees per tile
- `minTreeScale` - Minimum scale multiplier
- `maxTreeScale` - Maximum scale multiplier
- `minSpawnHeight` - Minimum Y coordinate for spawning
- `maxSpawnHeight` - Maximum Y coordinate for spawning
- `slopeThreshold` - Pre-calculated cosine of max slope angle
- `maxStaticObjectsSpawnedPerFrame` - Performance budget

### StaticObjectPrefabElement (Buffer)
Stores references to object prefab entities.

**Fields:**
- `prefabEntity` - Entity prefab to instantiate

### StaticObjectsSpawned (Tag)
Marks tiles that have had trees spawned.

### SpawnedStaticObjectReference (Buffer)
Tracks static object entities spawned on a tile for cleanup.

**Fields:**
- `treeEntity` - Entity reference to spawned tree

### StaticObjectTileOwnership (Component)
Tracks which terrain tile a tree belongs to and its local offset, without using parent-child hierarchy.

**Fields:**
- `tileEntity` - The terrain tile entity this tree belongs to
- `localOffset` - Local position offset from tile origin (for position updates)

## Systems

### TerrainStaticObjectSpawningSystem

**Update Group**: `SimulationSystemGroup`  
**Update After**: `TerrainRenderingSystem`  
**Type**: `SystemBase` (main thread - uses EntityManager directly)

**Purpose**: Spawns trees on terrain tiles after mesh is rendered.

**Algorithm**:
1. Query tiles with `MeshReference` + `meshGenerated=true` + no `StaticObjectsSpawned` tag
2. Enqueue tiles to pending queue
3. Process tiles up to frame budget (`maxStaticObjectsSpawnedPerFrame`)
4. For each tile:
   - Seed RNG with `gridCoordinate.GetHashCode()`
   - Determine random tree count (min-max range)
   - Loop: generate random XZ position, interpolate height/normal from mesh, check filters, spawn tree
   - Add `StaticObjectTileOwnership` component to track tile without parent-child hierarchy
   - Store tree in tile's `SpawnedStaticObjectReference` buffer for cleanup
   - Add `StaticObjectsSpawned` tag

### TreePositionUpdateSystem

**Update Group**: `TransformSystemGroup`  
**Update After**: `TileScrollPositionSystem`  
**Type**: `ISystem` (Burst-compiled)

**Purpose**: Updates tree positions when their owning tiles move (e.g., during auto-scrolling).

**Algorithm**:
1. Get `ComponentLookup<LocalTransform>` for tile positions
2. Query all trees with `StaticObjectTileOwnership` + `LocalTransform`
3. For each tree:
   - Check if owning tile still exists
   - Calculate new position: `tilePosition + localOffset`
   - Update tree's `LocalTransform.Position`

**Performance**:
- **Frame Budget**: Configurable via `maxStaticObjectsSpawnedPerFrame`
- **Typical**: 10-20 trees spawned per frame = <1ms
- **Profiler Markers**: `TerrainTrees.Spawning`, `TerrainTrees.Enqueue`, `TerrainTrees.Spawn`

### TileSpawningSystem (Modified)

**Tree Cleanup**:
- Trees are parented to tile entities using ECS `Parent` component
- Unity ECS automatically destroys child entities when parent is destroyed
- No manual cleanup code required - simplified implementation

## Authoring

### StaticObjectSpawnerConfigAuthoring

Place on the same GameObject as `TerrainConfigAuthoring`.

**Inspector Fields**:
```
object prefabs
├─ treePrefabs[] - Array of GameObject prefabs to convert to entities

Spawn Density
├─ minObjectsPerTile (0-50, default: 5)
└─ maxObjectsPerTile (0-50, default: 15)

Tree Variation
├─ minTreeScale (0.1-2, default: 0.8)
└─ maxTreeScale (0.1-2, default: 1.2)

Spawn Filtering
├─ minSpawnHeight (default: -100)
├─ maxSpawnHeight (default: 100)
└─ maxSlopeDegrees (0-90, default: 45)

Performance
└─ maxStaticObjectsSpawnedPerFrame (1-100, default: 20)
```

**Baker**:
- Converts GameObject prefabs to Entity prefabs via `GetEntity()`
- Pre-calculates slope threshold: `cos(radians(maxSlopeDegrees))`
- Creates singleton `StaticObjectSpawnerConfig`
- Populates `StaticObjectPrefabElement` buffer

## Setup Instructions

### 1. Create object prefabs

**Option A: SubScene Entities (Recommended)**
1. Create tree GameObjects in a SubScene
2. Add mesh renderers/materials
3. Ensure compatible with Entities Graphics

**Option B: Standalone Prefabs**
1. Create object prefab GameObjects
2. Unity will convert to entity prefabs during baking
3. Must have `LocalTransform` component (added automatically)

### 2. Configure Tree Spawner

1. Find GameObject with `TerrainConfigAuthoring` component
2. Add `StaticObjectSpawnerConfigAuthoring` component
3. Assign object prefabs to `treePrefabs[]` array
4. Configure density (min/max trees per tile)
5. Set variation ranges (scale)
6. Adjust filters (height, slope)
7. Set performance budget (max trees per frame)

### 3. Test in Play Mode

1. Enter Play mode
2. Watch tiles generate meshes
3. Trees appear shortly after mesh rendering
4. Check Profiler: `TerrainTrees.Spawning` should be <5ms

## Performance Tuning

### High-End VR (RTX 4080+)
```
maxStaticObjectsSpawnedPerFrame: 50
maxObjectsPerTile: 20
```

### Mid-Range VR (RTX 3070)
```
maxStaticObjectsSpawnedPerFrame: 20
maxObjectsPerTile: 15
```

### Low-End VR (Quest 2)
```
maxStaticObjectsSpawnedPerFrame: 10
maxObjectsPerTile: 8
```

### Optimization Tips

1. **Reduce Tree Count**: Lower `maxObjectsPerTile` for better performance
2. **Simplify Prefabs**: Use low-poly tree models with LOD
3. **Increase Slope Filter**: Higher `maxSlopeDegrees` = more spawn attempts = slower
4. **Frame Budget**: Lower `maxStaticObjectsSpawnedPerFrame` if stuttering occurs
5. **Height Filter**: Narrow height range = fewer valid spawn positions = faster
6. **Culling Distance**: Consider adding distance-based static object spawning (future feature)

## Implementation Details

### Random Number Generation

**Deterministic Seed**:
```csharp
var random = new Unity.Mathematics.Random((uint)(tile.gridCoordinate.GetHashCode() + 12345));
```

- Same tile coordinate always produces same tree layout
- Seed offset (12345) prevents correlation with other RNG uses
- Ensures consistent visuals across sessions

### Slope Filtering

**Pre-calculated Threshold**:
```csharp
float slopeThreshold = math.cos(math.radians(maxSlopeDegrees));
```

**Runtime Check**:
```csharp
if (normal.y < slopeThreshold) continue; // Too steep
```

- `normal.y` is cosine of angle from vertical
- Comparison is fast (no `acos()` needed)
- Lower `normal.y` = steeper slope

### Transform Calculation

**Local Position**:
```csharp
// Tree position is LOCAL to parent tile
EntityManager.SetComponentData(treeEntity, new LocalTransform
{
    Position = localPosition,  // Vertex position relative to tile
    Rotation = rotation,
    Scale = scale
});
```

- `localPosition` = random XZ position within tile + interpolated Y from mesh
- Tree uses world position = `tilePosition + localOffset`
- TreePositionUpdateSystem updates tree positions when tiles move

**Tile Ownership (No Hierarchy)**:
```csharp
EntityManager.AddComponentData(treeEntity, new StaticObjectTileOwnership
{
    tileEntity = tileEntity,
    localOffset = localPosition
});
```

- Tracks which tile owns the tree without parent-child hierarchy overhead
- TreePositionUpdateSystem updates positions each frame: `treePosition = tilePosition + localOffset`
- TileSpawningSystem explicitly destroys trees when tiles despawn
- 5x faster than Parent component approach (~0.1ms vs ~0.5ms per 1000 trees)

**Rotation & Scale**:
```csharp
quaternion rotation = quaternion.RotateY(random.NextFloat(0f, math.PI * 2f));
float scale = random.NextFloat(config.minTreeScale, config.maxTreeScale);
```

- Y-rotation: 0-360 degrees random
- Uniform scale: single multiplier for X/Y/Z

## Debugging

### No Trees Spawning?

**Check 1**: StaticObjectSpawnerConfigAuthoring assigned?
- Look for component on same GameObject as TerrainConfigAuthoring

**Check 2**: object prefabs valid?
- Console should show: `[TreeSpawner] Baked N object prefabs`
- If 0, prefabs weren't converted correctly

**Check 3**: Height/slope filters too restrictive?
- Try setting: `minSpawnHeight = -1000`, `maxSpawnHeight = 1000`
- Try setting: `maxSlopeDegrees = 90` (no slope filtering)

**Check 4**: Tiles generating meshes?
- Trees only spawn after `TerrainRenderingSystem` completes
- Check that terrain tiles are visible

### Trees Spawning Too Slowly?

**Increase**: `maxStaticObjectsSpawnedPerFrame`
- Default 20 → try 50 or 100
- Watch Profiler to ensure no stutter

### Trees Clustered/Overlapping?

**Expected Behavior**: Random placement can cause clustering
- This is normal with pure random distribution
- Consider implementing Poisson disk sampling (future enhancement)

**Workaround**: Increase tile size or reduce tree count
- Fewer trees per tile = less clustering

## Future Enhancements

1. **Poisson Disk Sampling**: More even distribution, prevents clustering
2. **Distance-Based Culling**: Don't spawn trees on distant tiles
3. **LOD System**: Different object prefabs based on distance
4. **Biome Support**: Different tree types based on height/noise values
5. **Density Maps**: Use textures to control tree placement
6. **Wind Animation**: Add ECS system for tree swaying
7. **Billboard LOD**: Distant trees rendered as billboards

## See Also

- `TerrainMeshGenerationSystem.cs` - Mesh generation that precedes static object spawning
- `TileSpawningSystem.cs` - Tile lifecycle management
- `TerrainRenderingSystem.cs` - Mesh rendering that enables static object spawning
- `EXTENSIONS.md` - Other terrain customization ideas

