# TextureBlender Architecture

Internal design and implementation details of the TextureBlender system.

## System Overview

The TextureBlender system consists of three main components working together:

```
┌─────────────────┐
│ TextureBlender  │ ← Public API, main component
│  MonoBehaviour  │
└────────┬────────┘
         │
         ├─────────────────────────────────────┐
         │                                     │
┌────────▼────────────┐              ┌────────▼──────────────┐
│ TextureBlender      │              │ TextureArrayBuilder   │
│ Resources           │              │ (Static Utility)      │
│                     │              │                       │
│ - RenderTexture     │              │ - Texture2D[] →       │
│   Pooling           │              │   Texture2DArray      │
│ - ComputeBuffer     │              │ - Hash computation    │
│   Pooling           │              │ - Size normalization  │
│ - Texture2DArray    │              │                       │
│   Tracking          │              │                       │
└─────────────────────┘              └───────────────────────┘
         │
         │ Uses
         ▼
┌─────────────────────┐
│ Compute Shader      │
│ (GPU Side)          │
│                     │
│ - BlendAdditive     │
│ - BlendAlphaWeighted│
│ - BlendMultiplicative│
│ - BlendNormalsWithBaseAlpha │
└─────────────────────┘
```

## Component Breakdown

### TextureBlender (Main Component)

**Responsibilities:**
1. Public API for blend operations
2. Resource lifecycle management
3. Compute shader parameter setup
4. Cache management
5. Input validation

**Key Fields:**
```csharp
// Serialized configuration
private ComputeShader imageProcessorShader;
private int defaultOutputWidth = 2048;
private int defaultOutputHeight = 2048;
private RenderTextureFormat outputFormat = ARGB32;
private bool useTexturePooling = true;
private int maxPooledTextures = 5;
private bool enableArrayCache = true;
private bool fastMode = false;

// Runtime state
private TextureBlenderResources resources;
private bool isInitialized = false;
private Dictionary<int, Texture2DArray> textureArrayCache;

// Cached kernel IDs
private int kernelBlendArrayAdditive;
private int kernelBlendArrayAlphaWeighted;
private int kernelBlendArrayMultiplicative;
// ... etc
```

**Initialization Flow:**
```
Awake()
  └─> Initialize()
       ├─> Validate compute shader
       ├─> Cache kernel IDs
       ├─> Create TextureBlenderResources
       ├─> Prewarm pools (VR sizes)
       └─> Initialize cache dictionary
```

### TextureBlenderResources

**Responsibilities:**
1. RenderTexture pooling
2. ComputeBuffer pooling
3. Texture2DArray tracking
4. Resource cleanup

**Pool Structure:**
```csharp
// RenderTexture pool keyed by (width, height, format)
Dictionary<(int, int, RenderTextureFormat), Queue<RenderTexture>> renderTexturePool;

// ComputeBuffer pool keyed by element count
Dictionary<int, Queue<ComputeBuffer>> bufferPool;

// Tracked arrays for cleanup
List<Texture2DArray> tempArrays;
```

**Pool Behavior:**
- FIFO queue for each size/format
- Max size limit prevents unbounded growth
- Items beyond limit are released immediately
- Validation on retrieval (IsCreated/IsValid checks)

### TextureArrayBuilder

**Responsibilities:**
1. Convert Texture[] to Texture2DArray
2. Handle size mismatches
3. Replace null textures with black
4. Compute cache keys

**Optimization Paths:**
```
BuildFromTextures()
  ├─> Check all same size?
  │    ├─> YES → BuildFromUniformTexturesFast()
  │    │         └─> Graphics.CopyTexture (GPU-side)
  │    │
  │    └─> NO → Mixed size handling
  │              └─> Graphics.Blit + scale
  │
  └─> Create Texture2DArray
       └─> Apply (upload to GPU)
```

