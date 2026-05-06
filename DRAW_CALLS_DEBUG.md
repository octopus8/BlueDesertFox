# Global Tree Rendering Debug - High Draw Calls Investigation

## Problem
Draw calls are still high despite implementing global tree instance rendering system.

## Diagnostic Steps Added

### 1. GlobalTreeInstanceSystem Debug Logging
**File**: `GlobalTreeInstanceSystem.cs`

Added logging to check:
- If system is running
- How many trees with `GlobalTreeInstance` tag are found
- Batch count and draw calls

```csharp
// Early in OnUpdate:
int treeCount = 0;
foreach (var entity in SystemAPI.Query<RefRO<GlobalTreeInstance>>().WithEntityAccess())
{
    treeCount++;
}

if (treeCount == 0)
{
    Debug.Log("[GlobalTreeInstance] No trees with GlobalTreeInstance tag found");
    return;
}
else
{
    Debug.Log($"[GlobalTreeInstance] Found {treeCount} trees with GlobalTreeInstance tag");
}
```

### 2. RequireForUpdate Removed
**Problem**: `RequireForUpdate<GlobalTreeInstance>()` prevents system from running until at least one entity with the tag exists

**Fix**: Removed `RequireForUpdate`, system now always runs and checks manually

### 3. Tree Spawning Debug Logging
**File**: `TerrainTreeSpawningSystem.cs`

Added logging for first tree on each tile:
```csharp
if (actualTreesSpawned == 0)
{
    Debug.Log($"[TreeSpawning] First tree on tile {tile.gridCoordinate}: Entity {treeEntity.Index}, Mesh: {treeMeshes[prefabIndex]?.name}, Material: {treeMaterials[prefabIndex]?.name}");
}
```

## Expected Console Output (Next Run)

### If Working Correctly:
```
[TreeSpawning] Starting spawn for tile int2(-1, 2)...
[TreeSpawning] First tree on tile int2(-1, 2): Entity 400, Mesh: TreeMesh, Material: TreeMaterial
[TreeSpawning] Tile int2(-1, 2) spawned 50 trees...
[GlobalTreeInstance] Found 50 trees with GlobalTreeInstance tag
[GlobalTreeInstance] Rendering 50 trees in 1 draw calls (1 unique mesh/material combinations)
```

### If Trees Not Getting Components:
```
[TreeSpawning] Starting spawn for tile int2(-1, 2)...
[TreeSpawning] First tree on tile int2(-1, 2): Entity 400, Mesh: null, Material: null  ← PROBLEM!
[GlobalTreeInstance] No trees with GlobalTreeInstance tag found  ← PROBLEM!
```

### If Mesh/Material Null:
```
[TreeSpawning] First tree on tile int2(-1, 2): Entity 400, Mesh: , Material:   ← Empty names = null
```

## Possible Root Causes

### 1. Mesh/Material Not Extracted During Baking
**Check**: Look for `[TreeSpawner] Baked 1 tree prefabs` message
**Expected**: Should show the tree prefab was found
**Problem**: If mesh/material are null in baked data, trees won't render

**Fix**: Verify tree GameObject prefab has:
- `MeshFilter` component with assigned mesh
- `MeshRenderer` component with assigned material
- Material has "Enable GPU Instancing" checked

### 2. Managed Component Not Being Added
**Check**: See if `GlobalTreeInstanceData` is successfully added
**Problem**: `EntityManager.AddComponentData` with managed component might fail silently

**Test**: Add try-catch around component addition

### 3. System Not Finding Trees
**Check**: `[GlobalTreeInstance] Found X trees` message
**Problem**: Query not matching entities

**Possible Causes**:
- Trees have `DisableRendering` component (excluded by query)
- `GlobalTreeInstance` tag not actually added
- Trees destroyed before rendering

## Quick Manual Test

1. **Enter Play Mode**
2. **Wait for trees to spawn** (look for `[TreeSpawning]` messages)
3. **Check console for** `[GlobalTreeInstance]` messages
4. **Open Entity Debugger** (Window → Entities → Hierarchy)
5. **Find a tree entity** (search for entities with `TreeTileOwnership`)
6. **Check components**:
   - ✅ Should have: `GlobalTreeInstance` (tag)
   - ✅ Should have: `GlobalTreeInstanceData` (managed)
   - ✅ Should have: `LocalTransform`
   - ✅ Should have: `TreeTileOwnership`
   - ❌ Should NOT have: `MaterialMeshInfo`, `RenderBounds` (old ECS rendering)

## Next Steps

1. Run Unity Play mode with new debug logging
2. Check console output
3. If no `[GlobalTreeInstance]` logs → trees not getting components
4. If "Found 0 trees" → component not being added
5. If "Found X trees" but still high draw calls → `Graphics.DrawMeshInstanced` not working

---

**Date**: April 18, 2026  
**Debug Session**: In progress

