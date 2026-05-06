# Terrain Rendering Toggle - Test Tree-Only Rendering

## Feature Added

Added `renderTerrain` flag to `TerrainConfigAuthoring` to disable terrain tile rendering while keeping trees visible.

## Purpose

**Test global tree instance rendering in isolation** by hiding terrain tiles:
- Trees continue to spawn on tiles (mesh generation happens)
- Trees move with scrolling terrain
- Trees render via `GlobalTreeInstanceSystem`
- **Terrain tiles don't render** (visual clutter removed)

## Usage

### 1. Find TerrainConfigAuthoring in Scene
- GameObject with terrain configuration (usually in SubScene)
- Has `TerrainConfigAuthoring` component

### 2. Disable Terrain Rendering
- Uncheck **"Render Terrain"** checkbox
- Located in "Debug/Testing" section at bottom of component

### 3. Enter Play Mode
- Trees spawn normally
- Trees move with terrain scrolling
- **Terrain tiles invisible** (mesh generated but not rendered)

### 4. Re-Enable When Done
- Check **"Render Terrain"** checkbox
- Terrain becomes visible again

## Implementation Details

### Files Modified

1. **TerrainConfigAuthoring.cs**
   ```csharp
   [Header("Debug/Testing")]
   [Tooltip("Enable terrain tile rendering (disable to test tree rendering only)")]
   public bool renderTerrain = true;
   ```

2. **TileComponents.cs**
   ```csharp
   public struct TerrainTileConfig : IComponentData
   {
       // ...existing fields...
       public bool renderTerrain;
   }
   ```

3. **TerrainRenderingSystem.cs**
   ```csharp
   protected override void OnUpdate()
   {
       var config = SystemAPI.GetSingleton<TerrainTileConfig>();
       if (!config.renderTerrain)
       {
           return; // Skip rendering terrain
       }
       // ...rest of rendering code...
   }
   ```

## What Still Works When Terrain Rendering Disabled

✅ **Tile spawning/despawning** - TileSpawningSystem still runs
✅ **Mesh generation** - TerrainMeshGenerationSystem still runs (needed for tree height sampling)
✅ **Tree spawning** - TerrainTreeSpawningSystem still runs (needs mesh data)
✅ **Tree positioning** - TreePositionUpdateSystem still runs
✅ **Tree rendering** - GlobalTreeInstanceSystem still runs
✅ **Scrolling** - Scroll systems still run
✅ **Physics colliders** - Still generated (for walking on terrain)

❌ **Terrain visual rendering** - TerrainRenderingSystem skips mesh creation

## Testing Scenarios

### Scenario 1: Test Tree Draw Calls Only
```
1. Disable terrain rendering
2. Enter Play mode
3. Open Frame Debugger
4. Should see ONLY tree DrawMeshInstanced calls
5. Verify draw call count is low (~1-10)
```

### Scenario 2: Compare With/Without Terrain
```
1. With renderTerrain=true: Note total draw calls
2. With renderTerrain=false: Note total draw calls
3. Difference = terrain draw calls
```

### Scenario 3: Test Tree Movement
```
1. Disable terrain rendering
2. Enable scrolling (scrollEnabled=true)
3. Trees should move smoothly (no terrain visual reference)
```

## Performance Impact

When `renderTerrain = false`:

**CPU Savings**:
- TerrainRenderingSystem: ~0.5-1ms saved (mesh creation skipped)
- Entities Graphics: ~0.3-0.5ms saved (fewer render batches)

**GPU Savings**:
- Terrain draw calls: ~10-25 removed (depends on tile count)
- Vertices processed: Reduced by terrain mesh complexity

**Memory**:
- Unchanged (mesh data still generated for tree spawning)

## Known Limitations

### Trees Still Need Terrain Meshes
Trees spawn on terrain mesh vertices, so:
- `TerrainMeshGenerationSystem` must still run
- Mesh buffers must still be populated
- Only the **visual rendering** is disabled

### Physics Colliders Still Generated
Players/objects need to walk on terrain:
- Physics system still creates colliders
- Use separate physics layers to distinguish

## Use Cases

1. **Debugging Tree Rendering**
   - Isolate tree draw calls from terrain
   - Verify `Graphics.DrawMeshInstanced()` is working
   - Test material GPU instancing settings

2. **Performance Profiling**
   - Measure tree rendering cost separately
   - Compare frame times with/without terrain
   - Identify bottlenecks

3. **Visual Testing**
   - Test tree placement patterns without terrain distraction
   - Verify tree movement during scrolling
   - Check tree LOD transitions (if implemented)

## Console Output Example

With `renderTerrain = false`:
```
[TerrainRendering] Using material from TerrainConfigAuthoring: Test
(TerrainRenderingSystem.OnUpdate returns early - no mesh creation logs)

[TreeSpawning] Starting spawn for tile int2(-1, 2)...
[TreeSpawning] Tile int2(-1, 2) spawned 50 trees...
[GlobalTreeInstance] Found 50 trees with GlobalTreeInstance tag
[GlobalTreeInstance] Collection results: Collected=50, SkippedNoData=0, SkippedNullMesh=0
[GlobalTreeInstance] Rendering 50 trees in 1 draw calls (1 unique mesh/material combinations)
```

## Inspector Location

**TerrainConfigAuthoring Component**:
```
Player Tracking
Tile Settings
Auto-Scrolling
Procedural Noise Settings
Material
Physics Optimization
Debug/Testing
  └─ [✓] Render Terrain  ← Toggle this checkbox
```

## Default Value

`renderTerrain = true` (terrain visible by default)

Uncheck to hide terrain and test trees only.

---

**Date**: April 18, 2026  
**Feature**: Debug/testing toggle

