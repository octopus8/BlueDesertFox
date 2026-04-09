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
│   Caching           │              │                       │
│ - Texture2D Temp    │              │                       │
│   Pooling (OpenGL)  │              │                       │
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
│                     │
│ Outputs:            │
│ - RWTexture2D       │ (Desktop/Vulkan)
│ - RWStructuredBuffer│ (OpenGL ES 3.0)
└─────────────────────┘
         │
         │ OpenGL ES 3.0 Only
         ▼
┌─────────────────────┐
│ Buffer Copy Path    │
│ (CPU Fallback)      │
│                     │
│ Buffer → Texture2D  │ (uses temp pool)
│ Texture2D → Blit    │
│ → RenderTexture     │
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
private bool fastMode = false;

// Runtime state
private TextureBlenderResources resources;
private bool isInitialized = false;

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
       ├─> Detect OpenGL ES 3.0 fallback
       └─> Create flat normal map (1x1 texture)
```

### TextureBlenderResources

**Responsibilities:**
1. RenderTexture pooling
2. ComputeBuffer pooling
3. Texture2DArray caching (hash-based)
4. Temporary Texture2D pooling (OpenGL ES 3.0 buffer copy)
5. Resource cleanup

**Pool Structure:**
```csharp
// RenderTexture pool keyed by (width, height, format)
Dictionary<(int, int, RenderTextureFormat), Queue<RenderTexture>> renderTexturePool;

// ComputeBuffer pool keyed by element count
Dictionary<int, Queue<ComputeBuffer>> bufferPool;

// Texture2DArray cache keyed by hash (always enabled)
Dictionary<int, Texture2DArray> textureArrayCache;

// Temporary Texture2D pool for OpenGL ES 3.0 buffer copies (NEW v3.0.1)
Dictionary<(int, int), Queue<Texture2D>> tempTexturePool;
```

**Pool Behavior:**
- **RenderTexture & ComputeBuffer**: FIFO queue, borrow/return lifecycle
- **Texture2DArray**: Hash-based cache, no return mechanism (persists until dispose)
- **Temporary Texture2D**: FIFO queue, used only for OpenGL ES 3.0 buffer copies
- Max size limit prevents unbounded growth
- Items beyond limit are released/destroyed immediately
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
```
- TextureBlender.BlendTextures
    - TextureBlenderResources.GetOrCreateRenderTexture (RenderTextures; a start texture)
    - TextureBlenderResources.GetOrCreateTextureArray (tracked texture arrays; array of textures to blend)
    - ExecuteBlend
        - TextureBlenderResources.GetOrCreateBuffer (ComputeBuffer; weight, offset, rotation, output buffer)
        - Dispatch
```
**Caching vs Pooling:**
It is worth noting that different resource types use different patterns:

- **Pooling Pattern** (RenderTexture, ComputeBuffer, Temp Texture2D):
  - Borrow/return lifecycle
  - FIFO queues
  - Resources are returned after use
  - Same resource can be reused for different purposes
  - Max pool size limits memory usage

- **Caching Pattern** (Texture2DArray):
  - Hash-based lookup
  - No return mechanism
  - Resources stay cached until component destroyed
  - Same input always returns same cached resource
  - Provides 35% speedup for repeat blends

**Temporary Texture2D Pool** (NEW v3.0.1):
- Used exclusively for OpenGL ES 3.0 buffer-to-texture copies
- Never used on Desktop/Vulkan platforms (zero overhead)
- RGBA32 format for maximum compatibility
- Pooled to avoid allocations during buffer copy (minimizes 2-5ms overhead)
- Automatically returned to pool after each buffer copy operation

**General**
- TextureArrayBuilder.BuildFromTextures sets all textures to the same size.

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
// Managed by TextureBlenderResources (owns complete lifecycle)
// TextureBlender simply delegates to resources

// In TextureBlender:
Texture2DArray textureArray = resources.GetOrCreateTextureArray(textures, out width, out height);

// Inside TextureBlenderResources.GetOrCreateTextureArray():
// 1. Compute hash from texture instance IDs
int hash = TextureArrayBuilder.ComputeTextureArrayHash(textures);

// 2. Check cache
if (textureArrayCache.ContainsKey(hash))
{
    return cachedArray;  // Cache hit: ~0ms
}

// 3. Cache miss - build new array
Texture2DArray newArray = TextureArrayBuilder.BuildFromTextures(textures, out w, out h, false);  // ~1-2ms

// 4. Store in cache
textureArrayCache[hash] = newArray;

// 5. Return
return newArray;
```

