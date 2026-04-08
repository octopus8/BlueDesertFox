# TextureBlender Performance Guide

Optimization strategies and profiling techniques for maximum performance.

## Performance Targets

### Desktop (RTX 3070)

| Configuration      | Resolution  | Target Time | Cached Time |
|-------------------|-------------|-------------|-------------|
| 4 textures        | 1024×1024   | <2ms        | <1ms        |
| 4 textures        | 2048×2048   | <5ms        | <2ms        |
| 8 textures        | 2048×2048   | <8ms        | <3ms        |
| 16 textures       | 2048×2048   | <10ms       | <4ms        |

### VR Targets

| Platform    | Resolution  | Target Time | Notes                    |
|-------------|-------------|-------------|--------------------------|
| Quest 2     | 1024×1024   | <3ms        | Enable Fast Mode         |
| Quest 2     | 2048×2048   | <8ms        | Use with caution         |
| Quest Pro   | 1024×1024   | <2ms        | Standard settings OK     |
| PCVR        | 1024×1024   | <2ms        | Can use higher res       |
| PCVR        | 2048×2048   | <5ms        | Desktop-class GPU        |

## Optimization Checklist

### Essential Optimizations (Always Do)

- ✅ **Enable Texture Pooling** - Saves 0.5-1ms per blend
- ✅ **Set Appropriate Resolution** - Use 1024×1024 for VR
- ✅ **Call ReturnTexture()** - Prevents memory leaks

**Note:** Texture2DArray caching is always enabled (saves 1-2ms on repeat blends)

### High-Impact Optimizations

- ✅ **Use Additive Mode** - 30% faster than AlphaWeighted when possible
- ✅ **Reuse RenderTextures** - Use `BlendToExistingTexture()` for updates
- ✅ **Batch Similar Operations** - Use `BatchBlend()` for multiple blends
- ✅ **Prewarm Pools** - Eliminate first-frame allocation costs
- ✅ **Avoid Rotation/Offset When Possible** - Zero-overhead when null or all zeros (98% faster with cached arrays)

### Advanced Optimizations

- ⚙️ **Enable Fast Mode** - Skip validation (only if inputs verified)
- ⚙️ **Reduce Max Pooled Textures** - Lower memory footprint
- ⚙️ **Use Uniform Texture Sizes** - 50% faster array conversion

## Configuration for Different Scenarios

### Scenario 1: VR Real-Time Terrain

**Requirements:** <3ms per frame, 90+ FPS

**Configuration:**
```csharp
// Inspector settings
Default Output Width: 1024
Default Output Height: 1024
Use Texture Pooling: ✓
Max Pooled Textures: 3
Fast Mode: ✓

// Code
RenderTexture terrain = blender.BlendTextures(
    terrainLayers,
    splatWeights,
    TextureBlender.BlendMode.Additive);  // Use Additive for speed
```

**Expected Performance:** 1-2ms per blend

---

### Scenario 2: Desktop High-Quality Scene

**Requirements:** <10ms budget, visual quality priority

**Configuration:**
```csharp
// Inspector settings
Default Output Width: 2048
Default Output Height: 2048
Use Texture Pooling: ✓
Max Pooled Textures: 5
Fast Mode: ☐

// Code
RenderTexture result = blender.BlendTextures(
    textures,
    weights,
    TextureBlender.BlendMode.AlphaWeighted);  // Quality mode
```

**Expected Performance:** 3-5ms per blend

---

### Scenario 3: Loading Screen (Async)

**Requirements:** Non-blocking, quality important

**Configuration:**
```csharp
// Inspector settings
Default Output Width: 2048
Default Output Height: 2048
Use Texture Pooling: ✓
Max Pooled Textures: 10
Fast Mode: ☐

// Code
RenderTexture result = await blender.BlendTexturesAsync(
    textures,
    weights,
    TextureBlender.BlendMode.AlphaWeighted,
    cancellationToken);
```

**Expected Performance:** Background execution, no frame impact

---

### Scenario 4: Batch Processing

**Requirements:** Process multiple texture sets efficiently

**Configuration:**
```csharp
// Inspector settings
Default Output Width: 1024
Default Output Height: 1024
Use Texture Pooling: ✓
Max Pooled Textures: 20
Fast Mode: ✓

// Code
var requests = CreateBatchRequests();  // Multiple blend ops
RenderTexture[] results = blender.BatchBlend(requests);
```

**Expected Performance:** ~80% of sequential time (parallel GPU usage)

---

## Profiling

### Unity Profiler Markers

Enable Deep Profiling and look for these markers:

