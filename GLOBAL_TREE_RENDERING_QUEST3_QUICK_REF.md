# Quest 3 VR Tree Rendering - Quick Reference

**Last Updated**: May 2, 2026  
**System**: GlobalTreeInstanceSystem v2.0

---

## Performance Tuning Parameters

### Distance Culling (GlobalTreeInstanceSystem.cs)

```csharp
private const float DefaultMaxRenderDistance = 400f; // Line ~208
```

**Quest 3 Recommendations**:
- **Indoor scenes**: 250-300m
- **Outdoor scenes**: 400-500m  
- **Performance mode**: 300m
- **Quality mode**: 500m

**Runtime Modification**:
```csharp
// Access the private field if needed via reflection
// Or expose as public configuration in TreeSpawnerConfig
```

---

### LOD Update Frequency (TreeLODUpdateSystem.cs)

```csharp
private const int VRFrameSkip = 2; // Line ~26
```

**Quest 3 Recommendations**:
- **72Hz mode**: `VRFrameSkip = 2` (update every 2 frames)
- **90Hz mode**: `VRFrameSkip = 3` (update every 3 frames)
- **120Hz mode**: `VRFrameSkip = 4` (update every 4 frames)

---

## Expected Performance Metrics

### Quest 3 @ 72Hz with 2000 Trees

| Metric | Before v2.0 | After v2.0 | Improvement |
|--------|-------------|------------|-------------|
| **CPU Time** | 8-15ms | 2-5ms | **~10ms** ⬇️ |
| **GC Alloc** | 2-5 KB/frame | 0 KB/frame | **100%** ⬇️ |
| **Draw Calls** | 15-20 | 12-15 | **~20%** ⬇️ |
| **Trees Rendered** | 2000 | 1800-1900* | *Distance culled |

*Actual rendered trees depend on camera frustum and distance culling

---

## Quick Profiler Check

### Unity Profiler Markers to Monitor

1. **`GlobalTreeInstance.Render`** (Total)
   - **Target**: <5ms on Quest 3
   - **Warning**: >8ms = consider reducing tree count or render distance

2. **`GlobalTreeInstance.Collect`** (Matrix collection)
   - **Target**: <2ms
   - **Warning**: >3ms = frustum/distance culling not reducing load

3. **`GlobalTreeInstance.Convert`** (Batch conversion)
   - **Target**: <1ms
   - **Warning**: >2ms = too many unique mesh/material combinations

4. **`GlobalTreeInstance.Draw`** (Graphics API)
   - **Target**: <2ms CPU (GPU depends on complexity)
   - **Warning**: >3ms = too many draw calls (>20)

---

## Debug Logging

### Enable Logging

Set in `TreeSpawnerConfigAuthoring` or via code:
```csharp
var config = SystemAPI.GetSingleton<TreeLODConfig>();
config.enableTreeLODDebug = true; // Enable debug logging
```

### Sample Output

```
[GlobalTreeInstance] Rendered 1843/2000 trees in 12 draw calls 
(3 unique batches, max distance: 400m)
```

**Interpretation**:
- **1843/2000**: 157 trees culled (distance + frustum)
- **12 draw calls**: Batch splitting due to 1023 instance limit
- **3 unique batches**: 3 different mesh/material combos

---

## Common Issues & Solutions

### Issue: Still seeing high CPU time (>8ms)

**Solutions**:
1. ✅ Reduce `MaxRenderDistance` to 300m
2. ✅ Increase `VRFrameSkip` to 3 (update every 3rd frame)
3. ✅ Check if too many tree types → reduce unique meshes/materials
4. ✅ Profile `Collect` vs `Convert` → identify bottleneck

### Issue: Trees popping in/out abruptly

**Solutions**:
1. ✅ Increase `MaxRenderDistance` (trade performance for quality)
2. ✅ Add fog to hide culling transition
3. ✅ Adjust LOD distances in `TreeLODConfig`:
   - `lod0Distance`: 50m → 75m
   - `lod1Distance`: 150m → 200m
   - `lod2Distance`: 300m → 400m

