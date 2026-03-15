# Terrain Rendering Fix - Changes Made

## Problem
Terrain tiles were being created and visible in the Editor Scene View (gizmos), but not rendering in the Game View.

## Root Causes Identified
1. **MaterialMeshInfo Invalid State**: The `MaterialMeshInfo` component was being added but not properly initialized with valid mesh and material IDs from the `EntitiesGraphicsSystem`.
2. **Missing LocalToWorld**: Tiles were created with `LocalTransform` but the `LocalToWorld` component wasn't always updated in time for rendering systems.
3. **Material Issues**: Terrain material wasn't being created with proper debug colors to verify rendering.

## Changes Made

### 1. TerrainRenderingSystem.cs
**Added proper mesh/material registration**:
- Now explicitly registers meshes and materials with `EntitiesGraphicsSystem` using `RegisterMesh()` and `RegisterMaterial()`
- This ensures valid IDs are assigned before creating `MaterialMeshInfo`
- Added logging to verify registration succeeds

**Improved material creation**:
- Added fallback shader search (URP Lit → Standard → Unlit/Color)
- Set bright pink debug color `(1, 0.5, 0.8)` to make terrain visible during testing
- Added more detailed logging about which shader is being used

**Added MaterialMeshInfo verification**:
- After adding render components, now logs the actual mesh and material IDs
- Helps debug if registration is working correctly

### 2. TileSpawningSystem.cs
**Explicitly added LocalToWorld component**:
- When creating tiles, now explicitly adds `LocalToWorld` component with initialized value
- Computed from `LocalTransform` using `float4x4.TRS()`
- Ensures transform is ready for rendering immediately

### 3. TerrainRenderingDebugSystem.cs
**Added error handling**:
- Wrapped `MaterialMeshInfo` access in try-catch to prevent assertion failures
- Now logs when `MaterialMeshInfo` exists but is invalid
- Added camera information logging (position, culling mask, far clip distance)

**Improved system ordering**:
- Changed to use `OrderLast = true` in `PresentationSystemGroup`
- Ensures it runs after all transform updates

## Testing Instructions

### 1. Open the Test Scene
- Open `Assets/_App/Ace of Ages/Ace of Ages.unity`

### 2. Required Scene Setup
Ensure the scene has:
- **TerrainConfig GameObject** with `TerrainConfigAuthoring` component
- **Player/Camera Entity** with `PlayerTagAuthoring` component and `LocalTransform`
- **Main Camera** with proper culling mask (should include Default layer)

### 3. Enter Play Mode
Watch the Console for debug logs:

#### Expected Successful Output:
```
[TileSpawning] Spawning X new tiles
[TileSpawning] Created tile at grid (x,y), world position (x,0,z)
[TerrainRendering] Processing X tiles for rendering setup
[TerrainRendering] Tile at (x,y): verts=1024, indices=6000
[TerrainRendering] Registered mesh ID: 123, material ID: 456
[TerrainRendering] MaterialMeshInfo: Mesh=123, Material=456
[TerrainDebug] Total tiles: X
[TerrainDebug] Tiles with rendering components: X
```

#### What to Look For:
1. **Pink terrain tiles** should be visible in Game View (debug color)
2. No assertion errors about MaterialMeshInfo
3. Debug logs showing valid mesh/material IDs (not 0)
4. WorldRenderBounds should have non-zero extents

### 4. If Still Not Visible

**Check Camera**:
- Is Main Camera position near (0, 10, 0) or within view distance of tiles?
- Is culling mask set to include Default layer (layer 0)?
- Is far clip plane large enough (e.g., 1000+)?

**Check Materials**:
- Does the created material have a valid shader?
- Is the color actually pink in logs?

**Check Transforms**:
- Do tiles have both `LocalTransform` AND `LocalToWorld`?
- Is `LocalToWorld.Position` showing reasonable values?

**Check Render Bounds**:
- Are `RenderBounds` and `WorldRenderBounds` present?
- Do bounds have non-zero extents?
- Are bounds overlapping camera frustum?

### 5. Viewing Debug Info
The `TerrainRenderingDebugSystem` logs detailed info every 2 seconds:
- Total tile count and tile states
- Camera position and settings
- First tile's complete component breakdown
- Mesh data (vertex count, bounds)
- All rendering components

## Next Steps If Issues Persist

1. **Create a minimal test scene**:
   - Single cube with ECS rendering (verify ECS rendering pipeline works)
   - Single terrain tile at (0,0) with player at (0, 10, 0)

2. **Check Unity Settings**:
   - Project Settings → Graphics → Scriptable Render Pipeline Settings
   - Verify URP asset is assigned
   - Check render scale, HDR, anti-aliasing settings

3. **Verify Entities Graphics Package**:
   - Package Manager → Entities Graphics
   - Should be compatible with Unity 6 and current Entities version
   - Try reimporting if needed

4. **Check Frame Debugger**:
   - Window → Analysis → Frame Debugger
   - Look for terrain draw calls
   - Verify meshes are being submitted to GPU

## Files Modified
- `Assets/_App/Ace of Ages/Terrain/TerrainRenderingSystem.cs`
- `Assets/_App/Ace of Ages/Terrain/TileSpawningSystem.cs`
- `Assets/_App/Ace of Ages/Terrain/TerrainRenderingDebugSystem.cs`

## Temporary Debug Features
- Pink material color (change to gray/green once working)
- Verbose debug logging (can reduce once stable)
- Debug system running every 2 seconds (can disable entirely once working)

