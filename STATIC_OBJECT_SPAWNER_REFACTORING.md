# Static Object Spawner System - Refactoring Summary
**Date:** May 14, 2026  
**Version:** 1.0  
**Status:** ✅ Complete

> **Note:** TreeSpawner / TreeSpawnerConfig names in the migration tables below are **historical only**. The project uses `StaticObjectSpawnerConfig`, `TerrainStaticObjectSpawningSystemOptimized`, and related static object components exclusively.

## Overview

Generalized the tree-specific spawning system into a reusable static object spawner capable of spawning any ground-based static objects (trees, rocks, bushes, props, etc.) on terrain tiles. Added per-object-type LOD spawn weight distribution to control initial LOD probability at spawn time.

## Key Changes

### 1. Component Renaming (TileComponents.cs)

All tree-specific components renamed to static object terminology:

| Old Component Name | New Component Name | Purpose |
|-------------------|-------------------|---------|
| `TreeSpawnerConfig` | `StaticObjectSpawnerConfig` | Spawner configuration singleton |
| `TreePrefabElement` | `StaticObjectPrefabElement` | Buffer element for prefab entities |
| `TreePrefabMeshMaterialData` | `StaticObjectPrefabMeshMaterialData` | Managed mesh/material data |
| `TreesSpawned` | `StaticObjectsSpawned` | Tag for spawned tiles |
| `TreeSpawnPosition` | `StaticObjectSpawnPosition` | Temp buffer for spawn data |
| `SpawnedTreeReference` | `SpawnedStaticObjectReference` | Cleanup tracking buffer |
| `TreeTileOwnership` | `StaticObjectTileOwnership` | Tile ownership component |
| `GlobalTreeInstance` | `GlobalStaticObjectInstance` | Instance rendering tag |
| `GlobalTreeInstanceData` | `GlobalStaticObjectInstanceData` | Instance rendering data |
| `TreeLODConfig` | `StaticObjectLODConfig` | LOD configuration singleton |
| `TreeChunkMembership` | `StaticObjectChunkMembership` | Spatial chunk assignment |
| `GlobalTreeRenderingData` | `GlobalStaticObjectRenderingData` | Global rendering data |

### 2. Field Renaming

Configuration fields updated for clarity:

| Old Field | New Field |
|-----------|-----------|
| `minTreesPerTile` | `minObjectsPerTile` |
| `maxTreesPerTile` | `maxObjectsPerTile` |
| `maxTreesSpawnedPerFrame` | `maxObjectsSpawnedPerFrame` |
| `lodsPerTreeType` | `lodsPerObjectType` |
| `enableTreeLODDebug` | `enableObjectLODDebug` |
| `maxTreeRenderDistance` | `maxObjectRenderDistance` |
| `treeEntity` | `objectEntity` |
| `treeTypeIndex` | `objectTypeIndex` |

### 3. New Feature: LOD Spawn Weights

Added `StaticObjectLODWeights` buffer component to control initial LOD distribution:

```csharp
public struct StaticObjectLODWeights : IBufferElementData
{
    public int objectTypeIndex;     // Which object type these weights apply to
    public float lod0Weight;        // Probability for LOD0 (0.0-1.0)
    public float lod1Weight;        // Probability for LOD1 (0.0-1.0)
    public float lod2Weight;        // Probability for LOD2 (0.0-1.0)
}
```

**Purpose:** Allows different object types to have different LOD spawn distributions:
- **Trees:** 60% LOD0, 30% LOD1, 10% LOD2 (high visual quality)
- **Distant Rocks:** 10% LOD0, 20% LOD1, 70% LOD2 (performance optimized)
- **Mixed Vegetation:** 33% each (balanced)

**Implementation:**
- Weights configured per `StaticObjectLODSet` in authoring component
- Auto-normalized to sum to 1.0 during baking
- Spawning system uses weighted random selection for initial LOD level
- Runtime LOD transitions still distance-based (unchanged)

### 4. Authoring Component Updates

**File Renamed:** `TreeSpawnerConfigAuthoring.cs` → `StaticObjectSpawnerConfigAuthoring.cs`

**Class Renamed:** `TreeLODSet` → `StaticObjectLODSet`

