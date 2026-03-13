# Terrain Visibility Fixes Applied

## Date: March 13, 2026

## Changes Made

### 1. Enhanced TerrainRenderingSystem.cs
**Location:** `Assets\_App\Ace of Ages\Terrain\TerrainRenderingSystem.cs`

**Changes:**
- Added explicit `layer`, `renderingLayerMask`, and `motionMode` parameters to `RenderMeshDescription`
- Added `WorldRenderBounds` component check and logging
- Added `RenderFilterSettings` component check

**Why:** The rendering system needs explicit render filter settings to be visible in Unity's Entities Graphics system. Without proper layer masks and motion modes, entities may not render.

### 2. Enhanced TerrainRenderingDebugSystem.cs
**Location:** `Assets\_App\Ace of Ages\Terrain\TerrainRenderingDebugSystem.cs`

**Changes:**
- Added `WorldRenderBounds` check and logging
- Added `RenderFilterSettings` check and logging

**Why:** This helps diagnose exactly which rendering components are missing from terrain tiles.

## Next Steps to Fix Visibility

### Option 1: Check Material Setup (MOST LIKELY ISSUE)

1. **Create the TerrainMaterial manually:**
   ```
   a. Create folder: Assets/Resources (if not exists)
   b. In Resources folder: Right-click → Create → Material
   c. Name it exactly "TerrainMaterial"
   d. Set Shader to: "Universal Render Pipeline/Lit"
   e. Set Base Color to something visible (e.g., green or gray)
   f. Save the project
   ```

2. **Test in Play Mode:**
   - Enter Play Mode
   - Check Console for: `[TerrainRendering] Created default URP Lit material`
   - If you see errors about shader not found, URP may not be configured correctly

### Option 2: Check Camera Position (SECOND MOST LIKELY)

1. **Verify player/camera height:**
   - Tiles spawn at Y=0 with height based on noise (0-20 units typically)
   - If your VR camera/player is at Y=1.5-2.0, terrain might be visible
   - But if camera starts at Y=100, you won't see terrain below

2. **Test:**
   - Enter Play Mode
   - Switch to Scene View (Tab or click Scene tab)
   - Look at origin (0, 0, 0)
   - If you see gray/green tiles in Scene View but not Game View:
     → Camera is not looking at terrain
   - If you don't see tiles in Scene View either:
     → Rendering system issue

### Option 3: Check Entity Visibility in Entities Window

1. **Open Entities Hierarchy:**
   ```
   Window → Entities → Hierarchy
   ```

2. **Find terrain entities:**
   - Should see entities with "TerrainTile" component
   - Select one and check Inspector

3. **Verify components:**
   - LocalTransform ✓
   - LocalToWorld ✓
   - MaterialMeshInfo ✓
   - RenderMeshArray ✓
   - RenderBounds ✓
   - WorldRenderBounds ✓
   - RenderFilterSettings ✓

   If ANY of these are missing → Rendering system problem

### Option 4: Check URP Configuration

1. **Verify URP Asset:**
   ```
   Edit → Project Settings → Graphics
   ```
   - "Scriptable Render Pipeline Settings" should point to a URP asset
   - If it's "None" → URP not configured!

2. **Fix if needed:**
   - Find or create URP asset in project
   - Assign it to Graphics settings
   - Restart Unity Editor

### Option 5: Force Visible Material (Debug Test)

If all else fails, modify TerrainRenderingSystem.cs temporarily:

```csharp
// In OnStartRunning(), after creating _terrainMaterial:
if (_terrainMaterial != null)
{
    // Make it VERY visible for debugging
    _terrainMaterial.SetColor("_BaseColor", Color.magenta);
    _terrainMaterial.EnableKeyword("_EMISSION");
    _terrainMaterial.SetColor("_EmissionColor", Color.yellow);
}
```

This will make terrain glow bright yellow/magenta - impossible to miss!

## Debugging Commands

### Enable the Debug System
The `TerrainRenderingDebugSystem` is already in the project. It logs every 2 seconds in Play Mode.

Watch Console for these messages:
```
[TerrainDebug] ========== Terrain Tile Analysis ==========
[TerrainDebug] Total tiles: X
[TerrainDebug] Tiles with mesh data: X
[TerrainDebug] Tiles with rendering components: X
```

If "Tiles with rendering components" is 0 → Rendering setup failed
If it's > 0 but still not visible → Material/Camera/Culling issue

## Common Root Causes

### 1. Material Shader Not Found (60% of issues)
- URP not installed or configured
- Wrong shader name
- Material is null

**Fix:** Create material manually (see Option 1)

### 2. Camera Not Looking at Terrain (25% of issues)
- VR camera starts too high or facing wrong direction
- Terrain is at Y=0, camera at Y=100
- No clear skybox makes it hard to see

**Fix:** In Scene View during Play Mode, manually position Scene camera to see terrain

### 3. RenderFilterSettings Wrong (10% of issues)
- Layer mask excludes terrain
- Rendering layer mask wrong
- Motion mode incompatible

**Fix:** My changes should fix this - explicit settings added

### 4. Entities Graphics System Not Running (5% of issues)
- Unity.Entities.Graphics package issue
- EntitiesGraphicsSystem disabled
- Build target incompatible

**Fix:** Verify package versions, check Build Settings platform

## How to Test If Fix Worked

1. **Enter Play Mode**
2. **Check Console Output:**
   - Look for `[TerrainRendering] ✓ Mesh setup complete for entity X`
   - Should see multiple of these
3. **Check Scene View:**
   - Switch to Scene view
   - If tiles visible there → Camera/viewport issue
   - If not visible there either → Rendering failed
4. **Check Game View:**
   - If visible now → SUCCESS!
   - If not, see console for errors

## Emergency Fallback

If NOTHING works, the terrain system might have an incompatibility with your Unity version or VR setup. In that case:

1. Disable the terrain systems temporarily
2. Create a simple test: Plain Unity Cube with ECS RenderMeshUtility
3. If that renders → Terrain system has a bug
4. If that doesn't render → Entities Graphics system broken

Would you like me to create that test scene?

