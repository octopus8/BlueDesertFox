# Tree Rendering Without Terrain - Fix

## Problem

Trees were not rendering when "Render Terrain" checkbox was unchecked.

## Root Cause

**Tree spawning requires `MeshReference` component**:
```csharp
// TerrainTreeSpawningSystem query:
SystemAPI.Query<RefRO<TerrainTile>>()
    .WithAll<MeshReference>()  ← REQUIRES THIS!
    .WithNone<TreesSpawned>()
```

**But TerrainRenderingSystem only added MeshReference when rendering**:
```csharp
// Old code:
if (!config.renderTerrain)
{
    return; // Early exit = no MeshReference added!
}
```

**Result**: When terrain rendering disabled → No MeshReference → No tree spawning!

## Solution

Modified `TerrainRenderingSystem` to **always** add `MeshReference` (needed for tree spawning logic) but **conditionally** skip rendering setup.

### Code Changes

**TerrainRenderingSystem.cs**:

```csharp
protected override void OnUpdate()
{
    var config = SystemAPI.GetSingleton<TerrainTileConfig>();
    bool shouldRender = config.renderTerrain;
    
    // Always process tiles for MeshReference (needed for tree spawning)
    // But skip rendering setup if disabled
    
    // Pass shouldRender to CreateAndAssignMesh
    CreateAndAssignMesh(entity, vertices, normals, uvs, indices, shouldRender);
}

private void CreateAndAssignMesh(..., bool shouldRender)
{
    // Create mesh and add MeshReference (ALWAYS)
    Mesh mesh = new Mesh();
    // ...set mesh data...
    EntityManager.AddComponentData(entity, new MeshReference { mesh = mesh });
    
    // Skip rendering setup if disabled (NEW!)
    if (!shouldRender)
    {
        Debug.Log("Added MeshReference but skipped rendering setup");
        return; // Don't add MaterialMeshInfo, RenderBounds, etc.
    }
    
    // Add rendering components (only if shouldRender=true)
    RenderMeshUtility.AddComponents(...);
}
```

## What Happens Now

### With renderTerrain = false:
```
Tile Created → Mesh Generated → MeshReference Added ✅
                                 ↓
                    Trees Spawn (MeshReference exists) ✅
                                 ↓
                    Trees Render (GlobalTreeInstanceSystem) ✅
                                 ↓
                    Terrain DOESN'T Render (no MaterialMeshInfo) ✅
```

### With renderTerrain = true:
```
Tile Created → Mesh Generated → MeshReference Added ✅
                              → MaterialMeshInfo Added ✅
                              → RenderBounds Added ✅
                                 ↓
                    Tiles Render (ECS system) ✅
                    Trees Render (GlobalTreeInstanceSystem) ✅
```

## Expected Console Output

### With renderTerrain = false:
```
[TerrainRendering] Added MeshReference for tile 367 but skipped rendering setup (renderTerrain=false)
[TreeSpawning] Enqueued tile int2(-1, 2), Entity: 367
[TreeSpawning] Starting spawn for tile int2(-1, 2), Entity: 367
[TreeSpawning] Tile int2(-1, 2) spawned 50 trees...
[GlobalTreeInstance] Found 50 trees with GlobalTreeInstance tag
[GlobalTreeInstance] Rendering 50 trees in 1 draw calls...
```

### With renderTerrain = true:
```
(No "skipped rendering setup" message)
[TreeSpawning] Enqueued tile int2(-1, 2), Entity: 367
[TreeSpawning] Starting spawn for tile int2(-1, 2), Entity: 367
[TreeSpawning] Tile int2(-1, 2) spawned 50 trees...
[GlobalTreeInstance] Found 50 trees with GlobalTreeInstance tag
[GlobalTreeInstance] Rendering 50 trees in 1 draw calls...
```

## Frame Debugger Results

### With renderTerrain = false:
- **Terrain Draw Calls**: 0 (no MaterialMeshInfo components)
- **Tree Draw Calls**: 1 (DrawMeshInstanced)
- **Total**: **1 draw call** ✅

### With renderTerrain = true:
- **Terrain Draw Calls**: ~10-25 (ECS batching)
- **Tree Draw Calls**: 1 (DrawMeshInstanced)
- **Total**: **~11-26 draw calls** ✅

## Files Modified

✅ **TerrainRenderingSystem.cs**
- Modified `OnUpdate()` to store `shouldRender` flag
- Modified `CreateAndAssignMesh()` to accept `shouldRender` parameter
- Always adds `MeshReference` for tree spawning
- Conditionally adds rendering components based on flag

## Benefits

1. **Tree spawning works** regardless of terrain rendering state
2. **Clean testing** - can isolate tree rendering
3. **No code duplication** - same mesh creation path
4. **Performance** - mesh still created once (needed for tree placement)

## Testing

1. **Uncheck "Render Terrain"** in TerrainConfigAuthoring
2. **Enter Play Mode**
3. **Trees should be visible** floating in space
4. **Terrain should be invisible**
5. **Frame Debugger** should show 1 draw call

---

**Date**: April 18, 2026  
**Fix**: MeshReference always added, rendering conditionally skipped

