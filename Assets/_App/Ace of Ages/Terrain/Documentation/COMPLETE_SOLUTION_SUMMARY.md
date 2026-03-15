# Terrain Visibility Fix - Complete Summary

## Date: March 13, 2026

## Problem
Terrain tiles were being created by the ECS system but not rendering/visible in the scene.

## Root Cause Analysis
After investigating the terrain system, I identified several potential issues:

1. **Missing TerrainMaterial** - The system expects a "TerrainMaterial" in Assets/Resources/ but it didn't exist
2. **Incomplete RenderMeshDescription** - Layer, rendering layer mask, and motion mode were not explicitly set
3. **Insufficient debugging** - Hard to diagnose which rendering component was missing

## Fixes Applied

### 1. Created TerrainMaterialCreator.cs (NEW FILE)
**Location:** `Assets\_App\Ace of Ages\Terrain\Editor\TerrainMaterialCreator.cs`

**Purpose:** Automatically creates the missing TerrainMaterial when Unity compiles scripts

**Features:**
- Runs automatically on editor startup via `[InitializeOnLoad]`
- Creates Resources folder if needed
- Creates material with URP Lit shader
- Sets visible greenish-gray color
- Provides menu item: Tools → Terrain → Create Terrain Material

**Expected Result:** When you next open Unity, console will show:
```
[TerrainMaterialCreator] ✓ Created TerrainMaterial at: Assets/Resources/TerrainMaterial.mat
```

### 2. Enhanced TerrainRenderingSystem.cs
**Location:** `Assets\_App\Ace of Ages\Terrain\TerrainRenderingSystem.cs`

**Changes:**
```csharp
// BEFORE:
var renderMeshDescription = new RenderMeshDescription(
    shadowCastingMode: ShadowCastingMode.On,
    receiveShadows: true
);

// AFTER:
var renderMeshDescription = new RenderMeshDescription(
    shadowCastingMode: ShadowCastingMode.On,
    receiveShadows: true,
    layer: 0,  // Default layer - ADDED
    renderingLayerMask: 1,  // Default rendering layer mask - ADDED
    motionMode: MotionVectorGenerationMode.Camera  // ADDED
);
```

**Also added logging for:**
- WorldRenderBounds component
- RenderFilterSettings component

### 3. Enhanced TerrainRenderingDebugSystem.cs
**Location:** `Assets\_App\Ace of Ages\Terrain\TerrainRenderingDebugSystem.cs`

**Added checks for:**
- WorldRenderBounds (critical for culling)
- RenderFilterSettings (controls which camera sees the entity)

### 4. Created TestECSRenderingSystem.cs (NEW FILE)
**Location:** `Assets\_App\Ace of Ages\Terrain\TestECSRenderingSystem.cs`

**Purpose:** Creates a simple red cube to verify Entities Graphics is working

**What it does:**
- Creates a 2x2x2 cube at position (10, 2, 10)
- Uses the same rendering setup as terrain
- If you see this cube, terrain SHOULD render
- If you don't see this cube, there's a deeper Entities Graphics problem

## Testing Instructions

### Step 1: Wait for Unity to Recompile
1. Open Unity Editor
2. Wait for scripts to compile (bottom-right progress bar)
3. Check Console for: `[TerrainMaterialCreator] ✓ Created TerrainMaterial...`

### Step 2: Verify Material Exists
1. In Project window, navigate to: Assets → Resources
2. You should see "TerrainMaterial.mat"
3. Select it and verify in Inspector:
   - Shader: Universal Render Pipeline/Lit
   - Base Color: Greenish-gray

### Step 3: Enter Play Mode
1. Click Play button
2. Check Console for messages:
   ```
   [TestECSRendering] Creating test cube at position (10, 2, 10)...
   [TestECSRendering] ✓ Test cube created
   [TileSpawning] Spawning X new tiles
   [TerrainMeshGen] Generating mesh for tile at (0, 0)
   [TerrainRendering] Processing X tiles for rendering setup
   [TerrainRendering] ✓ Mesh setup complete for entity X
   ```

### Step 4: Look for Test Cube
1. In Scene View, navigate to position (10, 2, 10)
2. You should see a red or greenish cube (2x2x2 meters)
3. If YES: Entities Graphics works! Terrain should be visible too
4. If NO: Deeper issue (see Troubleshooting below)

### Step 5: Look for Terrain
1. In Scene View, navigate to origin (0, 0, 0)
2. If player/camera is at origin, terrain tiles should surround it
3. Tiles are 100x100 meters each
4. Should see greenish-gray procedural terrain

### Step 6: Check Debug Output
Every 2 seconds, TerrainRenderingDebugSystem logs status:
```
[TerrainDebug] ========== Terrain Tile Analysis ==========
[TerrainDebug] Total tiles: 9
[TerrainDebug] Tiles with mesh data: 9
[TerrainDebug] Tiles with rendering components: 9
```

**Good signs:**
- All counts match (9 = 9 = 9)
- "Missing" warnings are gone
- WorldRenderBounds present

