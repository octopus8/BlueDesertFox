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
- **Slope Filtering**: Avoid spawning on steep terrain
- **Frame Budgeting**: Limits object entities spawned **and destroyed** per frame to prevent ECB playback stuttering
- **Non-Hierarchical**: Uses `StaticObjectTileOwnership` component instead of parent-child hierarchy for better performance

## Components

### StaticObjectSpawnerConfig (Singleton)
Configuration for static object spawning behavior.

**Fields:**
- `minObjectsPerTile` - Minimum trees per tile
- `maxObjectsPerTile` - Maximum trees per tile
- `minTreeScale` - Minimum scale multiplier
- `maxTreeScale` - Maximum scale multiplier
- `slopeThreshold` - Pre-calculated cosine of max slope angle
- `maxObjectsSpawnedPerFrame` - Shared spawn/destroy budget (max object entities created or destroyed per frame)

### StaticObjectSpawnProgress (Component)
Tracks partial instantiation on a tile while spawn positions are consumed incrementally.

**Fields:**
- `nextSpawnIndex` - Next index in `StaticObjectSpawnPosition` buffer to instantiate

### PendingTileDespawn (Tag)
Marks a tile leaving the view ring whose static objects are being destroyed over multiple frames before the tile entity is removed.

### StaticObjectsSpawned (Tag)
Marks tiles that have had trees spawned.

### StaticObjectPrefabElement (Buffer)
Stores references to object prefab entities.

**Fields:**
- `prefabEntity` - Entity prefab to instantiate

### StaticObjectSpawnPosition (Buffer)
Temporary spawn data computed once per tile, consumed incrementally across frames until `StaticObjectsSpawned` is added.

### SpawnedStaticObjectReference (Buffer)
Tracks static object entities spawned on a tile for cleanup.

**Fields:**
- `objectEntity` - Entity reference to spawned static object

### StaticObjectTileOwnership (Component)
Tracks which terrain tile a tree belongs to and its local offset, without using parent-child hierarchy.

**Fields:**
- `tileEntity` - The terrain tile entity this tree belongs to
- `localOffset` - Local position offset from tile origin (for position updates)

## Systems

### TerrainStaticObjectSpawningSystemOptimized

**Update Group**: `SimulationSystemGroup`  
**Update After**: `TerrainRenderingSystem`  
**Type**: `ISystem` (Burst-compiled where possible; EntityManager on main thread for instantiation)

**Purpose**: Spawns trees on terrain tiles after mesh is rendered.

**Algorithm**:
1. Query tiles with `MeshReference` + `meshGenerated=true` + no `StaticObjectsSpawned` tag
2. Enqueue new tiles; resume tiles with `StaticObjectSpawnProgress`
3. Calculate spawn positions once per tile into `StaticObjectSpawnPosition` buffer (Burst parallel job)
4. Instantiate up to `maxObjectsSpawnedPerFrame` **object entities** per frame (not per tile):
   - Resume in-progress tiles first, then newly ready tiles
   - Instantiate the correct LOD prefab directly; set transform, ownership, chunk membership, and instance data
   - Add `StaticObjectSpawnProgress` when a tile spans multiple frames; add `StaticObjectsSpawned` when complete
5. LOD prefabs are baked with `GlobalStaticObjectInstance` and default `GlobalStaticObjectInstanceData` to reduce ECB command count

**Prefab baking**: Add `StaticObjectPrefabAuthoring` to each static object LOD prefab root (bakes spawn components on the prefab entity). Re-bake the Entities SubScene after adding or changing prefabs.

### StaticObjectPositionUpdateSystem

**Update Group**: `TransformSystemGroup`  
**Update After**: `TileScrollPositionSystem`  
**Type**: `ISystem` (Burst-compiled)

**Purpose**: Updates static object positions when their owning tiles move (e.g., during auto-scrolling).

**Algorithm**:
1. Get `ComponentLookup<LocalTransform>` for tile positions
2. Query all static objects with `StaticObjectTileOwnership` + `LocalTransform`
3. For each object:
   - Check if owning tile still exists
   - Calculate new position: `tilePosition + localOffset`
   - Update object's `LocalTransform.Position`

**Performance**:
- **Frame Budget**: Configurable via `maxObjectsSpawnedPerFrame` (`StaticObjectSpawnerConfig`) — counts **instances**, not tiles
- **Typical**: 20 objects spawned per frame keeps `EntityCommandBuffer.Playback` under ~1–2ms for static objects
- **Profiler Markers**: `TreeSpawner.PositionCalc`, `TreeSpawner.Instantiation`, `EndSimulationEntityCommandBufferSystem`

### TileSpawningSystem (Modified)

**Static Object Cleanup**:
- Static objects are tracked via the tile's `SpawnedStaticObjectReference` buffer (non-hierarchical — no `Parent` component)
- When a tile despawns, `TileSpawningSystem` adds `PendingTileDespawn` and destroys up to `maxObjectsSpawnedPerFrame` object entities per frame
- The tile entity is destroyed only after its `SpawnedStaticObjectReference` buffer is empty
- Grid coordinates with pending despawns are blocked from respawn until cleanup completes
- `StaticObjectTileOwnership` tracks which tile owns each object for position updates without parent-child hierarchy overhead

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
6. Adjust slope filter (`maxSlopeDegrees`)
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
4. **Frame Budget**: Lower `maxObjectsSpawnedPerFrame` if ECB playback stutters (applies to both spawn and despawn)
5. **SubScene re-bake**: Required after prefab baking changes in `StaticObjectSpawnerConfigAuthoring`

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

**Check 3**: Slope filter too restrictive?
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
- `TerrainStaticObjectSpawningSystemOptimized.cs` - Active spawning system (legacy spawner removed)
- `StaticObjectPositionUpdateSystem.cs` - Position update when tiles scroll
- `TileSpawningSystem.cs` - Tile lifecycle management and static object cleanup
- `TerrainRenderingSystem.cs` - Mesh rendering that enables static object spawning
- `Documentation/EXTENSIONS.md` - Other terrain customization ideas