**Architecture Pattern:**
- TextureBlenderResources owns the complete lifecycle (check → build → cache → return)
- TextureBlender doesn't know about TextureArrayBuilder or hashing
- Follows same pattern as GetOrCreateRenderTexture() and GetOrCreateBuffer()
- Single Responsibility: Resources manages all resource pooling/caching

**Cache Invalidation:**
- Automatic: Component destroyed (via Dispose())
- Texture instance ID changes trigger natural miss
- Hash-based lookup ensures modified textures get new entries

**Cache Benefits:**
- 35% speedup for repeat blends with same texture set
- Eliminates expensive Texture2DArray conversion
- Automatic cleanup on component destroy
- Always enabled (no configuration needed)

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
// Modern approach (preferred - works on desktop)
OutputTexture[id.xy] = result;

// Fallback for OpenGL ES 3.0 (Quest/Pico) - REQUIRED
OutputBuffer[id.y * TextureWidth + id.x] = result;
```

**Why both?**
- RWTexture2D not supported on OpenGL ES 3.0 (Quest 2, older Android devices)
- Quest 3/Pro/Pico 4+ support Vulkan (RWTexture2D works - use Vulkan!)
- RWStructuredBuffer universally supported
- Desktop: Only RWTexture2D write succeeds (fast path)
- Mobile VR (OpenGL ES 3.0): Only RWStructuredBuffer write succeeds (needs CPU copy)
- Mobile VR (Vulkan): Only RWTexture2D write succeeds (fast path, no copy needed)

**OpenGL ES 3.0 Fallback (April 2026 Fix):**
```csharp
// After shader dispatch on Quest/Pico
if (requiresBufferCopyFallback) // Auto-detected at startup
{
    CopyBufferToTexture(outputBuffer, target);
    // GPU → CPU → GPU transfer (adds 2-5ms)
}
```

**Performance Impact:**
- Desktop (DirectX/Vulkan/Metal): No overhead (buffer ignored)
- Quest 3/Pro with Vulkan: No overhead (RWTexture2D works!)
- Quest 2 / Quest 3 with OpenGL ES 3.0: +2-5ms per blend
- Uses temp texture pooling to minimize allocations

**Debug Testing:**
- Enable `forceBufferCopyPath` in Inspector
- Tests fallback path in Editor without VR device
- Verify correct output before Quest deployment

**Recommendation**: Configure Quest 3/Pro/Pico 4+ to use Vulkan as primary Graphics API for best performance.

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
- BufferCopy: OpenGL ES 3.0 buffer-to-texture copy time (v3.0.1+)

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
```

### 7. Zero-Offset Optimization

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
RenderTexture result = blender.BlendTextures(null, textures, weights, null, null);

// Method 2: Pass zero offsets (also optimized)
Vector2[] offsets = { Vector2.zero, Vector2.zero };
RenderTexture result = blender.BlendTextures(null, textures, weights, null, offsets);

// Method 3: Use overload without offset parameter (fastest)
RenderTexture result = blender.BlendTextures(null, textures, weights);

// All three have identical performance (~0.001ms offset overhead)
```

### 8. Null Normal Texture Handling (Automatic)

**Problem:** Users need normal maps for some layers but not others, requiring manual flat normal map creation

**Solution:** Automatically substitute null normal textures with a pre-created 1x1 flat normal map

**Implementation:**
```csharp
// In TextureBlender initialization
private Texture2D flatNormalMap;