**Bad signs:**
- "Tiles with rendering components: 0"
- Multiple "Missing XYZ!" warnings
- Mesh is null

## Expected Results

### Success Case
- ✅ TerrainMaterial created in Resources
- ✅ Test cube visible at (10, 2, 10)
- ✅ Terrain tiles visible around player/origin
- ✅ Console shows mesh generation success
- ✅ No errors in Console

### Partial Success Case
- ✅ Test cube visible
- ❌ Terrain not visible
- **Likely cause:** Camera position or tile spawning logic
- **Next step:** Check player position vs tile positions in console

### Failure Case
- ❌ Test cube NOT visible
- ❌ Terrain NOT visible
- **Likely cause:** Entities Graphics system not working
- **Next step:** See Troubleshooting → Fundamental Issue below

## Troubleshooting

### Issue: TerrainMaterial Not Created
**Symptoms:**
- No message in console about material creation
- Resources folder empty

**Solution:**
1. Manually run: Tools → Terrain → Create Terrain Material
2. Check console for errors
3. If shader not found: URP not installed/configured

### Issue: Shader Not Found Error
**Symptoms:**
```
[TerrainMaterialCreator] Failed to find 'Universal Render Pipeline/Lit' shader!
```

**Solution:**
1. Open: Edit → Project Settings → Graphics
2. Verify "Scriptable Render Pipeline Settings" is set
3. Should point to a URP asset (e.g., UniversalRenderPipelineAsset)
4. If "None": Find URP asset in project and assign it
5. Restart Unity Editor

### Issue: Test Cube Not Visible
**Symptoms:**
- Console says "Test cube created"
- Can't find cube in Scene View at (10, 2, 10)

**Solution:**
This indicates a fundamental Entities Graphics issue:

1. **Check Entities Graphics package:**
   - Window → Package Manager
   - Search for "Entities Graphics"
   - Should be installed (comes with Entities)

2. **Check World creation:**
   - Window → Entities → Systems
   - Should see "EntitiesGraphicsSystem"
   - If missing: Entities Graphics not initialized

3. **Check Build Target:**
   - File → Build Settings
   - Some platforms don't support Entities Graphics
   - Windows/Mac/Linux should work

### Issue: Tiles Spawning But Not Visible
**Symptoms:**
- Console shows tile creation
- Debug system shows "Tiles with rendering components: 9"
- Still can't see terrain

**Solution:**
1. **Check camera position:**
   - In Scene View during Play Mode, go to (0, 0, 0)
   - Should see terrain there if spawning near origin
   
2. **Check player position:**
   - Window → Entities → Hierarchy
   - Find entity with "PlayerTag"
   - Check LocalTransform position
   - Tiles spawn around player, within viewDistance (default 300m)

3. **Check rendering bounds:**
   - In debug output, look for "WorldRenderBounds"
   - If Center or Extents are NaN or zero: Culling broken
   - Should see reasonable bounds (e.g., Center=(50, 10, 50), Extents=(50, 10, 50))

### Issue: Performance Problems
**Symptoms:**
- Tiles visible but game runs slowly
- Frame rate drops below 60 FPS

**Solution:**
Reduce terrain settings in TerrainConfigAuthoring:
- Vertices Per Side: 32 → 16
- View Distance: 300 → 200
- Noise Octaves: 4 → 2

## What Changed in Code

### Files Modified:
1. `TerrainRenderingSystem.cs` - Added explicit render settings
2. `TerrainRenderingDebugSystem.cs` - Added more debug checks

### Files Created:
1. `Editor\TerrainMaterialCreator.cs` - Auto-creates material
2. `TestECSRenderingSystem.cs` - Test cube for validation
3. `FIXES_APPLIED.md` - This document
4. `COMPLETE_SOLUTION_SUMMARY.md` - Detailed solution

### Assets Created:
1. `Resources\TerrainMaterial.mat` - Will be created automatically

## Next Steps If Still Not Working

### Option A: Simplified Test
Disable terrain systems and test just rendering:
1. Comment out terrain systems in their files
2. Keep only TestECSRenderingSystem active
3. If cube renders: Bug is in terrain logic
4. If cube doesn't render: Bug is in Entities Graphics setup

### Option B: Manual Material Assignment
Instead of Resources.Load, use direct reference:
1. In TerrainConfigAuthoring, add public Material field
2. Assign material in Inspector
3. Pass to baking system
4. Use that material instead of Resources.Load

### Option C: Traditional Mesh Rendering
Fall back to GameObject-based terrain:
1. Modify TerrainRenderingSystem to create GameObjects
2. Add MeshRenderer/MeshFilter components
3. Position GameObjects instead of entities
4. Slower but more compatible

## Contact/Support

If none of these fixes work, the issue might be:
1. Unity version incompatibility
2. VR-specific rendering issue
3. Platform-specific limitation
4. Custom project settings interfering

In that case, I recommend:
- Check Unity Entities Graphics documentation
- Search Unity forums for similar issues
- Verify URP is working with simple non-ECS objects first
- Test on a clean project to isolate the issue

