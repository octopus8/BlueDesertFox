# TextureBlender Troubleshooting Guide

Common issues and solutions for the TextureBlender system.

## Quick Diagnostics

### Check These First

1. ✅ Is compute shader assigned in Inspector?
2. ✅ Are all texture arrays non-null and non-empty?
3. ✅ Is texture pooling enabled?
4. ✅ Is array caching enabled?
5. ✅ Are you calling `ReturnTexture()` when done?

---

## Common Issues

### Issue: "ImageProcessorShader is not assigned!"

**Symptoms:**
- Error in Console on Start
- Blends return null
- Component doesn't initialize

**Cause:** Missing compute shader reference

**Solutions:**

**Solution 1: Assign in Inspector**
1. Select TextureBlender GameObject
2. Find "Image Processor Shader" field
3. Assign `TextureBlenderComputeShader.compute` from Assets/LiquidForce/TextureBlender/

**Solution 2: Assign in Code**
```csharp
[SerializeField] private ComputeShader shader;

void Awake()
{
    var blenderComponent = GetComponent<TextureBlender>();
    // Note: imageProcessorShader is private, assign in Inspector instead
}
```

---

### Issue: Blend Returns Null

**Symptoms:**
- `BlendTextures()` returns null
- No texture appears on material

**Causes & Solutions:**

**Cause 1: No Textures Provided**
```csharp
// BAD
Texture[] textures = null;
var result = blender.BlendTextures(textures);  // Returns null

// GOOD
Texture[] textures = { tex1, tex2, tex3 };
var result = blender.BlendTextures(textures);
```

**Cause 2: All Textures Are Null**
```csharp
// BAD
Texture[] textures = { null, null, null };
var result = blender.BlendTextures(textures);  // Returns null

// GOOD - At least one valid texture
Texture[] textures = { tex1, null, tex3 };
var result = blender.BlendTextures(textures);  // Works, null replaced with black
```

**Cause 3: Compute Shader Missing**
- See "ImageProcessorShader is not assigned" above

---

### Issue: Slow Performance / Frame Drops

**Symptoms:**
- Blend takes >10ms
- VR stuttering
- Frame rate drops during blends

**Diagnostic Steps:**

**Step 1: Check Resolution**
```csharp
// Open Profiler → CPU → Expand frame
// Look for TextureBlender.Dispatch time

// If >5ms for desktop or >3ms for VR:
// Reduce resolution in Inspector:
Default Output Width: 1024   // Was 2048
Default Output Height: 1024  // Was 2048
```

**Step 2: Enable Optimizations**
```csharp
// In Inspector:
Use Texture Pooling: ✓
Fast Mode: ✓ (if inputs validated)

// Note: Texture2DArray caching is always enabled
```

**Step 3: Use Faster Blend Mode**
```csharp
// Change from AlphaWeighted to Additive
var result = blender.BlendTextures(
    textures,
    weights,
    TextureBlender.BlendMode.Additive);  // 30% faster
```

**Step 4: Check Profiler Markers**
```
TextureBlender.ConvertToArray > 2ms consistently?
→ Check if textures are changing (cache hit requires same instances)

TextureBlender.AllocateResources > 0.5ms?
→ Pool misses, increase Max Pooled Textures

TextureBlender.Dispatch > 5ms?
→ Resolution too high or too many textures
```

---

### Issue: Memory Leaks / Growing Memory Usage

**Symptoms:**
- Memory usage increases over time
- Eventually runs out of VRAM
- Performance degrades over minutes

**Cause:** Not returning textures to pool

**Solution:**
```csharp
// BAD - Leaks RenderTexture every call
void UpdateTexture()
{
    var result = blender.BlendTextures(textures);
    material.mainTexture = result;
    // Forgot to return!
}

// GOOD - Returns to pool
void UpdateTexture()
{
    // Return old texture first
    if (material.mainTexture is RenderTexture oldRT)
    {
        blender.ReturnTexture(oldRT);
    }
    
    var result = blender.BlendTextures(textures);
    material.mainTexture = result;
}

// BETTER - Reuse same texture
private RenderTexture persistentRT;

void Start()
{
    persistentRT = new RenderTexture(1024, 1024, 0);
    persistentRT.enableRandomWrite = true;
    persistentRT.Create();
}

void UpdateTexture()
{
    blender.BlendToExistingTexture(persistentRT, textures, weights);
    material.mainTexture = persistentRT;
}

void OnDestroy()
{
    persistentRT?.Release();
}
```

---

### Issue: Cache Not Working

**Symptoms:**
- First blend: 5ms
- Second blend: 5ms (should be ~2ms)
- `TextureBlender.ConvertToArray` always shows time

**Note:** Texture2DArray caching is always enabled automatically.

**Diagnostic:**
```csharp
// Verify textures aren't changing
// Cache uses texture instance IDs
// If textures recreated, cache misses
```

