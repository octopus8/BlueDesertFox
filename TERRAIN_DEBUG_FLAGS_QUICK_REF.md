# Terrain System Debug Flags - Quick Reference

## Overview
Two debug flags allow disabling terrain features for performance testing and debugging.

## Flags Location
**TerrainConfigAuthoring** Inspector → **Debug/Testing** section:

1. **Enable Terrain Tile Rendering** (default: true)
   - Controls visual rendering of terrain tiles
   - Controls mesh generation
   - Controls tree spawning
   
2. **Enable Physics Colliders** (default: true)
   - Controls physics collider generation
   - Controls LOD distance tracking
   - Controls collider preparation jobs

## Systems Affected

### When `renderTerrain = false`:
- ✅ **TileSpawningSystem** - Skipped (if physics also disabled)
- ✅ **TerrainMeshGenerationSystem** - Skipped
- ✅ **TerrainRenderingSystem** - No work (no meshes to render)
- ✅ **TerrainTreeSpawningSystem** - Skipped

### When `enablePhysicsColliders = false`:
- ✅ **TerrainDistanceTrackingSystem** - Skipped
- ✅ **TerrainColliderPreparationSystem** - Skipped
- ✅ **TerrainPhysicsSystem** - Skipped

### When BOTH = false:
- ✅ **TileSpawningSystem** - Skipped (no tiles needed at all)
- ✅ **Entire terrain system dormant** (<0.01ms/frame overhead)

## Performance Savings

### Rendering Disabled Only
- Mesh generation: ~1-3ms saved
- Tree spawning: ~0.5-1ms saved  
- Tile spawning still active (for potential physics)

### Physics Disabled Only
- LOD tracking: ~0.1-0.5ms saved
- Collider prep jobs: ~1-3ms saved
- Collider creation: ~2-5ms saved
- Tile spawning still active (for rendering)

### Both Disabled
- **Total savings**: ~3-8ms/frame + eliminates periodic stutter spikes
- **Quest 3 impact**: Eliminates ~10-second stale frame issue
- **System overhead**: Near zero (<0.01ms)

## Use Cases

### Performance Testing
```
Disable rendering only → Test physics system cost
Disable physics only → Test rendering system cost
Disable both → Establish baseline without terrain
```

### VR Debugging
```
Both disabled → Debug player/ship movement without terrain overhead
Rendering only → Test ground collision without visuals
Physics only → Test visual quality without collision
```

### Development Workflow
```
Both disabled → Fast iteration on non-terrain features
Enable as needed → Incremental feature testing
```

## Implementation Details

All early exits added at the **start of OnUpdate()** before any queries/allocations:

```csharp
// TileSpawningSystem
if (!config.renderTerrain && !config.enablePhysicsColliders)
    return;

// TerrainMeshGenerationSystem, TerrainTreeSpawningSystem  
if (!config.renderTerrain)
    return;

// TerrainDistanceTrackingSystem, TerrainColliderPreparationSystem, TerrainPhysicsSystem
if (!config.enablePhysicsColliders)
    return;
```

## Related Files
- `TERRAIN_PHYSICS_DEBUG_FLAG.md` - Physics flag initial implementation
- `TERRAIN_PERFORMANCE_DEBUG_FIX.md` - Quest 3 stutter fix details
- `TerrainConfigAuthoring.cs` - Configuration component