**Color Space Handling:**
```csharp
// Texture2DArray created with linear=false
// Indicates sRGB color space
// Unity performs sRGB→Linear on sample

Texture2DArray array = new Texture2DArray(
    width, height, count,
    format, mipChain,
    linear: false);  // sRGB space
```

### Developer Notes
**How is `TextureBlenderResources` used?**
- It is used by methods in `TextureBlender` like `BlendTextures`, `BlendNormalsWithBaseAlpha`, and related methods.
- Even when not blending to an existing texture, an output texture is created (`GetOrCreateRenderTexture`) and `BlendToExistingTexture` is called, which results in a call to `ExecuteBlend`.
- Callstack
```
- TextureBlenderResources.GetOrCreateRenderTexture (RenderTextures; a start texture)
- BlendToExistingTexture
    - TextureBlenderResources.GetOrCreateTextureArray (tracked texture arrays; array of textures to blend)
    - ExecuteBlend
        - TextureBlenderResources.GetOrCreateBuffer (ComputeBuffer; weight, offset, rotation)
        - Dispatch
```

## Data Flow

### Standard Blend Operation

```
1. User calls BlendTextures(textures, weights, mode)
   │
   ├─> 2. Validate inputs (unless fastMode)
   │
   ├─> 3. Get/Create RenderTexture from pool
   │
   ├─> 4. Normalize weights based on mode
   │
   ├─> 5. Convert to Texture2DArray
   │      ├─> Check cache (hash lookup)
   │      ├─> Hit? → Return cached
   │      └─> Miss? → Build new + cache
   │
   ├─> 6. Get/Create ComputeBuffers from pool
   │
   ├─> 7. Set compute shader parameters
   │      ├─> Bind Texture2DArray
   │      ├─> Bind output RenderTexture
   │      ├─> Bind output buffer (VR compat)
   │      └─> Set weights buffer
   │
   ├─> 8. Dispatch compute shader
   │      └─> GPU executes blend kernel
   │
   ├─> 9. Return buffers to pool
   │
   └─> 10. Return RenderTexture to user
```

### Caching Strategy

**Texture Array Cache:**
```csharp
// Cache key = hash of texture instance IDs
int hash = ComputeTextureArrayHash(textures);

// Cache lookup
if (textureArrayCache.ContainsKey(hash))
{
    return cachedArray;  // ~0ms
}

// Cache miss - build and store
Texture2DArray newArray = BuildFromTextures(textures);  // ~1-2ms
textureArrayCache[hash] = newArray;
return newArray;
```

**Cache Invalidation:**
- Manual: `ClearCache()` called by user
- Automatic: Component destroyed
- Texture instance ID changes trigger natural miss

## Compute Shader Architecture

### Kernel Structure

Each kernel follows this pattern:

```hlsl
// Inputs (set from C#)
Texture2DArray<float4> InputTexturesArray;
StructuredBuffer<float> BlendValues;
int TextureCount;
int TextureWidth;
int TextureHeight;

// Outputs (dual write for compatibility)
RWTexture2D<float4> OutputTexture;          // Modern GPUs
RWStructuredBuffer<float4> OutputBuffer;    // OpenGL ES 3.0

[numthreads(8,8,1)]
void BlendKernel(uint3 id : SV_DispatchThreadID)
{
    // Bounds check
    if (id.x >= TextureWidth || id.y >= TextureHeight)
        return;
    
    // Blend logic here
    float4 result = PerformBlend(id.xy);
    
    // Write to both outputs
    OutputTexture[id.xy] = result;
    OutputBuffer[id.y * TextureWidth + id.x] = result;
}
```

### Thread Group Size

**Choice: [numthreads(8,8,1)]**

Rationale:
- 64 threads per group = 2 warps on NVIDIA
- Optimal for RTX series GPUs
- Good occupancy vs register pressure balance
- Processes 8×8 pixel blocks efficiently

