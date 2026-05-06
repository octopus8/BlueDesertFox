# Tree Mesh LOD System - Implementation Complete

## Summary

Successfully implemented a distance-based tree mesh LOD system with 3 LOD levels per tree type, spatial chunking for efficient updates, and hysteresis to prevent LOD flickering.

## Files Created/Modified

### New Files
1. **TreeLODUpdateSystem.cs** - Main LOD update system using spatial chunking
2. **TreeSpatialChunkingSystem.cs** - Assigns trees to 100m spatial chunks
3. **TreeLODDebugSystem.cs** - Editor-only visualization (green=LOD0, yellow=LOD1, orange=LOD2)

### Modified Files
1. **TreeSpawnerConfigAuthoring.cs** - Now uses TreeLODSet[] with 3 LODs per tree type
2. **TileComponents.cs** - Added TreeLODConfig, updated GlobalTreeInstanceData with LOD fields
3. **TerrainTreeSpawningSystem.cs** - Spawns trees with initial LOD based on distance to player

## Configuration Setup

### Step 1: Update Tree Prefabs in Inspector

1. Open your scene with `TerrainConfigAuthoring` GameObject
2. Find the `TreeSpawnerConfigAuthoring` component
3. **New structure**: Instead of `Tree Prefabs[]`, you now have `Tree LOD Sets[]`
4. For each tree type, create a TreeLODSet:
   - **Tree Type Name**: Descriptive name (e.g., "Oak Tree")
   - **LOD0**: Highest detail mesh (0-50m)
   - **LOD1**: Medium detail mesh (50-150m) - can be null, will use LOD0 as fallback
   - **LOD2**: Lowest detail mesh (150m+) - can be null, will use LOD1/LOD0 as fallback

### Step 2: Configure LOD Distances

Set these values in `TreeSpawnerConfigAuthoring`:
- **LOD0 Distance**: 50m (default) - transition from LOD0 to LOD1
- **LOD1 Distance**: 150m (default) - transition from LOD1 to LOD2  
- **LOD2 Distance**: 300m (default) - maximum distance for LOD2
- **LOD Hysteresis**: 5m (default) - prevents flickering at boundaries

### Step 3: Adjust Performance Settings

- **Max Chunks Updated Per Frame**: 7 (default) - balance between responsiveness and CPU cost
  - Higher = faster LOD updates, more CPU usage
  - Lower = slower LOD updates, less CPU usage

## Creating LOD Meshes

### Option A: Manual Mesh Variants
1. Create 3 separate prefabs per tree type with different mesh complexity
2. Example:
   - `Oak_LOD0.prefab` - 5000 triangles
   - `Oak_LOD1.prefab` - 1500 triangles  
   - `Oak_LOD2.prefab` - 500 triangles

### Option B: Unity's Mesh Simplification
1. Duplicate your tree prefab 3 times
2. Use ProBuilder or external tools to decimate meshes
3. Assign increasingly simplified meshes to LOD1 and LOD2 prefabs

## Debug Visualization

Enable debug visualization in Scene view:

```csharp
// In your code or via Unity Console
TreeLODDebugSystem.EnableVisualization = true;
```

This shows:
- **Green wireframe spheres** (radius 1m) = LOD0 trees (highest detail)
- **Yellow wireframe spheres** (radius 2m) = LOD1 trees (medium detail)
- **Orange wireframe spheres** (radius 3m) = LOD2 trees (lowest detail)
- **Gray wireframe cubes** (100m) = Spatial chunk boundaries

## How It Works

### Mesh Array Layout
Meshes are stored in a flattened array:
```
[Tree0_LOD0, Tree0_LOD1, Tree0_LOD2, Tree1_LOD0, Tree1_LOD1, Tree1_LOD2, ...]
```

### Mesh Index Calculation
```csharp
meshIndex = (treeTypeIndex * 3) + currentLODLevel
```
- Tree type 0, LOD0 = index 0
- Tree type 0, LOD1 = index 1  
- Tree type 0, LOD2 = index 2
- Tree type 1, LOD0 = index 3
- etc.

### LOD Transition Logic with Hysteresis

**Transitioning UP (farther away):**
Distance must exceed threshold + hysteresis:
- LOD0→LOD1: distance > (50m + 5m) = 55m
- LOD1→LOD2: distance > (150m + 5m) = 155m