### Issue: GC allocations still appearing

**Solutions**:
1. ❌ Check for managed component access in jobs
2. ❌ Verify all collections use `Allocator.Persistent`
3. ❌ Ensure no `ToArray()` or `ToList()` calls in hot path
4. ✅ Disable debug logging (string allocations)

### Issue: Too many draw calls (>20)

**Solutions**:
1. ✅ Reduce tree type variety (fewer unique meshes/materials)
2. ✅ Use material atlasing to combine materials
3. ✅ Ensure trees share materials via `GlobalTreeRenderingData`

---

## Runtime Configuration Access

### Modify Max Render Distance

```csharp
// In GlobalTreeInstanceSystem (add public setter if needed)
public partial class GlobalTreeInstanceSystem : SystemBase
{
    public void SetMaxRenderDistance(float distance)
    {
        _maxRenderDistance = Mathf.Clamp(distance, 100f, 1000f);
        Debug.Log($"[GlobalTreeInstance] Max render distance set to {_maxRenderDistance}m");
    }
}
```

### Modify LOD Update Frequency

```csharp
// Add to TreeLODConfig component
public struct TreeLODConfig : IComponentData
{
    // ...existing fields...
    public int vrFrameSkip; // NEW: Runtime configurable
}

// In TreeLODUpdateSystem.OnUpdate()
var lodConfig = SystemAPI.GetSingleton<TreeLODConfig>();
if (_frameCounter % lodConfig.vrFrameSkip != 0)
    return;
```

---

## Asset Optimization Tips

### Tree Mesh Guidelines

- **LOD0** (0-50m): 500-1000 vertices
- **LOD1** (50-150m): 200-400 vertices
- **LOD2** (150m+): 50-100 vertices

### Material Guidelines

- Use **mobile-optimized shaders** (URP/Lit or Simple Lit)
- Limit to **2-3 material variations** per tree type
- Use **texture atlases** where possible
- Disable shadows for LOD2 trees

### Batching Guidelines

- Group trees by material to maximize batching
- Aim for **<5 unique mesh/material combinations** total
- Use instancing-friendly shaders (avoid per-instance properties)

---

## Performance Quality Presets

### **Performance Mode** (Quest 3 @ 72Hz, >2000 trees)
```csharp
MaxRenderDistance = 300f;
VRFrameSkip = 3;
lod0Distance = 40f;
lod1Distance = 120f;
lod2Distance = 300f;
```

### **Balanced Mode** (Quest 3 @ 72Hz, 1000-2000 trees)
```csharp
MaxRenderDistance = 400f;
VRFrameSkip = 2;
lod0Distance = 50f;
lod1Distance = 150f;
lod2Distance = 400f;
```

### **Quality Mode** (Quest 3 @ 72Hz, <1000 trees)
```csharp
MaxRenderDistance = 500f;
VRFrameSkip = 1;
lod0Distance = 75f;
lod1Distance = 200f;
lod2Distance = 500f;
```

---

## Version Notes

**v2.0 Changes**:
- ✅ Native-only batching (no GC)
- ✅ Distance culling before frustum
- ✅ LOD update throttling
- ✅ Optimized matrix operations

**Compatibility**: Drop-in replacement for v1.0, no scene changes required

---

## Quick Commands

### Test Performance on Quest 3

1. Build and deploy to Quest 3
2. Enable Profiler in Build Settings
3. Connect via USB → Unity Profiler
4. Monitor `GlobalTreeInstance.*` markers
5. Adjust parameters based on metrics above

### Reset to Defaults

```csharp
// GlobalTreeInstanceSystem
_maxRenderDistance = 400f;

// TreeLODUpdateSystem
VRFrameSkip = 2;
```

---

## Contact & Support

- **Documentation**: See `GLOBAL_TREE_RENDERING_OPTIMIZATION.md`
- **Architecture**: See `Assets/_App/Ace of Ages/Terrain/TREE_SPAWNING_SYSTEM.md`
- **General Guide**: See `AGENTS.md`