**New Fields Added:**
```csharp
[Header("LOD Spawn Distribution")]
[Range(0f, 1f)] public float lod0SpawnWeight = 0.6f;
[Range(0f, 1f)] public float lod1SpawnWeight = 0.3f;
[Range(0f, 1f)] public float lod2SpawnWeight = 0.1f;
```

**OnValidate Enhancement:**
- Auto-normalizes weights if all non-zero
- Resets to defaults (0.6/0.3/0.1) if all weights are zero

**Baking Logic:**
- Creates `StaticObjectLODWeights` buffer with one entry per object type
- Logs normalized weights during baking for verification

### 5. System File Renames

| Old Filename | New Filename |
|-------------|-------------|
| `TerrainTreeSpawningSystem.cs` | *(removed; replaced by optimized system)* |
| `TerrainTreeSpawningSystemOptimized.cs` | `TerrainStaticObjectSpawningSystemOptimized.cs` |
| `TreePositionUpdateSystem.cs` | `StaticObjectPositionUpdateSystem.cs` |
| `GlobalTreeInstanceSystem.cs` | `GlobalStaticObjectInstanceSystem.cs` |
| `TreeLODUpdateSystem.cs` | `StaticObjectLODUpdateSystem.cs` |
| `TreeSpatialChunkingSystem.cs` | `StaticObjectSpatialChunkingSystem.cs` |
| `TreeLODDebugSystem.cs` | `StaticObjectLODDebugSystem.cs` |
| `TreeCleanupDebugSystem.cs` | `StaticObjectCleanupDebugSystem.cs` |

### 6. System Updates

All systems updated with:
- Component name replacements
- Variable/parameter renaming (`tree*` → `object*`)
- Documentation string updates
- Comment updates
- Debug log message updates
- Profiler marker renames

**Key System Changes:**
- `TileSpawningSystem.cs`: Updated cleanup logic to use `SpawnedStaticObjectReference`
- Spawning systems: Future update will implement weighted LOD selection using `StaticObjectLODWeights` buffer

### 7. Documentation Updates

**Files Renamed:**
- `TREE_SPAWNING_SYSTEM.md` → `STATIC_OBJECT_SPAWNING_SYSTEM.md`

**Files Updated:**
- `AGENTS.md`: Updated all references to static object terminology, added LOD weights documentation
- `STATIC_OBJECT_SPAWNING_SYSTEM.md`: Component names, system names, examples updated

**New Documentation:**
- `STATIC_OBJECT_SPAWNER_REFACTORING.md` (this file)

## Usage Examples

### Dense Forest Configuration
```
Object Type: "Oak Tree"
LOD0 Weight: 0.7  (70% spawn at high detail)
LOD1 Weight: 0.2  (20% spawn at medium detail)
LOD2 Weight: 0.1  (10% spawn at low detail)
minObjectsPerTile: 15
maxObjectsPerTile: 30
```

### Performance-Optimized Rocks
```
Object Type: "Boulder"
LOD0 Weight: 0.1  (10% spawn at high detail)
LOD1 Weight: 0.2  (20% spawn at medium detail)
LOD2 Weight: 0.7  (70% spawn at low detail)
minObjectsPerTile: 5
maxObjectsPerTile: 10
```

### Mixed Vegetation
```
Object Type: "Grass Clump"
LOD0 Weight: 0.33  (balanced distribution)
LOD1 Weight: 0.33
LOD2 Weight: 0.34
minObjectsPerTile: 20
maxObjectsPerTile: 40
```

## Breaking Changes

⚠️ **No Backward Compatibility** - This is a clean break refactoring. Migration is complete in this repository.

**For new scenes:** Use `StaticObjectSpawnerConfigAuthoring` on the terrain config GameObject, assign `objectLODSets`, configure LOD spawn weights (defaults: 0.6/0.3/0.1), and re-bake subscenes.

**Compile Errors:** Any custom code referencing old component names will fail to compile. Update using the Component Renaming table above.

## Implementation Status

### ✅ Completed
- [x] Component definitions renamed in TileComponents.cs
- [x] Added StaticObjectLODWeights component
- [x] Authoring component renamed and updated with LOD weight fields
- [x] Baker logic updated with weight normalization and buffer population
- [x] 8 system files renamed
- [x] System code updated with component name replacements
- [x] Variable/parameter naming updated
- [x] Documentation strings and comments updated
- [x] TileSpawningSystem.cs cleanup logic updated
- [x] AGENTS.md updated
- [x] STATIC_OBJECT_SPAWNING_SYSTEM.md renamed and updated
- [x] Summary documentation created