**Solution: Don't Recreate Textures**
```csharp
// BAD - Creates new texture each frame
void Update()
{
    Texture2D newTex = new Texture2D(512, 512);
    // ... fill texture
    textures[0] = newTex;  // New instance ID = cache miss
    blender.BlendTextures(textures);
}

// GOOD - Reuse same texture
Texture2D persistentTex;

void Start()
{
    persistentTex = new Texture2D(512, 512);
    textures[0] = persistentTex;
}

void Update()
{
    // Modify pixels, don't recreate
    persistentTex.SetPixels(...);
    persistentTex.Apply();
    
    // Note: Cache uses texture instance IDs, so modifying pixels
    // of the same texture instance won't change the hash.
    // Modified textures will use cached array (still valid).
    blender.BlendTextures(textures);
}
```

**Note:** Hash is based on texture instance IDs, not pixel content. If you modify a texture's pixels but keep the same Texture2D instance, the cache will still use the same Texture2DArray (which is fine - the pixel data is in the original textures, not the array).

---

### Issue: Incorrect Colors / Washed Out Results

**Symptoms:**
- Colors look wrong
- Too bright or too dark
- Different from expected blend

**Causes & Solutions:**

**Cause 1: Wrong Blend Mode**
```csharp
// Additive mode brightens
var result = blender.BlendTextures(
    textures,
    weights,
    BlendMode.Additive);  // Can exceed 1.0

// Try AlphaWeighted for natural blending
var result = blender.BlendTextures(
    textures,
    weights,
    BlendMode.AlphaWeighted);
```

**Cause 2: Weights Sum > 1.0**
```csharp
// Weights are normalized in most modes
// But Additive mode can over-brighten

float[] weights = { 1.0f, 1.0f, 1.0f };  // Sum = 3.0
// In Additive mode, result will be 3x brighter

// Solution: Normalize weights manually
float sum = 0;
foreach (var w in weights) sum += w;
for (int i = 0; i < weights.Length; i++)
{
    weights[i] /= sum;
}
```

**Cause 3: Linear/sRGB Mismatch**
```csharp
// Texture array conversion handles this automatically
// But check material color space settings

// In material:
// Use Linear for PBR materials
// Use sRGB for UI/sprites
```

---

### Issue: VR Rendering Artifacts

**Symptoms:**
- Texture looks wrong in one eye
- Flickering in VR
- "OpenGL ES 3.0 not supported" errors

**Cause:** Compute shader compatibility

**Solution:**
```csharp
// System already writes to both:
// - RWTexture2D (modern)
// - RWStructuredBuffer (OpenGL ES 3.0)

// Ensure VR project settings:
// Graphics API: OpenGL ES 3.0 or higher
// Stereo Rendering Mode: Multi-pass or Single-pass
```

---

### Issue: "Kernel not found" Error

**Symptoms:**
- Error: "Kernel 'BlendTexturesArrayAdditive' not found"
- Blend operations fail

**Cause:** Wrong compute shader assigned

**Solution:**
```csharp
// Ensure you're using the correct compute shader:
// TextureBlenderComputeShader.compute

// NOT the old one:
// ImageProcessorEnhanced.compute (deprecated)
// TextureProcessor - From SimX.compute (legacy)
```

---

### Issue: Normal Maps Look Flat

**Symptoms:**
- Blended normal map has no detail
- Surface appears smooth when it shouldn't

**Causes & Solutions:**

**Cause 1: Using Wrong Blend Method**
```csharp
// BAD - Regular blend flattens normals
var normals = blender.BlendTextures(normalTextures);

// GOOD - Use specialized normal blend
var normals = blender.BlendNormalsWithBaseAlpha(
    normalTextures,
    baseTextures,  // Alpha masks
    weights);
```

**Cause 2: Missing Alpha Masks**
```csharp
// Base textures need alpha channels
// Alpha = 0: No contribution
// Alpha = 1: Full contribution

// Check base texture import settings:
// Alpha Source: Input Texture Alpha
// Alpha Is Transparency: ✓
```

---

### Issue: Async Blend Never Completes

**Symptoms:**
- `BlendTexturesAsync()` never returns
- UI hangs waiting for result

**Cause:** Cancellation token cancelled or invalid

**Solution:**
```csharp
// BAD
CancellationTokenSource cts = new CancellationTokenSource();
cts.Cancel();  // Cancelled immediately!
var result = await blender.BlendTexturesAsync(
    textures, null, BlendMode.AlphaWeighted, cts.Token);

// GOOD
CancellationTokenSource cts = new CancellationTokenSource();
try
{
    var result = await blender.BlendTexturesAsync(
        textures, 
        null, 
        BlendMode.AlphaWeighted, 
        cts.Token);
    // Use result
}
catch (OperationCanceledException)
{
    Debug.Log("Blend cancelled");
}
finally
{
    cts.Dispose();
}

// BETTER - Use component lifetime token
var result = await blender.BlendTexturesAsync(
    textures,
    null,
    BlendMode.AlphaWeighted,
    this.GetCancellationTokenOnDestroy());
```