**Transitioning DOWN (closer):**
Distance must go below threshold - hysteresis:
- LOD2→LOD1: distance < (150m - 5m) = 145m
- LOD1→LOD0: distance < (50m - 5m) = 45m

This prevents rapid flickering when a tree is exactly at a threshold distance.

### Spatial Chunking

- World divided into 100m × 100m chunks
- Each tree assigned to a chunk based on its XZ position
- Each frame: update ~7 chunks (player's chunk + neighbors + rotating distant chunks)
- For 10,000 trees: ~14 frame cycle (0.23s at 60fps) for full update coverage

### Performance Characteristics

**Expected Performance** (10,000 trees, mid-range VR hardware):
- LOD update system: <0.5ms per frame
- Spatial chunking: <0.1ms per frame
- Memory overhead: ~5 bytes per tree = 50KB total

**Chunk Update Pattern:**
- Always updates player's chunk + 8 neighbors (9 total)
- Rotates through distant chunks for full coverage
- Frame budget: 7 chunks per frame (configurable)

## Testing Checklist

1. ✅ Assign at least one TreeLODSet with LOD0 prefab
2. ✅ Enter Play mode - trees should spawn normally
3. ✅ Enable `TreeLODDebugSystem.EnableVisualization = true`
4. ✅ Move camera/player - observe LOD transitions (color changes in Scene view)
5. ✅ Check console for baking logs: `[TreeSpawner] Baked N tree types`
6. ✅ Profile LOD system: Look for "TreeLOD.Update" marker in Profiler

## Troubleshooting

### Trees not spawning
- Check console for: `[TreeSpawner] No tree LOD sets assigned`
- Ensure at least LOD0 is assigned for each tree type

### No LOD transitions
- Verify `TreeLODConfig` singleton exists (check Entity Debugger)
- Ensure player position is being tracked (check for PlayerTransformReference)
- Enable debug visualization to confirm LOD levels are changing

### Poor performance
- Reduce `maxChunksUpdatedPerFrame` from 7 to 3-4
- Increase LOD distances to transition earlier
- Use simpler LOD2 meshes (target <500 triangles)

### LOD flickering
- Increase `lodHysteresis` from 5m to 10m or 15m
- Check that LOD distances are sufficiently spaced apart

## Future Enhancements

### Possible Improvements
1. **Distance-based culling**: Hide trees beyond LOD2 distance entirely
2. **Billboard LODs**: Replace LOD2 with 2D billboards at extreme distances
3. **Async LOD updates**: Offload distance calculations to jobs for better parallelism
4. **Per-tree-type LOD distances**: Different trees use different transition distances
5. **Camera frustum filtering**: Only update LODs for visible trees

## Technical Details

### Component Structure
```csharp
// Per-tree components
struct GlobalTreeInstanceData {
    int meshIndex;              // Current mesh being rendered
    int materialIndex;          // Current material
    int treeTypeIndex;          // Which tree type (0-N)
    byte currentLODLevel;       // 0, 1, or 2
    float lastDistanceToPlayer; // For hysteresis
}

struct TreeChunkMembership {
    int2 chunkCoord;            // Which 100m chunk
}

// Singleton configuration
struct TreeLODConfig {
    float lod0Distance;         // LOD0->LOD1 threshold
    float lod1Distance;         // LOD1->LOD2 threshold
    float lod2Distance;         // Maximum distance
    float hysteresisDelta;      // Flickering prevention
    int maxChunksUpdatedPerFrame; // Performance budget
}
```

### System Update Order
1. `TreeSpatialChunkingSystem` - Assigns new trees to chunks
2. `TerrainDistanceTrackingSystem` - Updates terrain tile LODs
3. `TreeLODUpdateSystem` - Updates tree mesh LODs
4. `GlobalTreeInstanceSystem` - Renders trees via GPU instancing

## Expected Vertex Count Reduction

Assuming typical LOD reductions:
- **LOD0**: 5000 vertices (0-50m range)
- **LOD1**: 1500 vertices (50-150m range, 70% reduction)
- **LOD2**: 500 vertices (150m+ range, 90% reduction)

For 10,000 trees with even distribution:
- **Without LODs**: 50M vertices total
- **With LODs**: ~15M vertices total (70% reduction)

Actual reduction depends on player movement patterns and LOD distances.

---

**Implementation Date**: April 26, 2026  
**Status**: ✅ Complete - Ready for testing