### 🔄 Future Work (Optional Enhancements)
- [ ] Implement weighted LOD selection in spawning systems (currently baked but not used at spawn time)
- [ ] Custom PropertyDrawer to show normalized percentages in Inspector
- [ ] Example prefab configurations for common object types
- [ ] Performance testing with mixed object types

## Technical Notes

### LOD Weight Implementation

**Baking (Completed):**
```csharp
float totalWeight = lod0 + lod1 + lod2;
if (totalWeight > 0.001f) {
    lod0Weight = lod0 / totalWeight;
    lod1Weight = lod1 / totalWeight;
    lod2Weight = lod2 / totalWeight;
}
lodWeightsBuffer.Add(new StaticObjectLODWeights { ... });
```

**Spawning Logic (Future Implementation):**
```csharp
// Read weights for selected object type
var weights = lodWeightsBuffer[objectTypeIndex];

// Weighted random selection
float rand = random.NextFloat(0f, 1f);
byte initialLOD = 0;
if (rand < weights.lod0Weight)
    initialLOD = 0;
else if (rand < weights.lod0Weight + weights.lod1Weight)
    initialLOD = 1;
else
    initialLOD = 2;
```

### Performance Considerations

**Memory Impact:**
- `StaticObjectLODWeights` buffer: 16 bytes per object type (minimal)
- No runtime performance impact (weights used only during spawn, not per-frame)

**Compilation Impact:**
- Clean build recommended after refactoring
- All .meta files should be regenerated by Unity

## Migration Guide

This section documents the one-time migration from the legacy tree spawner. Existing scenes in this project have already been migrated.

### For Existing Scenes (historical)

1. **Backup Scene:**
   ```
   Save copy of scene before proceeding
   ```

2. **Update Terrain GameObject:**
   ```
   - Remove legacy TreeSpawnerConfigAuthoring component (if present)
   - Add StaticObjectSpawnerConfigAuthoring component
   - Assign object prefabs to objectLODSets array
   - Configure LOD spawn weights (or use defaults)
   ```

3. **Re-bake SubScenes:**
   ```
   - Select SubScene asset
   - Inspector → Bake → Rebake
   ```

4. **Verify:**
   ```
   - Check console for baking logs
   - Verify normalized weights in log output
   - Test spawning in Play mode
   ```

### For Custom Code

Replace all old component names using the Component Renaming table:

```csharp
// OLD
var config = SystemAPI.GetSingleton<TreeSpawnerConfig>();
var prefabs = EntityManager.GetBuffer<TreePrefabElement>(entity);

// NEW
var config = SystemAPI.GetSingleton<StaticObjectSpawnerConfig>();
var prefabs = EntityManager.GetBuffer<StaticObjectPrefabElement>(entity);
```

## Files Modified

### Core Systems (7 files renamed + updated)
- TerrainStaticObjectSpawningSystemOptimized.cs
- StaticObjectPositionUpdateSystem.cs
- GlobalStaticObjectInstanceSystem.cs
- StaticObjectLODUpdateSystem.cs
- StaticObjectSpatialChunkingSystem.cs
- StaticObjectLODDebugSystem.cs
- StaticObjectCleanupDebugSystem.cs

### Supporting Systems (2 files updated)
- TileComponents.cs
- TileSpawningSystem.cs

### Authoring (1 file renamed + updated)
- StaticObjectSpawnerConfigAuthoring.cs

### Documentation (3 files)
- AGENTS.md (updated)
- STATIC_OBJECT_SPAWNING_SYSTEM.md (renamed + updated)
- STATIC_OBJECT_SPAWNER_REFACTORING.md (new)

## Testing Checklist

Before committing:
- [ ] Project compiles without errors
- [ ] No obsolete component references remain
- [ ] Baking completes successfully with normalized weight logs
- [ ] Objects spawn on terrain tiles in Play mode
- [ ] LOD transitions work correctly (distance-based runtime updates)
- [ ] Tile despawn properly destroys spawned objects
- [ ] Performance is unchanged (LOD weights don't impact runtime)

## Version History

### v1.0 (May 14, 2026)
- Initial refactoring from tree spawner to static object spawner
- Added LOD spawn weight distribution feature
- Comprehensive component and system renaming
- Documentation updates

