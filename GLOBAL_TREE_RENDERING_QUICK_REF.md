# Global Tree Instance Rendering - Quick Reference

## Quick Setup Check

### 1. Verify Tree Prefabs
✅ Tree prefabs must have:
- `MeshFilter` component (sharedMesh assigned)
- `MeshRenderer` component (sharedMaterial assigned)
- Material with **"Enable GPU Instancing"** checked

### 2. Expected Console Output
When trees spawn, you should see:
```
[GlobalTreeInstance] Rendering 150 trees in 1 draw calls (1 unique mesh/material combinations)
```

### 3. Frame Debugger Verification
1. Window → Analysis → Frame Debugger → Enable
2. Look for: `Drawing → DrawMeshInstanced`
3. Should see 1-5 draw calls instead of 100+

## Component Quick Reference

| Component | Type | Purpose |
|-----------|------|---------|
| `GlobalTreeInstance` | Tag | Marks tree for batch rendering |
| `GlobalTreeInstanceData` | Managed | Stores mesh/material refs |
| `TreeTileOwnership` | Data | Unchanged - position updates |
| `LocalTransform` | Data | Unchanged - position/rotation |

## System Quick Reference

| System | Update Group | Purpose |
|--------|--------------|---------|
| `TerrainTreeSpawningSystem` | Simulation | Spawns trees + adds components |
| `TreePositionUpdateSystem` | Transform | Updates tree positions (unchanged) |
| `GlobalTreeInstanceSystem` | Presentation | Renders all trees via batching |

## Performance Targets

| Metric | Before | After | Target |
|--------|--------|-------|--------|
| Draw Calls | ~100 | ~1-5 | <10 |
| CPU Time | ~3-5ms | ~0.5-1ms | <2ms |
| Trees per Draw Call | 1-5 | 500+ | 1023 max |

## Troubleshooting (Quick)

### No Trees Visible?
```csharp
// Check in Entity Debugger or console
[GlobalTreeInstance] Rendering 0 trees...  // ← Problem!
```
**Fix**: Verify mesh/material extraction in console warnings

### Still 100 Draw Calls?
- Check material has **"Enable GPU Instancing"** ✅
- Check Frame Debugger shows "DrawMeshInstanced" (not "Draw")

### Trees in Wrong Position?
- `TreePositionUpdateSystem` unchanged - check tile scrolling

## Code Snippets

### Check if Using Global Rendering
```csharp
foreach (var entity in SystemAPI.Query<RefRO<GlobalTreeInstance>>().WithEntityAccess())
{
    Debug.Log($"Tree {entity.Index} using global rendering");
}
```

### Manually Add Global Rendering to Tree
```csharp
EntityManager.AddComponent<GlobalTreeInstance>(treeEntity);
EntityManager.AddComponentData(treeEntity, new GlobalTreeInstanceData
{
    mesh = treeMesh,
    material = treeMaterial,
    prefabIndex = 0
});
```

### Count Trees per Batch
```csharp
// In GlobalTreeInstanceSystem, add debug output
Debug.Log($"Batch with {mesh.name}: {batch.matrices.Count} trees");
```

## Profiler Markers

Monitor these in Unity Profiler:
- `GlobalTreeInstance.Render` - Total (target: <1ms)
- `GlobalTreeInstance.Collect` - Collection (target: <0.5ms)
- `GlobalTreeInstance.Draw` - Drawing (target: <0.3ms)

## Files Reference

| File | Location | Purpose |
|------|----------|---------|
| Components | `TileComponents.cs` | GlobalTreeInstance, GlobalTreeInstanceData |
| Rendering | `GlobalTreeInstanceSystem.cs` | Batch rendering system |
| Spawning | `TerrainTreeSpawningSystem.cs` | Modified to add components |
| Docs | `GLOBAL_TREE_RENDERING_IMPLEMENTATION.md` | Full documentation |

## Common Settings

### Single Tree Type (Best Performance)
- **Expected**: 1 draw call for all trees
- **Verify**: `1 unique mesh/material combinations`

### Multiple Tree Types
- **Expected**: 1 draw call per material
- **Example**: 3 tree types = 3 draw calls (vs 100+ before)

### Large Tree Count (>1023)
- **Automatic**: System splits into multiple batches
- **Example**: 2000 trees = 2 draw calls (1023 + 977)

## Key Differences from Old System

| Aspect | Old (ECS Rendering) | New (Global Batching) |
|--------|-------------------|---------------------|
| Draw Calls | 1 per tree/group | 1 per 1023 trees |
| Batching | Limited | Maximum |
| Components | MaterialMeshInfo, RenderBounds | GlobalTreeInstance |
| Frustum Culling | Per-entity | Per-batch (all) |
| Performance | Good | Excellent |

## Date
Implemented: April 18, 2026