**Dispatch Calculation:**
```csharp
int dispatchX = Mathf.CeilToInt(width / 8f);
int dispatchY = Mathf.CeilToInt(height / 8f);

// Example: 2048×2048 texture
// dispatchX = 256 (2048/8)
// dispatchY = 256
// Total thread groups = 65,536
// Total threads = 4,194,304 (one per pixel)
```

### VR Compatibility

**Dual Output Pattern:**
```hlsl
// Modern approach (preferred)
OutputTexture[id.xy] = result;

// Fallback for OpenGL ES 3.0 (Quest)
OutputBuffer[id.y * TextureWidth + id.x] = result;
```

Why both?
- RWTexture2D not supported on all mobile GPUs
- RWStructuredBuffer universally supported
- Minimal overhead (same data, two writes)

### Texture Sampling and Tiling

**Custom Sampler with Wrap Mode:**
```hlsl
// Custom inline sampler for tiling behavior during rotation
SamplerState sampler_linear_repeat;

// Usage in kernels
float4 sample = InputTexturesArray.SampleLevel(sampler_linear_repeat, float3(rotatedUV, i), 0);
```

**Tiling Behavior:**
- `sampler_linear_repeat` uses Wrap address mode (AddressU/V = Wrap)
- When rotation pushes UV coordinates outside [0,1], they automatically wrap/tile
- Provides seamless tiling for rotated textures without manual UV clamping
- Linear filtering ensures smooth interpolation between texels
- Essential for terrain textures and repeating patterns with rotation

## Performance Optimizations

### 1. Kernel ID Caching

**Problem:** `FindKernel()` uses string lookup (slow)

**Solution:** Cache IDs during initialization
```csharp
// Awake - once
kernelBlendArrayAdditive = shader.FindKernel("BlendTexturesArrayAdditive");

// Runtime - instant lookup
int kernel = kernelBlendArrayAdditive;
```

**Speedup:** ~0.1ms per blend

### 2. Shader Parameter ID Caching

**Problem:** `Shader.PropertyToID()` allocates

**Solution:** Static readonly fields
```csharp
private static readonly int InputTexturesArrayID = 
    Shader.PropertyToID("InputTexturesArray");

// Usage - no allocation
shader.SetTexture(kernel, InputTexturesArrayID, textureArray);
```

**Speedup:** Zero GC allocations

### 3. Profiler Markers

**Purpose:** Fine-grained performance tracking

**Usage:**
```csharp
using (s_TextureArrayConversion.Auto())
{
    // Measured code
}

// In Unity Profiler:
// Shows exact time spent in this section
```

**Markers:**
- ConvertToArray: Texture2DArray creation time
- Dispatch: GPU compute shader execution
- AllocateResources: Pool allocation overhead
- CacheCheck: Cache lookup time

### 4. Pool Prewarming

**Problem:** First-frame allocation stalls

**Solution:** Preallocate common sizes
```csharp
if (useTexturePooling)
{
    resources.PrewarmPool(1024, 1024, outputFormat, 2);  // VR
    resources.PrewarmPool(2048, 2048, outputFormat, 2);  // Desktop
}
```

**Benefit:** Eliminates first-frame spike

### 5. Fast Path for Uniform Sizes

**Problem:** Size checking and scaling expensive

**Solution:** Detect uniform sizes early
```csharp
bool allSameSize = true;
foreach (var tex in textures)
{
    if (tex.width != firstWidth || tex.height != firstHeight)
    {
        allSameSize = false;
        break;
    }
}

if (allSameSize)
{
    return BuildFromUniformTexturesFast();  // 50% faster
}
```

### 6. Zero-Rotation Optimization

**Problem:** Most blends don't use rotation, but preparing rotation arrays still costs ~0.05ms

**Solution:** Cache zero-filled arrays and detect when rotation is unnecessary