private void CreateFlatNormalMap()
{
    flatNormalMap = new Texture2D(1, 1, TextureFormat.RGB24, false, true);  // linear = true for normals
    Color flatNormal = new Color(0.5f, 0.5f, 1f, 1f);  // Tangent space flat normal
    flatNormalMap.SetPixel(0, 0, flatNormal);
    flatNormalMap.Apply();
    flatNormalMap.name = "FlatNormalMap_Internal";
}

// Before blending normal maps
private Texture[] SubstituteNullNormals(Texture[] normalTextures)
{
    if (normalTextures == null || flatNormalMap == null)
        return normalTextures;
    
    // Check if any nulls exist
    bool hasNulls = false;
    for (int i = 0; i < normalTextures.Length; i++)
    {
        if (normalTextures[i] == null)
        {
            hasNulls = true;
            break;
        }
    }
    
    if (!hasNulls)
        return normalTextures;  // No substitution needed
    
    // Create new array with nulls replaced
    Texture[] result = new Texture[normalTextures.Length];
    for (int i = 0; i < normalTextures.Length; i++)
    {
        result[i] = normalTextures[i] != null ? normalTextures[i] : flatNormalMap;
    }
    return result;
}
```

**Performance Impact:**
- Flat normal map: 1×1 texture = 4 bytes memory
- Null check: ~0.001ms per blend operation
- Array substitution (when needed): ~0.01ms for 8 textures
- **Improvement: Eliminates manual flat normal creation in user code**

**Why It Matters:**
- Common scenario: Terrain with only some layers having normal maps
- Procedural generation where normals are optional
- Simplifies user code - no need to create/manage flat normal textures
- Zero visual impact - flat normals produce no surface change
- Minimal memory cost (4 bytes total for 1×1 texture)

**Usage Example:**
```csharp
// User can pass null for normal textures without any setup!
Texture[] normalTextures = { normal1, null, normal3, null };  // Some nulls OK!
Texture[] baseTextures = { base1, base2, base3, base4 };

// Works automatically - nulls replaced with flat normals internally
RenderTexture normalMap = blender.BlendNormalsWithBaseAlpha(
    normalTextures,  // Can contain nulls!
    baseTextures,
    weights);
```

### 9. Temporary Texture2D Pooling (OpenGL ES 3.0 Buffer Copy)

**Problem:** OpenGL ES 3.0 requires buffer-to-texture copy via CPU, which allocates Texture2D each frame

**Solution:** Pool temporary Texture2D instances to minimize allocations during buffer copy

**Implementation:**
```csharp
// In TextureBlenderResources
private Dictionary<(int, int), Queue<Texture2D>> tempTexturePool;

public Texture2D GetOrCreateTempTexture(int width, int height)
{
    var key = (width, height);
    
    // Try to get from pool
    if (tempTexturePool.ContainsKey(key) && tempTexturePool[key].Count > 0)
    {
        Texture2D texture = tempTexturePool[key].Dequeue();
        if (texture != null)
            return texture;
    }
    
    // Create new Texture2D (RGBA32 for compatibility)
    return new Texture2D(width, height, TextureFormat.RGBA32, false);
}

