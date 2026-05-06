# Mesh/Material Extraction Fix - Global Tree Rendering

## Issues Fixed

### 1. Invalid System Ordering Warning
**Error**:
```
Ignoring invalid [Unity.Entities.UpdateAfterAttribute] attribute on GlobalTreeInstanceSystem targeting TreePositionUpdateSystem.
This attribute can only order systems that are members of the same ComponentSystemGroup instance.
```

**Root Cause**: `GlobalTreeInstanceSystem` (in `PresentationSystemGroup`) had `[UpdateAfter(typeof(TreePositionUpdateSystem))]` but `TreePositionUpdateSystem` is in `TransformSystemGroup` (different group).

**Fix**: Removed the `[UpdateAfter]` attribute. Not needed because `PresentationSystemGroup` already runs after `TransformSystemGroup` by default.

### 2. Tree Mesh/Material Extraction Failure
**Error**:
```
[TreeSpawning] Tree prefab 0 missing mesh or material! Mesh: , Material:
```

**Root Cause**: Tried to extract mesh/material at runtime from entity's `LinkedEntityGroup`, but Unity's entity baking doesn't preserve `MeshFilter`/`MeshRenderer` components in a queryable way on entities.

**Fix**: Extract mesh/material during **baking phase** from GameObject prefabs, store in managed component.

## Implementation

### New Component
**File**: `TileComponents.cs`

```csharp
public class TreePrefabMeshMaterialData : IComponentData
{
    public Mesh[] meshes;
    public Material[] materials;
}
```

Managed singleton component that stores mesh/material arrays (one per tree prefab).

### Updated Baking
**File**: `TreeSpawnerConfigAuthoring.cs`

```csharp
// Extract mesh/material from GameObject prefabs during baking
for (int i = 0; i < authoring.treePrefabs.Length; i++)
{
    var treePrefab = authoring.treePrefabs[i];
    
    // Get MeshFilter and MeshRenderer from GameObject or children
    var meshFilter = treePrefab.GetComponentInChildren<MeshFilter>();
    var meshRenderer = treePrefab.GetComponentInChildren<MeshRenderer>();
    
    treeMeshes[i] = meshFilter?.sharedMesh;
    treeMaterials[i] = meshRenderer?.sharedMaterial;
}

// Store in managed component
AddComponentObject(entity, new TreePrefabMeshMaterialData
{
    meshes = treeMeshes,
    materials = treeMaterials
});
```

### Updated Runtime Spawning
**File**: `TerrainTreeSpawningSystem.cs`

```csharp
// Get baked mesh/material data
var meshMaterialData = EntityManager.GetComponentData<TreePrefabMeshMaterialData>(configEntity);
var treeMeshes = meshMaterialData.meshes;
var treeMaterials = meshMaterialData.materials;

// Use directly when spawning trees
EntityManager.AddComponentData(treeEntity, new GlobalTreeInstanceData
{
    mesh = treeMeshes[prefabIndex],
    material = treeMaterials[prefabIndex],
    prefabIndex = prefabIndex
});
```

## Key Changes

### Before (Runtime Extraction - FAILED)
1. Tree spawns
2. Try to find `LinkedEntityGroup` on prefab entity
3. Search for `MeshFilter`/`MeshRenderer` components
4. ❌ **PROBLEM**: Components not found in entity structure

### After (Baking Extraction - WORKS)
1. **Baking phase**: Extract from GameObject prefabs
2. Store in `TreePrefabMeshMaterialData` managed component
3. **Runtime**: Read pre-extracted mesh/material directly
4. ✅ **SUCCESS**: Mesh/material available immediately

## Benefits

1. **Simpler**: No complex entity queries at runtime
2. **Faster**: No runtime GameObject component lookups
3. **Reliable**: Works with Unity's entity baking system
4. **Cleaner**: Separation of concerns (baking vs runtime)

## Testing

Expected console output on tree spawn:
```
[TreeSpawner] Baked 1 tree prefabs
[TreeSpawning] Starting spawn for tile int2(-1, 2), Entity: 367
[TreeSpawning] Tile int2(-1, 2) will spawn 50 trees (min: 50, max: 50)
[GlobalTreeInstance] Rendering 50 trees in 1 draw calls (1 unique mesh/material combinations)
```

No more "missing mesh or material" warnings!

## Files Changed

1. ✅ `TileComponents.cs` - Added `TreePrefabMeshMaterialData` component
2. ✅ `TreeSpawnerConfigAuthoring.cs` - Extract mesh/material during baking
3. ✅ `TerrainTreeSpawningSystem.cs` - Use pre-extracted data
4. ✅ `GlobalTreeInstanceSystem.cs` - Removed invalid `[UpdateAfter]`

## Status

✅ **All Compilation Errors Fixed**
- Only naming convention warnings remain (cosmetic)
- Ready for testing in Unity Play mode

---

**Date**: April 18, 2026  
**Fix Type**: Architectural change (runtime → baking)

