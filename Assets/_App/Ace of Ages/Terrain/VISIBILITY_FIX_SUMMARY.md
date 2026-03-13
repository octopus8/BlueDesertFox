# Visibility Issue Fix - Summary

## Problem
User reported: "Tiles are being created, but are not visible"

## Diagnosis Approach

Added comprehensive debug logging to identify where the rendering pipeline is failing.

## Changes Made

### 1. TerrainRenderingSystem.cs - Enhanced Logging
Added debug output to track:
- Number of tiles being processed for rendering
- Mesh creation details (vertex count, triangle count, bounds)
- Component addition (MaterialMeshInfo, RenderBounds, LocalToWorld)
- Final entity state after setup
- Any errors during RenderMeshUtility.AddComponents

### 2. TerrainMeshGenerationSystem.cs - Enhanced Logging
Added debug output to track:
- Number of tiles being processed for mesh generation
- Which tiles are generating meshes (grid coordinates)
- Completion confirmation with vertex/triangle counts

### 3. TileSpawningSystem.cs - Enhanced Logging
Added debug output to track:
- Number of tiles being spawned
- Grid coordinates and world positions of new tiles
- Number of tiles being despawned

### 4. TerrainRenderingDebugSystem.cs - NEW
Created comprehensive diagnostic system that logs every 2 seconds:
- Total tile count
- Tiles with mesh data
- Tiles with rendering components
- Tiles with LocalToWorld, RenderBounds
- Detailed inspection of first tile including all components
- Mesh details and bounds information

### 5. VISIBILITY_TROUBLESHOOTING.md - NEW
Created detailed troubleshooting guide covering:
- How to read debug output
- Common issues and fixes
- Manual verification steps
- Quick fixes to try
- Emergency fallback rendering method
- Expected visual results

## What the User Should See Now

When entering Play Mode, Console should show detailed output like:

```
[TileSpawning] Spawning 30 new tiles
[TileSpawning] Created tile at grid (0, 0), world position (0, 0, 0)
[TerrainMeshGen] Generating mesh for tile at (0, 0)
[TerrainMeshGen] ✓ Mesh generated: 1024 vertices, 1922 triangles for tile at (0, 0)
[TerrainRendering] Processing 30 tiles for rendering setup
[TerrainRendering] Creating mesh for entity 45
[TerrainRendering] Mesh created: 1024 verts, 1922 tris, bounds=Center: (50.0, 10.0, 50.0) ...
[TerrainRendering] RenderBounds: center=(50, 10, 50), extents=(50, 20, 50)
[TerrainRendering] Entity has LocalTransform: True
[TerrainRendering] Entity has LocalToWorld: False
[TerrainRendering] RenderMeshUtility.AddComponents succeeded for entity 45
[TerrainRendering] Has MaterialMeshInfo: True
[TerrainRendering] Has RenderMeshArray: True
[TerrainRendering] Has RenderBounds: True
[TerrainRendering] Tile position: (0, 0, 0), rotation: (0, 0, 0, 1), scale: 1
[TerrainRendering] ✓ Mesh setup complete for entity 45
```

And every 2 seconds:
```
[TerrainDebug] ========== Terrain Tile Analysis ==========
[TerrainDebug] Total tiles: 30
[TerrainDebug] Tiles with mesh data: 30
[TerrainDebug] Tiles with rendering components: 30
[TerrainDebug] Tiles with LocalToWorld: 30
[TerrainDebug] Tiles with RenderBounds: 30
[TerrainDebug] --- First Tile Detail (Entity 45:1) ---
... detailed component info ...
```

## Most Likely Issues Based on Logging

### Issue A: Material Problem
If log shows "material is null" or shader not found:
- URP not properly set up
- Shader "Universal Render Pipeline/Lit" doesn't exist
- Need to manually create and assign material

### Issue B: RenderMeshUtility Fails
If log shows "Failed to add render components":
- Unity.Rendering package issue
- API version mismatch
- Graphics settings incorrect

### Issue C: Tiles Below Camera
If everything logs correctly but still not visible:
- Tiles might be below player/camera
- Check tile positions in log
- Compare with camera position
- Terrain height (amplitude=20) might be too low

### Issue D: Culling Problem
If tiles render but disappear:
- RenderBounds calculation issue
- Camera frustum culling too aggressive
- Check bounds in debug log match mesh size

## Next Diagnostic Steps for User

1. **Run the project in Play Mode**
2. **Check Console for the debug messages above**
3. **Post the output** - This will tell us exactly where it's failing:
   - If no [TileSpawning] messages: Player/config issue
   - If no [TerrainMeshGen] messages: Mesh generation not running
   - If no [TerrainRendering] messages: Rendering system not running
   - If [TerrainDebug] shows "Missing MaterialMeshInfo": Rendering setup failed
   - If all logs look good but still not visible: Camera/position issue

4. **Check Scene view** (not Game view) to see if tiles visible there

5. **Check Frame Debugger** (Window → Analysis → Frame Debugger) for draw calls

## Potential Quick Fixes

If logs show everything working but still not visible:

### Fix 1: Make Terrain Much Taller
```csharp
// In TerrainConfigAuthoring component in scene:
Noise Amplitude = 100  // Instead of 20
```

### Fix 2: Check Camera Position
- Ensure camera is at reasonable height (Y > 0)
- Look down at terrain
- Terrain spawns at Y=0 to Y=amplitude

### Fix 3: Verify Player Position
- Tiles spawn around player
- If player at (0, 1000, 0), tiles spawn at (0, 0, 0) to (0, amplitude, 0)
- Player might be far above terrain

### Fix 4: Check URP Asset
- Project Settings → Graphics → Scriptable Render Pipeline Settings
- Must have URP asset assigned
- Check URP asset has proper renderer

## Files Modified
- ✅ TerrainRenderingSystem.cs (added debug logging)
- ✅ TerrainMeshGenerationSystem.cs (added debug logging)
- ✅ TileSpawningSystem.cs (added debug logging)

## Files Created
- ✅ TerrainRenderingDebugSystem.cs (new diagnostic system)
- ✅ VISIBILITY_TROUBLESHOOTING.md (comprehensive guide)
- ✅ VISIBILITY_FIX_SUMMARY.md (this file)

## Status
✅ **Diagnostic systems in place**
⏳ **Waiting for user to run and report Console output**

The extensive logging will pinpoint exactly where in the rendering pipeline the issue occurs, allowing for a targeted fix.

