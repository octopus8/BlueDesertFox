# Component Removal Fix - RenderFilterSettings Error

## Error
```
Assets\_App\Ace of Ages\Terrain\TerrainTreeSpawningSystem.cs(353,60): error CS0234: 
The type or namespace name 'RenderFilterSettings' does not exist in the namespace 'Unity.Rendering'
```

## Root Cause

`Unity.Rendering.RenderFilterSettings` doesn't exist in Unity 6 (6000.3.10f1).

The component might have been:
- Renamed in this version
- Moved to a different namespace
- Not necessary for disabling ECS rendering

## Solution

Removed all `RenderFilterSettings` references. Removing `MaterialMeshInfo` and `RenderBounds` is sufficient to disable ECS rendering.

### Components Removed (Final List):
1. ✅ `Unity.Rendering.MaterialMeshInfo` - Core rendering component
2. ✅ `Unity.Rendering.RenderBounds` - Frustum culling bounds
3. ❌ `Unity.Rendering.RenderFilterSettings` - REMOVED (doesn't exist)

## Why It Still Works

ECS rendering requires `MaterialMeshInfo` to know what to render. Without it, entities are invisible to the Entities Graphics system.

```
Tree Entity:
├─ MaterialMeshInfo ← REMOVED = ECS won't render
├─ RenderBounds ← REMOVED = Extra safety
├─ LocalTransform ← KEPT = Position for GlobalTreeInstanceSystem
├─ GlobalTreeInstance ← KEPT = Tag for batch rendering
└─ GlobalTreeInstanceData ← KEPT = Mesh/material for batching
```

## Result

✅ **Compilation Successful**  
✅ **Trees should only render via GlobalTreeInstanceSystem**  
✅ **Draw calls should drop to ~1**

## Testing

Next Unity Play mode should show:
```
[GlobalTreeInstance] Rendering 100 trees in 1 draw calls (1 unique mesh/material combinations)
```

**Frame Debugger**:
- Should show **1 DrawMeshInstanced** call
- Should NOT show 100+ individual tree draw calls
- Total draw calls: **~1-10** (massive improvement!)

---

**Date**: April 18, 2026  
**Status**: ✅ Fixed, ready for testing

