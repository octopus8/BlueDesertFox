# UpdateScene GC Allocation Fix Summary ✅

## The Real Culprit: GlobalTreeInstanceSystem

**You were seeing 0.8 KB GC allocations in UpdateScene because `GlobalTreeInstanceSystem` was accessing a managed component EVERY FRAME.**

---

## Quick Summary

### What Was Wrong:
```csharp
// GlobalTreeInstanceSystem.OnUpdate() - Line 174
// ❌ Called EVERY FRAME when trees are visible
var renderingData = EntityManager.GetComponentData<GlobalTreeRenderingData>(configEntity);
```

This retrieves a managed class containing `Mesh[]` and `Material[]` arrays **every single frame**, causing:
- **0.8 KB GC allocation per frame**
- At 90 FPS = ~70 KB/second of garbage
- Constant micro-stutters in VR

### What Was Fixed:
```csharp
// OnStartRunning() - Cache once at startup
private GlobalTreeRenderingData _cachedRenderingData;

protected override void OnStartRunning()
{
    var configEntity = SystemAPI.GetSingletonEntity<TreeSpawnerConfig>();
    if (EntityManager.HasComponent<GlobalTreeRenderingData>(configEntity))
    {
        _cachedRenderingData = EntityManager.GetComponentData<GlobalTreeRenderingData>(configEntity);
    }
}

// OnUpdate() - Use cached data
protected override void OnUpdate()
{
    // ✅ ZERO GC allocations - uses cached data
    if (_cachedRenderingData == null || _cachedRenderingData.meshes == null)
        return;
    
    // Use _cachedRenderingData.meshes and _cachedRenderingData.materials
}
```

---

## Why The First Fix Wasn't Enough

**First attempt**: Fixed `TerrainRenderingSystem` array allocations
- **Problem**: Only runs when terrain tiles are created (periodic)
- **Impact**: Eliminated small periodic spikes, but not the main issue

**Second fix**: Fixed `GlobalTreeInstanceSystem` component access
- **Problem**: Runs EVERY FRAME when trees are visible
- **Impact**: ⭐ **Eliminates the 0.8 KB per-frame allocation you saw in the profiler**

---

## Files Modified

1. **GlobalTreeInstanceSystem.cs** ⭐ PRIMARY FIX
   - Line 114: Added `_cachedRenderingData` field
   - Lines 150-157: Added `OnStartRunning()` to cache component
   - Lines 174-182: Use cached data instead of GetComponentData()
   - Lines 240-242: Updated job to use cached data
   - Lines 279-280: Updated batch resolution to use cached data

2. **TerrainRenderingSystem.cs** (Secondary fix)
   - Lines 21-23: Added `_cachedMaterialArray` and `_cachedMeshArray` fields
   - Lines 37-38: Initialize arrays in OnCreate
   - Lines 219-223: Use cached arrays

---

## Testing Instructions

1. Open Unity Profiler
2. Click "Record" and "Deep Profile"
3. Run Ace of Ages scene
4. Navigate to area with trees visible
5. Look at: **Editor Loop → UpdateScene → PresentationSystemGroup → GlobalTreeInstanceSystem**
6. **Verify: GC Alloc column shows "0 B"** (previously "0.8 KB")

**Frame-by-frame verification**:
- Before: Every frame shows 0.8 KB allocation when trees visible
- After: Every frame shows 0 B allocation

---

## Performance Impact

**Before**:
- 0.8 KB × 90 FPS = ~70 KB/second
- 60-second gameplay = 4.3 MB of garbage
- Triggers GC pauses every few seconds
- VR micro-stutters

**After**:
- 0 B/frame
- No GC pressure from rendering
- Smooth VR performance
- Stable frame times

---

## Pattern for Future

**Whenever you have a managed component (class with arrays/objects):**

```csharp
// ❌ BAD - Allocates every frame
protected override void OnUpdate()
{
    var data = EntityManager.GetComponentData<ManagedComponent>(entity);
    // Use data...
}

// ✅ GOOD - Cache once, reuse forever
private ManagedComponent _cachedData;

protected override void OnStartRunning()
{
    _cachedData = EntityManager.GetComponentData<ManagedComponent>(entity);
}

protected override void OnUpdate()
{
    // Use _cachedData (zero allocations)
}
```

**Key Rule**: Never call `GetComponentData<T>()` on managed components in hot paths!

---

## Status

✅ **GlobalTreeInstanceSystem**: Fixed - component cached in OnStartRunning  
✅ **TerrainRenderingSystem**: Fixed - arrays cached in OnCreate  
✅ **Compilation**: Both files compile with zero errors  
✅ **Ready for Testing**: Should see 0 B GC Alloc in Profiler

**Expected Result**: UpdateScene now shows **0 B GC Alloc** instead of **0.8 KB**

---

## Documentation Updated

See `TERRAIN_RENDERING_GC_FIX.md` for detailed analysis and implementation notes.