```csharp
// Cache for zero-filled rotation arrays
private Dictionary<int, float[]> cachedZeroRotations = new Dictionary<int, float[]>();
private const float RotationEpsilon = 0.0001f;

// Check if any rotation is actually needed
private bool IsRotationNeeded(float[] rotationsDegrees)
{
    if (rotationsDegrees == null || rotationsDegrees.Length == 0)
        return false;
        
    for (int i = 0; i < rotationsDegrees.Length; i++)
    {
        if (Mathf.Abs(rotationsDegrees[i]) > RotationEpsilon)
            return true;
    }
    return false;
}

// Prepare rotation angles with optimization
private float[] PrepareRotationAngles(int textureCount, float[] rotationsDegrees)
{
    // Fast path: No rotation needed
    if (!IsRotationNeeded(rotationsDegrees))
    {
        // Return cached zero array for this texture count
        if (!cachedZeroRotations.TryGetValue(textureCount, out float[] cachedZeros))
        {
            cachedZeros = new float[textureCount];  // Already initialized to 0
            cachedZeroRotations[textureCount] = cachedZeros;
        }
        return cachedZeros;
    }
    
    // Slow path: Convert degrees to radians for actual rotation
    float[] rotations = new float[textureCount];
    for (int i = 0; i < textureCount; i++)
    {
        float degrees = (i < rotationsDegrees.Length) ? rotationsDegrees[i] : 0f;
        rotations[i] = degrees * Mathf.Deg2Rad;
    }
    return rotations;
}
```

**Performance Impact:**
- Without optimization: ~0.05ms per blend (array allocation + loop)
- With optimization: ~0.001ms per blend (dictionary lookup)
- **Improvement: 98% faster** for the common case (no rotation)

**Why It Matters:**
- 95%+ of blends don't use rotation
- Cached arrays reused across all zero-rotation blends
- Zero GC allocations after first cache population
- Minimal memory cost (~100 bytes for typical cache)

**Usage Patterns:**
```csharp
// Method 1: Pass null (zero overhead)
RenderTexture result = blender.BlendTextures(textures, weights, null);

// Method 2: Pass zero array (also optimized)
float[] rotations = { 0f, 0f, 0f };
RenderTexture result = blender.BlendTextures(textures, weights, rotations);

// Method 3: Use overload without rotation parameter (fastest)
RenderTexture result = blender.BlendTextures(textures, weights);

// All three have identical performance (~0.001ms rotation overhead)
```### 7. Zero-Offset Optimization
**Problem:** Most blends don't use UV offset, but preparing offset arrays still costs ~0.05ms
**Solution:** Cache zero-filled arrays and detect when offset is unnecessary
```csharp
// Cache for zero-filled offset arrays
private Dictionary<int, float[]> cachedZeroOffsets = new Dictionary<int, float[]>();
private const float OffsetEpsilon = 0.0001f;
// Check if any offset is actually needed
private bool IsOffsetNeeded(Vector2[] offsets)
{
    if (offsets == null || offsets.Length == 0)
        return false;
    for (int i = 0; i < offsets.Length; i++)
    {
        if (Mathf.Abs(offsets[i].x) > OffsetEpsilon || Mathf.Abs(offsets[i].y) > OffsetEpsilon)
            return true;
    }
    return false;
}
// Prepare UV offsets with optimization
private float[] PrepareUVOffsets(int textureCount, Vector2[] offsets)
{
    // Fast path: No offset needed
    if (!IsOffsetNeeded(offsets))
    {
        int arraySize = textureCount * 2;  // x,y pairs
        if (!cachedZeroOffsets.TryGetValue(arraySize, out float[] cachedZeros))
        {
            cachedZeros = new float[arraySize];  // Already initialized to 0
            cachedZeroOffsets[arraySize] = cachedZeros;
        }
        return cachedZeros;
    }
    // Slow path: Convert Vector2[] to interleaved float array [x0, y0, x1, y1, ...]
    float[] result = new float[textureCount * 2];
    for (int i = 0; i < textureCount; i++)
    {
        Vector2 offset = (i < offsets.Length) ? offsets[i] : Vector2.zero;
        result[i * 2] = offset.x;
        result[i * 2 + 1] = offset.y;
    }
    return result;
}
```
**Performance Impact:**
- Without optimization: ~0.05ms per blend (array allocation + conversion)
- With optimization: ~0.001ms per blend (dictionary lookup)
- **Improvement: 98% faster** for the common case (no offset)
**Why It Matters:**
- 90%+ of blends don't use offset
- Cached arrays reused across all zero-offset blends
- Zero GC allocations after first cache population
- Minimal memory cost (~100-200 bytes for typical cache)
**Usage Patterns:**
```csharp
// Method 1: Pass null (zero overhead)
RenderTexture result = blender.BlendTextures(textures, weights, null, null);
// Method 2: Pass zero offsets (also optimized)
Vector2[] offsets = { Vector2.zero, Vector2.zero };
RenderTexture result = blender.BlendTextures(textures, weights, null, offsets);
// Method 3: Use overload without offset parameter (fastest)
RenderTexture result = blender.BlendTextures(textures, weights);
// All three have identical performance (~0.001ms offset overhead)
```