public void ReturnTempTexture(Texture2D texture)
{
    if (texture == null) return;
    
    var key = (texture.width, texture.height);
    
    if (!tempTexturePool.ContainsKey(key))
        tempTexturePool[key] = new Queue<Texture2D>();
    
    // Only pool if under max size limit
    if (tempTexturePool[key].Count < maxPoolSize)
    {
        tempTexturePool[key].Enqueue(texture);
    }
    else
    {
        UnityEngine.Object.Destroy(texture);
    }
}
```

**Usage in CopyBufferToTexture():**
```csharp
private void CopyBufferToTexture(ComputeBuffer buffer, RenderTexture target)
{
    using (s_BufferCopy.Auto())
    {
        // Get pooled temp texture (avoids allocation)
        Texture2D tempTexture = resources.GetOrCreateTempTexture(target.width, target.height);
        
        // Read buffer → CPU array
        Color[] pixelData = new Color[buffer.count];
        buffer.GetData(pixelData);
        
        // Upload to Texture2D
        tempTexture.SetPixels(pixelData);
        tempTexture.Apply(false);
        
        // Blit to RenderTexture (GPU transfer)
        Graphics.Blit(tempTexture, target);
        
        // Return to pool (reuse next frame)
        resources.ReturnTempTexture(tempTexture);
    }
}
```

**Performance Impact:**
- Without pooling: +0.5-1ms additional overhead (Texture2D allocation + GC)
- With pooling: Minimal allocation overhead after first use
- Total buffer copy time: 2-5ms (GPU→CPU→GPU transfer is unavoidable)

**Platform Behavior:**
- **Desktop/Vulkan**: Temp texture pool never used (zero overhead)
- **Quest 2 (OpenGL ES 3.0)**: Temp texture pool active, minimizes allocations
- **Quest 3/Pro with Vulkan**: Temp texture pool never used (zero overhead)
- **Quest 3/Pro with OpenGL ES 3.0**: Temp texture pool active if Vulkan unavailable

**Memory Usage:**
- 1024×1024 temp texture: ~4MB (RGBA32)
- 2048×2048 temp texture: ~16MB (RGBA32)
- Typical pool size: 1-2 textures (4-8MB for VR)
- Automatically cleaned up in Dispose()

**Why Pooling Matters:**
- Buffer copy happens every frame in VR applications
- Texture2D allocation triggers GC
- Pooling eliminates per-frame allocations
- Reduces total overhead from 3-6ms to 2-5ms
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
- Automatically returned after shader dispatch

### Temporary Texture2D Lifecycle (OpenGL ES 3.0 Only)

**Purpose**: Intermediate storage for buffer-to-texture copies on platforms without RWTexture2D support

**Lifecycle:**
```
1. Buffer copy needed (OpenGL ES 3.0 detected)
   ├─> Pool check (width, height)
   │    ├─> Available? → Dequeue and return
   │    └─> Empty? → Create new Texture2D (RGBA32)
   │
2. CopyBufferToTexture() uses temp texture
   ├─> buffer.GetData(colorArray)
   ├─> tempTexture.SetPixels(colorArray)
   ├─> tempTexture.Apply()
   └─> Graphics.Blit(tempTexture, target)
   │
3. Return temp texture to pool immediately
   ├─> Pool size < max?
   │    ├─> YES → Enqueue for reuse
   │    └─> NO → Destroy immediately
   │
4. Component destroyed
   └─> Destroy all pooled temp textures
```

**Platform Behavior:**
- **Desktop/Vulkan**: Temp texture pool never used, no allocations
- **Quest 2 (OpenGL ES 3.0)**: Temp texture borrowed and returned each blend
- **Quest 3/Pro (Vulkan)**: Temp texture pool never used, no allocations
- **Quest 3/Pro (OpenGL ES 3.0)**: Temp texture borrowed and returned if Vulkan unavailable

**Performance Impact:**
- Without pooling: +0.5-1ms allocation overhead per blend
- With pooling: ~0ms overhead after first use
- Reduces total buffer copy from 3-6ms to 2-5ms

**Memory Footprint:**
- 1024×1024 RGBA32: ~4MB per temp texture
- 2048×2048 RGBA32: ~16MB per temp texture
- Typical pool: 1-2 textures (reused across blends)
- Auto-cleaned on component destroy

### Texture2DArray Lifecycle

**Cached by hash - not pooled:**
```csharp
// Creation (in TextureBlender)
int hash = TextureArrayBuilder.ComputeTextureArrayHash(textures);
Texture2DArray array = resources.GetOrCreateTextureArray(textures, out width, out height);

