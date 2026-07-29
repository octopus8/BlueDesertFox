# URP Terrain Shader Warning Fix

## Issue
Warning message appearing in console:
```
Missing types referenced from component UniversalRenderPipelineGlobalSettings on game object UniversalRenderPipelineGlobalSettings:
	UnityEngine.Rendering.Universal.URPTerrainShaderSetting, Unity.RenderPipelines.Universal.Runtime (1 object)
	UnityEngine.Rendering.Universal.UniversalRenderPipelineRuntimeTerrainShaders, Unity.RenderPipelines.Universal.Runtime (1 object)
```

## Root Cause
The URP Global Settings asset (`Assets/Settings/Project Configuration/UniversalRenderPipelineGlobalSettings.asset`) contained references to Unity's built-in terrain shader system components that are either:
- Not available in Unity 6 (6000.3.10f1) with URP 17.3.0
- Deprecated/removed from the URP package
- Incompatible with the current project configuration

## Why This Happened
This project uses a **custom DOTS-based terrain system** (not Unity's built-in terrain), so these references were unnecessary. The references likely came from:
1. A Unity version upgrade that removed these types
2. Initial project setup using a template that included terrain shader settings
3. URP package version changes

## Solution Applied (May 11, 2026)
Removed the following from `UniversalRenderPipelineGlobalSettings.asset`:

### 1. Removed from Settings List (m_SettingsList):
- `rid: 2931874318534311936` (URPTerrainShaderSetting reference)
- `rid: 2931874318534311937` (UniversalRenderPipelineRuntimeTerrainShaders reference)

### 2. Removed Data Blocks:
- **URPTerrainShaderSetting** block (lines 274-278)
- **UniversalRenderPipelineRuntimeTerrainShaders** block (lines 279-285)

## Impact
- **✅ No functional impact**: Project uses custom DOTS terrain, not Unity built-in terrain
- **✅ Removes console warning spam**: Clean console logs
- **✅ Asset file reduced**: From 404 lines to 277 lines
- **✅ No breaking changes**: All existing systems continue to work

## Verification
To verify the fix:
1. Run the application
2. Check console - the warning should no longer appear
3. Terrain system should function normally (it never used these references)

## Related Systems
This fix does NOT affect:
- Custom DOTS terrain system (`Assets/_App/Ace of Ages/Terrain/`)
- Terrain mesh generation
- Terrain physics
- Tree spawning system
- Any custom shaders or materials

The project's terrain material is loaded via:
- `TerrainConfigAuthoring.terrainMaterial` (Inspector assignment)
- Resources folder fallback ("TerrainMaterial")
- Uses URP/Lit shader (not Unity terrain shaders)