## Memory Management

### RenderTexture Lifecycle

```
1. Blend requested
   ├─> Pool check (width, height, format)
   │    ├─> Available? → Dequeue and return
   │    └─> Empty? → Create new
   │
2. User uses RenderTexture
   │
3. User calls ReturnTexture()
   ├─> Pool size < max?
   │    ├─> YES → Enqueue for reuse
   │    └─> NO → Release immediately
   │
4. Component destroyed
   └─> Release all pooled textures
```

### ComputeBuffer Lifecycle

Similar to RenderTexture:
- Pooled by element count
- FIFO queue per size
- Max size limit
- Validation on retrieval

### Texture2DArray Lifecycle

**Not pooled - tracked for cleanup:**
```csharp
// Creation
Texture2DArray array = BuildFromTextures(textures);
tempArrays.Add(array);  // Track

// Disposal
foreach (var array in tempArrays)
{
    Destroy(array);
}
tempArrays.Clear();
```

Why not pooled?
- Size varies (texture count dimension)
- Less reusable than RenderTextures
- Cache provides performance benefit

## Async Architecture

### UniTask Integration

```csharp
public async UniTask<RenderTexture> BlendTexturesAsync(...)
{
    // Perform blend synchronously on current frame
    RenderTexture result = BlendTextures(...);
    
    // Yield to next frame for pacing
    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
    
    // Return result
    return result;
}
```

**Why this pattern?**
- Compute shaders are async by nature
- Unity manages GPU/CPU sync automatically
- UniTask.Yield provides frame pacing
- Prevents multiple blends per frame

## Blend Mode Implementation

### Additive

```hlsl
float4 result = float4(0, 0, 0, 0);

for (int i = 0; i < TextureCount; i++)
{
    float4 texColor = InputTexturesArray[uint3(id.xy, i)];
    float weight = BlendValues[i];
    result += texColor * weight;
}

return result;
```

**Characteristics:**
- Simplest (just accumulation)
- Can exceed 1.0 (HDR)
- No branching (fastest)

### AlphaWeighted

```hlsl
float4 result = float4(0, 0, 0, 0);

for (int i = 0; i < TextureCount; i++)
{
    float4 texColor = InputTexturesArray[uint3(id.xy, i)];
    float weight = BlendValues[i];
    float alpha = texColor.a;
    
    result = lerp(result, texColor, weight * alpha);
}

return result;
```

**Characteristics:**
- Respects alpha channels
- Smooth blending
- More complex than Additive

### Multiplicative

```hlsl
float4 result = float4(1, 1, 1, 1);

for (int i = 0; i < TextureCount; i++)
{
    float4 texColor = InputTexturesArray[uint3(id.xy, i)];
    float weight = BlendValues[i];
    
    result *= lerp(float4(1,1,1,1), texColor, weight);
}

return result;
```