1. **TextureBlender.ConvertToArray**
   - Time spent converting textures to Texture2DArray
   - Should be <1ms with caching
   - High values indicate cache misses

2. **TextureBlender.Dispatch**
   - GPU compute shader execution time
   - Core blend operation performance
   - Target: <2ms for 4×2048² textures

3. **TextureBlender.AllocateResources**
   - Resource pool allocation overhead
   - Should be near 0ms with pooling
   - High values indicate pool misses

4. **TextureBlender.CacheCheck**
   - Time spent checking texture array cache
   - Should be <0.1ms
   - Negligible overhead

### Analyzing Profiler Data

**Good Performance Pattern:**
```
Frame 1 (cold):
  ConvertToArray: 1.2ms
  Dispatch: 2.8ms
  AllocateResources: 0.5ms
  Total: 4.5ms

Frame 2+ (warm):
  ConvertToArray: 0.0ms (cached)
  Dispatch: 2.8ms
  AllocateResources: 0.0ms (pooled)
  Total: 2.8ms
```

**Problem Indicators:**
- ConvertToArray >2ms every frame → Cache not working
- Dispatch >5ms → Resolution too high or too many textures
- AllocateResources >0.5ms → Pooling disabled or pool too small

---

## Memory Optimization

### Texture Pool Sizing

**Formula:**
```
Pool Size = Concurrent Blends × Safety Factor
```

**Examples:**
- Single terrain blend: `Pool Size = 1 × 2 = 2`
- Multiple UI elements: `Pool Size = 5 × 1.5 = 8`
- Batch processing: `Pool Size = Batch Count × 1.2`

**Memory Usage:**
```
Memory per RT = Width × Height × 4 bytes (ARGB32)

Example (2048×2048):
= 2048 × 2048 × 4
= 16,777,216 bytes
= ~16 MB

Pool of 5 = 80 MB
Pool of 10 = 160 MB
```

### Cache Management

**When to Clear Cache:**
```csharp
// After modifying textures
texture.SetPixels(newColors);
texture.Apply();
blender.ClearCache();  // Force rebuild on next blend

// Before major scene transition
void OnSceneUnload()
{
    blender.ClearCache();  // Free memory
}

// When memory critical
if (Application.lowMemory)
{
    blender.ClearCache();
}
```

**Cache Size Estimation:**
```
Texture2DArray Size = Width × Height × Texture Count × 4 bytes

Example (4×2048² textures):
= 2048 × 2048 × 4 × 4
= 67,108,864 bytes
= ~64 MB per cached set
```

---

## Common Performance Issues

### Issue 1: First Blend is Slow

**Symptoms:**
- First blend: 5ms
- Subsequent blends: 2ms

**Cause:** Texture array conversion not cached

**Solution:**
```csharp
void Start()
{
    // Prewarm by blending once during loading
    var warmup = blender.BlendTextures(textures);
    blender.ReturnTexture(warmup);
    
    // Future blends will use cached array
}
```

---

### Issue 2: Performance Degrades Over Time

**Symptoms:**
- Blend time increases gradually
- Memory usage grows

**Cause:** Not returning textures to pool

**Solution:**
```csharp
// BAD
void UpdateTerrain()
{
    RenderTexture result = blender.BlendTextures(textures);
    // Leaks RenderTexture every frame!
}

// GOOD
void UpdateTerrain()
{
    RenderTexture result = blender.BlendTextures(textures);
    ApplyToMaterial(result);
    blender.ReturnTexture(result);  // Return to pool
}
```

---

### Issue 3: Stuttering in VR

**Symptoms:**
- Frame drops during blends
- Inconsistent frame times

**Cause:** Blend operation too expensive for VR

**Solutions:**

**Option A: Lower Resolution**
```csharp
// Change from 2048 to 1024
Default Output Width: 1024
Default Output Height: 1024
```

**Option B: Use Async**
```csharp
// Spread work across frames
private async void UpdateTerrain()
{
    var result = await blender.BlendTexturesAsync(
        textures,
        weights,
        BlendMode.Additive,  // Also use faster mode
        cancellationToken);
    
    ApplyToMaterial(result);
}
```

**Option C: Amortize Updates**
```csharp
// Update every N frames
private int frameCounter = 0;
private const int updateInterval = 3;

void Update()
{
    frameCounter++;
    if (frameCounter % updateInterval == 0)
    {
        UpdateTerrainBlend();
    }
}
```

---

### Issue 4: Batch Blend No Faster Than Sequential

**Symptoms:**
- BatchBlend() same speed as sequential blends

**Cause:** GPU already fully utilized