---

### Issue: Batch Blend Not Faster

**Symptoms:**
- `BatchBlend()` same speed as sequential blends
- Expected speedup not achieved

**Explanation:**
- BatchBlend is sequential on GPU
- Benefits appear with large batches (>10)
- GPU may already be fully utilized

**When to use:**
```csharp
// Good for batch blend:
// - Many small blends (>10)
// - Processing entire asset batch
// - Initial scene setup

// Not helpful:
// - 2-3 blends
// - Real-time updates
// - Single blend per frame
```

---

## Debugging Tools

### Enable Profiler Markers

```csharp
// In Unity Profiler:
// Window → Analysis → Profiler
// CPU Usage → Select frame
// Expand hierarchy to find:
//   - TextureBlender.ConvertToArray
//   - TextureBlender.Dispatch
//   - TextureBlender.AllocateResources
//   - TextureBlender.CacheCheck
```

### Performance Test Component

```csharp
[ContextMenu("Test Performance")]
void TestPerformance()
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    
    // First blend (uncached)
    var result1 = blender.BlendTextures(textures);
    var firstTime = watch.ElapsedMilliseconds;
    blender.ReturnTexture(result1);
    
    // Second blend (cached)
    watch.Restart();
    var result2 = blender.BlendTextures(textures);
    var secondTime = watch.ElapsedMilliseconds;
    blender.ReturnTexture(result2);
    
    Debug.Log($"First: {firstTime}ms, Cached: {secondTime}ms");
    Debug.Log($"Speedup: {firstTime / (float)secondTime:F2}x");
    
    // Verify caching working
    if (secondTime >= firstTime * 0.8f)
    {
        Debug.LogWarning("Cache may not be working!");
    }
}
```

### Validation Script

```csharp
public class TextureBlenderValidator : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    
    [ContextMenu("Validate Setup")]
    void ValidateSetup()
    {
        Debug.Log("=== TextureBlender Validation ===");
        
        // Check compute shader
        var shader = blender.GetComponent<TextureBlender>();
        // Note: imageProcessorShader is private field
        Debug.Log("✓ Component exists");
        
        // Check settings
        Debug.Log($"Pooling: {blender.useTexturePooling}");  // Private
        
        // Recommend Inspector check instead
        Debug.Log("Check Inspector for:");
        Debug.Log("  - Image Processor Shader assigned");
        Debug.Log("  - Use Texture Pooling enabled");
    }
}
```

---

## Platform-Specific Issues

### Quest 2 Standalone

**Issue: Out of memory**
**Solution:** Reduce resolution to 512×512 or 1024×1024

**Issue: Slow blends**
**Solution:** 
- Use Additive mode
- Enable Fast Mode
- Reduce max pooled textures to 3

### PCVR

**Issue: Stuttering**
**Solution:**
- Use async blending
- Enable texture pooling
- Spread blends across frames

### WebGL

**Issue: Not supported**
**Note:** TextureBlender uses compute shaders which are not supported in WebGL

---

## Getting Help

### Information to Provide

When reporting issues, include:

1. **Unity Version**: e.g., Unity 6 (6000.3.10f1)
2. **Platform**: Desktop/Quest2/PCVR/etc.
3. **Texture Count**: How many textures blending
4. **Resolution**: Input and output texture sizes
5. **Blend Mode**: Additive/AlphaWeighted/Multiplicative
6. **Settings**: Pooling/Caching/FastMode status
7. **Console Errors**: Copy full error messages
8. **Profiler Screenshot**: Show marker timings

### Minimal Reproduction

Create simplest possible reproduction:

```csharp
public class MinimalRepro : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture tex1;
    [SerializeField] private Texture tex2;
    
    void Start()
    {
        // Simplest possible case showing issue
        Texture[] textures = { tex1, tex2 };
        var result = blender.BlendTextures(textures);
        
        if (result == null)
        {
            Debug.LogError("Blend returned null!");
        }
        else
        {
            Debug.Log($"Blend succeeded: {result.width}x{result.height}");
        }
    }
}
```

---

## Checklist Before Reporting Issue

- [ ] Compute shader assigned in Inspector
- [ ] Using correct shader (TextureBlenderComputeShader.compute)
- [ ] At least one non-null texture in array
- [ ] Texture pooling enabled
- [ ] Array caching enabled
- [ ] Calling ReturnTexture() when done
- [ ] Checked Console for error messages
- [ ] Checked Unity Profiler markers
- [ ] Tried minimal reproduction
- [ ] Updated to latest version