if (array == null)
{
    // Build new array
    array = TextureArrayBuilder.BuildFromTextures(textures);
    
    // Store in cache (in TextureBlenderResources)
    resources.GetOrCreateTextureArray(hash, array);
}

// Disposal (automatic on component destroy)
// In TextureBlenderResources.Dispose():
foreach (var array in textureArrayCache.Values)
{
    Destroy(array);
}
textureArrayCache.Clear();
```

**Why cached instead of pooled?**
- Hash-based lookup provides better reuse (same texture set = instant retrieval)
- Size varies by texture count (pool keys would be complex)
- Less frequently reused than RenderTextures
- Cache provides 35% speedup for repeat blends

**Cache Management:**
- Owned by `TextureBlenderResources` (consistent with other resources)
- Automatically cleared on component destroy via `Dispose()`
- No manual cache clearing needed (hash-based lookup handles modified textures)

### Resource Management Summary Table

| Resource Type       | Pattern  | Key                        | Max Size | Disposal            | Platform Usage    |
|---------------------|----------|----------------------------|----------|---------------------|-------------------|
| RenderTexture       | Pooling  | (width, height, format)    | Per key  | Release on destroy  | All platforms     |
| ComputeBuffer       | Pooling  | element count              | Per key  | Release on destroy  | All platforms     |
| Texture2DArray      | Caching  | texture hash               | Unlimited| Destroy on destroy  | All platforms     |
| Temp Texture2D      | Pooling  | (width, height)            | Per key  | Destroy on destroy  | OpenGL ES 3.0 only|

**Memory Estimates (1024×1024):**
- RenderTexture (ARGB32): ~4MB each
- Texture2DArray (4 textures): ~16MB total
- Temp Texture2D (RGBA32): ~4MB each
- ComputeBuffer (weights): <1KB each

**Total Memory (Typical VR Setup):**
- RenderTexture pool (3 textures): ~12MB
- Texture2DArray cache (2 sets): ~32MB
- Temp Texture2D pool (2 textures): ~8MB (Quest 2 only, unused on Quest 3 Vulkan)
- ComputeBuffer pool: ~10KB
- **Total**: ~52MB (Quest 2), ~44MB (Quest 3 Vulkan)


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
- GPU time: ~5% overhead for dual write (minimal)
- Compatibility: Universal support across all platforms
- Code complexity: Slight increase
- CPU overhead: +2-5ms on OpenGL ES 3.0 for buffer copy (Quest 2 only if Quest 3/Pro use Vulkan)

**Decision**: VR compatibility worth small overhead, especially since Quest 3/Pro/Pico 4+ can avoid it entirely with Vulkan

### Why Temp Texture2D Pooling? (v3.0.1)

**Problem:** OpenGL ES 3.0 buffer copy allocates Texture2D each frame

**Solution:** Pool temporary Texture2D instances

**Trade-off:**
- Memory: ~4-8MB for typical VR pool (1-2 textures at 1024×1024)
- Speed: Eliminates ~0.5-1ms allocation overhead
- Complexity: Additional pool management

**Decision**: Essential for minimizing OpenGL ES 3.0 overhead (reduces 3-6ms to 2-5ms)

**Platform Impact:**
- Desktop/Vulkan: Pool never created, zero memory/performance cost
- Quest 2: Pool active, reduces buffer copy overhead
- Quest 3/Pro with Vulkan: Pool never created, zero cost
- Only impacts OpenGL ES 3.0 devices

## Future Improvements

### Potential Optimizations

1. **AsyncGPUReadback for OpenGL ES 3.0**
   - Replace synchronous `buffer.GetData()` with async readback
   - Could reduce buffer copy from 2-5ms to <1ms
   - Requires careful frame-pipelining logic
   - Most beneficial for Quest 2 (Quest 3/Pro should use Vulkan anyway)

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