**Solution:**
- Expected for small batches (<5)
- Benefits appear with larger batches (>10)
- Consider reducing resolution instead

---

## Extreme Optimization Techniques

### 1. Persistent RenderTextures

**Standard approach:**
```csharp
void Update()
{
    var result = blender.BlendTextures(null, textures, weights);
    material.mainTexture = result;
    blender.ReturnTexture(result);
}
```

**Optimized approach:**
```csharp
private RenderTexture persistentRT;

void Start()
{
    persistentRT = new RenderTexture(1024, 1024, 0);
    persistentRT.enableRandomWrite = true;
    persistentRT.Create();
}

void Update()
{
    blender.BlendTextures(persistentRT, textures, weights);
    material.mainTexture = persistentRT;
    // No allocation, no return needed
}
```

**Speedup:** ~20-30% faster

---

### 2. Uniform Texture Sizes

**Problem:**
```csharp
textures[0] = 2048×2048
textures[1] = 1024×1024  // Size mismatch!
textures[2] = 2048×2048
```

**Solution:**
```csharp
// Ensure all textures are same size
textures[0] = 2048×2048
textures[1] = 2048×2048  // Resized
textures[2] = 2048×2048

// Or use smaller common size
textures[0] = 1024×1024
textures[1] = 1024×1024
textures[2] = 1024×1024
```

**Speedup:** 50% faster array conversion

---

### 3. Weight Caching

**Standard approach:**
```csharp
void Update()
{
    float[] weights = CalculateWeights();  // Allocation!
    blender.BlendTextures(textures, weights);
}
```

**Optimized approach:**
```csharp
private float[] cachedWeights;

void Start()
{
    cachedWeights = new float[textureCount];
}

void Update()
{
    UpdateWeights(cachedWeights);  // Reuse array
    blender.BlendTextures(textures, cachedWeights);
}
```

**Benefit:** Zero GC allocations

---

### 4. Fast Mode Validation

**Enable Fast Mode only after validation:**
```csharp
void Start()
{
    // Validate inputs once
    if (ValidateTextures(textures))
    {
        // Enable fast mode for runtime
        blender.fastMode = true;  // Note: This is a private field
        // You'll need to enable in Inspector instead
    }
}
```

**Speedup:** ~5-10% from skipping validation

---

## Benchmarking

### Performance Test Script

```csharp
[ContextMenu("Benchmark")]
void RunBenchmark()
{
    const int iterations = 100;
    
    // Warmup
    for (int i = 0; i < 10; i++)
    {
        var warmup = blender.BlendTextures(textures);
        blender.ReturnTexture(warmup);
    }
    
    // Measure
    var startTime = Time.realtimeSinceStartup;
    
    for (int i = 0; i < iterations; i++)
    {
        var result = blender.BlendTextures(textures, weights, mode);
        blender.ReturnTexture(result);
    }
    
    var totalTime = (Time.realtimeSinceStartup - startTime) * 1000f;
    var avgTime = totalTime / iterations;
    
    Debug.Log($"Average blend time: {avgTime:F3}ms over {iterations} iterations");
    Debug.Log($"Textures: {textures.Length}, Resolution: {textures[0].width}×{textures[0].height}");
    Debug.Log($"Mode: {mode}, Pooling: {usePooling}");
}
```

---

## Quick Reference: Optimization Priorities

### Priority 1: Must Do
1. Enable texture pooling
2. Return textures to pool
3. Use appropriate resolution

**Note:** Texture2DArray caching is always enabled automatically

### Priority 2: High Impact
4. Use Additive mode when possible
5. Reuse RenderTextures
6. Prewarm pools

### Priority 3: Fine Tuning
7. Uniform texture sizes
8. Batch operations
9. Enable Fast Mode

### Priority 4: Extreme Cases
10. Persistent RenderTextures
11. Weight array caching
12. Frame amortization
13. Async operations

---

## Platform-Specific Recommendations

### Quest 2 (Standalone)
- Resolution: 1024×1024 max
- Mode: Additive preferred
- Fast Mode: ✓
- Pool Size: 3-5
- Target: <3ms

### Quest Pro (Standalone)
- Resolution: 1024×1024 or 2048×2048
- Mode: Any
- Fast Mode: Optional
- Pool Size: 5-8
- Target: <2ms

### PCVR (High-End)
- Resolution: 2048×2048
- Mode: Any
- Fast Mode: Optional
- Pool Size: 5-10
- Target: <5ms

### Desktop (Non-VR)
- Resolution: 2048×2048 or 4096×4096
- Mode: AlphaWeighted for quality
- Fast Mode: Optional
- Pool Size: 10-20
- Target: <10ms acceptable