**Characteristics:**
- Darkening operation
- Starts at white
- Good for masking

## Extension Points

### Adding Custom Blend Modes

**1. Add Compute Shader Kernel:**
```hlsl
[numthreads(8,8,1)]
void BlendTexturesArrayCustom(uint3 id : SV_DispatchThreadID)
{
    // Your blend logic
}
```

**2. Add Enum Value:**
```csharp
public enum BlendMode
{
    Additive,
    AlphaWeighted,
    Multiplicative,
    Custom  // New
}
```

**3. Cache Kernel ID:**
```csharp
private int kernelBlendArrayCustom;

void Initialize()
{
    kernelBlendArrayCustom = imageProcessorShader.FindKernel("BlendTexturesArrayCustom");
}
```

**4. Update Kernel Selector:**
```csharp
private int GetKernelForBlendMode(BlendMode mode)
{
    switch (mode)
    {
        case BlendMode.Custom:
            return kernelBlendArrayCustom;
        // ...
    }
}
```

### Adding Custom Resource Types

Extend `TextureBlenderResources`:
```csharp
// Add new pool
private Dictionary<SomeKey, Queue<CustomResource>> customPool;

// Add get/return methods
public CustomResource GetOrCreateCustomResource(...)
{
    // Pool logic
}

public void ReturnCustomResource(CustomResource resource)
{
    // Return to pool
}

// Add to Dispose()
public void Dispose()
{
    // ... existing cleanup
    
    // Custom cleanup
    foreach (var queue in customPool.Values)
    {
        while (queue.Count > 0)
        {
            queue.Dequeue()?.Dispose();
        }
    }
}
```

## Design Decisions

### Why Texture2DArray?

**Alternatives considered:**
1. ~~8 separate texture parameters~~ (hard limit)
2. ~~Texture2D with packed layers~~ (manual packing/unpacking)
3. **✓ Texture2DArray** (no limit, clean API)

**Benefits:**
- No hard texture count limit
- Clean shader indexing: `array[uint3(x,y,layer)]`
- Single bind operation
- GPU cache friendly

### Why Resource Pooling?

**Problem:** Creating RenderTextures is expensive (~0.5-1ms)

**Solution:** Reuse previously created resources

**Trade-off:**
- Memory: Higher (pools held in RAM)
- Speed: Lower allocation overhead
- Complexity: Pool management code

Decision: Worth it for performance-critical applications

### Why Cache Texture2DArrays?

**Problem:** Conversion takes 1-2ms per blend

**Solution:** Cache by texture instance ID hash

**Trade-off:**
- Memory: ~64MB per 4×2048² cached set
- Speed: 1-2ms savings per blend
- Complexity: Cache invalidation logic

Decision: Essential for real-time applications

### Why Dual Compute Shader Output?

**Problem:** OpenGL ES 3.0 lacks RWTexture2D support

**Solution:** Write to both texture and buffer

**Trade-off:**
- GPU time: ~5% overhead (minimal)
- Compatibility: Universal support
- Code complexity: Slight increase

Decision: VR compatibility worth small overhead

## Future Improvements

### Potential Optimizations

1. **Async GPU Readback**
   - Currently blocks on dispatch
   - Could use AsyncGPUReadback for better pipelining

2. **Mipmap Generation**
   - Currently disabled
   - Could add optional mipmap support

3. **Multi-Resolution Pyramid**
   - Generate multiple resolutions simultaneously
   - Useful for LOD systems

4. **Persistent Buffers**
   - Keep buffers allocated between frames
   - Reduce allocation overhead further

### Potential Features

1. **Custom Weight Textures**
   - Per-pixel weight maps
   - More flexible than uniform weights

2. **3D Texture Support**
   - Blend volume textures
   - Useful for volumetric effects

3. **Cube Map Support**
   - Blend cube maps
   - Useful for skyboxes/reflections

4. **Animation Support**
   - Blend animated textures
   - Frame interpolation

