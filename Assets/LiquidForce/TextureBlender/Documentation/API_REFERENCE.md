# TextureBlender API Reference

Complete API documentation for the TextureBlender system.

## TextureBlender Component

Main component for GPU-accelerated texture blending.

### Public Methods

---

#### BlendTextures()

Blends multiple textures into a new RenderTexture.

**Basic Blend:**
```csharp
public RenderTexture BlendTextures(
    Texture[] textures,
    float[] weights = null,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation:**
```csharp
public RenderTexture BlendTextures(
    Texture[] textures,
    float[] weights,
    float[] rotationsDegrees,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation and UV Offset:**
```csharp
public RenderTexture BlendTextures(
    Texture[] textures,
    float[] weights,
    float[] rotationsDegrees,
    Vector2[] offsets,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `textures` - Array of textures to blend (any count)
- `weights` - Optional blend weights (null = equal weights)
- `rotationsDegrees` - Optional rotation per texture (0-360°, null = no rotation)
- `offsets` - Optional UV offsets per texture (null = no offset, automatically tiles/wraps)
- `mode` - Blend mode to use (default: AlphaWeighted)

**Returns:** New RenderTexture with blended result

**Performance:** 
- <5ms for 4×2048² textures, <2ms for cached repeat blends
- Zero overhead when rotation is 0° or null (cached zero arrays, 98% faster)
- Zero overhead when offset is zero or null (cached zero arrays, 98% faster)

**Transformation Order:**
1. UV offset applied first (pans the texture)
2. Rotation applied second (rotates around center)
3. Automatic tiling/wrapping at boundaries

**Example:**
```csharp
// Equal weights
RenderTexture result = blender.BlendTextures(myTextures);

// Custom weights
float[] weights = { 0.5f, 0.3f, 0.2f };
RenderTexture result = blender.BlendTextures(myTextures, weights);

// With rotation (0-360 degrees per texture)
float[] rotations = { 0f, 45f, 90f };
RenderTexture result = blender.BlendTextures(myTextures, weights, rotations);

// With rotation and offset (full control)
float[] rotations = { 0f, 45f, 90f };
Vector2[] offsets = { new Vector2(0.5f, 0), Vector2.zero, new Vector2(0.25f, 0.25f) };
RenderTexture result = blender.BlendTextures(myTextures, weights, rotations, offsets);

// With offset only (pass null for rotations)
Vector2[] offsets = { new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(0.2f, 0.8f) };
RenderTexture result = blender.BlendTextures(myTextures, weights, null, offsets);

// Different mode
RenderTexture result = blender.BlendTextures(
    myTextures, 
    weights, 
    BlendMode.Additive);
```

---

#### BlendTexturesAsync()

Blends textures asynchronously (non-blocking).

**Basic Async:**
```csharp
public async UniTask<RenderTexture> BlendTexturesAsync(
    Texture[] textures,
    float[] weights = null,
    BlendMode mode = BlendMode.AlphaWeighted,
    CancellationToken cancellationToken = default)
```

**With Rotation:**
```csharp
public async UniTask<RenderTexture> BlendTexturesAsync(
    Texture[] textures,
    float[] weights,
    float[] rotationsDegrees,
    BlendMode mode = BlendMode.AlphaWeighted,
    CancellationToken cancellationToken = default)
```

**With Rotation and UV Offset:**
```csharp
public async UniTask<RenderTexture> BlendTexturesAsync(
    Texture[] textures,
    float[] weights,
    float[] rotationsDegrees,
    Vector2[] offsets,
    BlendMode mode = BlendMode.AlphaWeighted,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `textures` - Array of textures to blend
- `weights` - Optional blend weights
- `rotationsDegrees` - Optional rotation per texture (0-360°)
- `offsets` - Optional UV offsets per texture (automatically tiles/wraps)
- `mode` - Blend mode to use
- `cancellationToken` - Cancellation token for async operation

**Returns:** UniTask that resolves to blended RenderTexture

**Example:**
```csharp
private async void BlendAsync()
{
    RenderTexture result = await blender.BlendTexturesAsync(
        textures,
        weights,
        BlendMode.AlphaWeighted,
        this.GetCancellationTokenOnDestroy());
    
    renderer.material.mainTexture = result;
}

// With rotation
private async void BlendAsyncRotated()
{
    float[] rotations = { 0f, 45f, 90f };
    RenderTexture result = await blender.BlendTexturesAsync(
        textures,
        weights,
        rotations,
        BlendMode.AlphaWeighted,
        this.GetCancellationTokenOnDestroy());
    
    renderer.material.mainTexture = result;
}

// With rotation and offset
private async void BlendAsyncWithOffset()
{
    float[] rotations = { 0f, 45f, 90f };
    Vector2[] offsets = { new Vector2(0.5f, 0), Vector2.zero, new Vector2(0.25f, 0.25f) };
    RenderTexture result = await blender.BlendTexturesAsync(
        textures,
        weights,
        rotations,
        offsets,
        BlendMode.AlphaWeighted,
        this.GetCancellationTokenOnDestroy());
    
    renderer.material.mainTexture = result;
}
```

---

#### BlendToExistingTexture()

Blends textures into an existing RenderTexture (no allocation).

**Basic Blend:**
```csharp
public void BlendToExistingTexture(
    RenderTexture target, 
    Texture[] textures, 
    float[] weights, 
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation:**
```csharp
public void BlendToExistingTexture(
    RenderTexture target,
    Texture[] textures,
    float[] weights,
    float[] rotationsDegrees,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation and UV Offset:**
```csharp
public void BlendToExistingTexture(
    RenderTexture target,
    Texture[] textures,
    float[] weights,
    float[] rotationsDegrees,
    Vector2[] offsets,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `target` - Existing RenderTexture to blend into
- `textures` - Array of textures to blend
- `weights` - Blend weights (required)
- `rotationsDegrees` - Optional rotation per texture (0-360°, null = no rotation)
- `offsets` - Optional UV offsets per texture (null = no offset, automatically tiles/wraps)
- `mode` - Blend mode to use

**Performance:** Fastest option when reusing render targets

**Example:**
```csharp
// Create once
RenderTexture reusableTarget = new RenderTexture(2048, 2048, 0);
reusableTarget.enableRandomWrite = true;
reusableTarget.Create();

// Blend multiple times (no allocation)
blender.BlendToExistingTexture(reusableTarget, textures1, weights);
// ... use result ...

blender.BlendToExistingTexture(reusableTarget, textures2, weights);
// ... use result ...

// With rotation
float[] rotations = { 0f, 45f, 90f };
blender.BlendToExistingTexture(reusableTarget, textures3, weights, rotations);

// With rotation and offset
Vector2[] offsets = { new Vector2(0.5f, 0), Vector2.zero, new Vector2(0.25f, 0.25f) };
blender.BlendToExistingTexture(reusableTarget, textures4, weights, rotations, offsets);
```

---

#### BlendNormalsWithBaseAlpha()

Blends normal maps with per-pixel alpha weighting from base textures.

**Basic Blend:**
```csharp
public RenderTexture BlendNormalsWithBaseAlpha(
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights = null,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation:**
```csharp
public RenderTexture BlendNormalsWithBaseAlpha(
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    float[] rotationsDegrees,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation and UV Offset:**
```csharp
public RenderTexture BlendNormalsWithBaseAlpha(
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    float[] rotationsDegrees,
    Vector2[] offsets,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `normalTextures` - Array of normal map textures
- `baseTextures` - Array of base textures (alpha used for per-pixel weighting)
- `weights` - Blend weights for each layer
- `rotationsDegrees` - Optional rotation per texture (0-360°) - **MUST match base texture rotations!**
- `offsets` - Optional UV offsets per texture (automatically tiles/wraps) - **MUST match base texture offsets!**
- `mode` - Blend mode to use

**Returns:** New RenderTexture with blended normal map

**Important:** When using rotation or offset, normal map transformations **MUST** match the base texture transformations for visual coherence. Otherwise lighting will not align with surface detail.

**Example:**
```csharp
// Blend normals using base texture alpha for per-pixel weighting
RenderTexture normalMap = blender.BlendNormalsWithBaseAlpha(
    normalTextures,
    baseTextures,
    weights,
    BlendMode.AlphaWeighted);

material.SetTexture("_BumpMap", normalMap);

// With rotation - CRITICAL: Use same rotations as base textures!
float[] rotations = { 0f, 45f, 90f };

// Blend base textures
RenderTexture baseMap = blender.BlendTextures(
    baseTextures, weights, rotations, BlendMode.AlphaWeighted);

// Blend normals with SAME rotation
RenderTexture normalMap = blender.BlendNormalsWithBaseAlpha(
    normalTextures, baseTextures, weights, rotations, BlendMode.AlphaWeighted);

// Apply both
material.SetTexture("_BaseMap", baseMap);
material.SetTexture("_BumpMap", normalMap);

// With rotation and offset - CRITICAL: Use same transformations!
float[] rotations = { 0f, 45f, 90f };
Vector2[] offsets = { new Vector2(0.5f, 0), Vector2.zero, new Vector2(0.25f, 0.25f) };

// Blend base textures
RenderTexture baseMap = blender.BlendTextures(
    baseTextures, weights, rotations, offsets, BlendMode.AlphaWeighted);

// Blend normals with SAME rotation and offset
RenderTexture normalMap = blender.BlendNormalsWithBaseAlpha(
    normalTextures, baseTextures, weights, rotations, offsets, BlendMode.AlphaWeighted);

// Apply both
material.SetTexture("_BaseMap", baseMap);
material.SetTexture("_BumpMap", normalMap);
```

---

#### BlendNormalsWithBaseAlphaToExistingTexture()

Blends normal maps into existing RenderTexture with per-pixel alpha weighting.

**Basic Blend:**
```csharp
public void BlendNormalsWithBaseAlphaToExistingTexture(
    RenderTexture target,
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation:**
```csharp
public void BlendNormalsWithBaseAlphaToExistingTexture(
    RenderTexture target,
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    float[] rotationsDegrees,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**With Rotation and UV Offset:**
```csharp
public void BlendNormalsWithBaseAlphaToExistingTexture(
    RenderTexture target,
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    float[] rotationsDegrees,
    Vector2[] offsets,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `target` - Existing RenderTexture to blend into
- `normalTextures` - Array of normal maps
- `baseTextures` - Array of base textures for alpha weighting
- `weights` - Blend weights (required)
- `rotationsDegrees` - Optional rotation per texture (0-360°, null = no rotation) - **MUST match base texture rotations!**
- `offsets` - Optional UV offsets per texture (null = no offset, automatically tiles/wraps) - **MUST match base texture offsets!**
- `mode` - Blend mode to use

**Performance:** Fastest option for normal blending when reusing render targets

**Example:**
```csharp
// Create reusable targets
RenderTexture baseTarget = new RenderTexture(2048, 2048, 0);
baseTarget.enableRandomWrite = true;
baseTarget.Create();

RenderTexture normalTarget = new RenderTexture(2048, 2048, 0);
normalTarget.enableRandomWrite = true;
normalTarget.Create();

float[] weights = { 0.5f, 0.3f, 0.2f };

// Basic blend
blender.BlendToExistingTexture(baseTarget, baseTextures, weights);
blender.BlendNormalsWithBaseAlphaToExistingTexture(
    normalTarget, normalTextures, baseTextures, weights);

// With rotation - MUST match between base and normal!
float[] rotations = { 0f, 45f, 90f };
blender.BlendToExistingTexture(baseTarget, baseTextures, weights, rotations);
blender.BlendNormalsWithBaseAlphaToExistingTexture(
    normalTarget, normalTextures, baseTextures, weights, rotations);

// With rotation and offset - MUST match!
Vector2[] offsets = { new Vector2(0.5f, 0), Vector2.zero, new Vector2(0.25f, 0.25f) };
blender.BlendToExistingTexture(baseTarget, baseTextures, weights, rotations, offsets);
blender.BlendNormalsWithBaseAlphaToExistingTexture(
    normalTarget, normalTextures, baseTextures, weights, rotations, offsets);
```

---

#### BatchBlend()

Executes multiple blend operations in a batch.

```csharp
public RenderTexture[] BatchBlend(BlendRequest[] requests)
```

**Parameters:**
- `requests` - Array of blend requests to execute

**Returns:** Array of RenderTextures with results

**Example:**
```csharp
var requests = new TextureBlender.BlendRequest[]
{
    new TextureBlender.BlendRequest
    {
        inputTextures = textures1,
        blendWeights = weights1,
        blendMode = BlendMode.Additive,
        outputWidth = 1024,
        outputHeight = 1024
    },
    new TextureBlender.BlendRequest
    {
        inputTextures = textures2,
        blendWeights = weights2,
        blendMode = BlendMode.Multiplicative,
        outputWidth = 1024,
        outputHeight = 1024
    }
};

RenderTexture[] results = blender.BatchBlend(requests);
```

---

#### ReturnTexture()

Returns a RenderTexture to the pool for reuse.

```csharp
public void ReturnTexture(RenderTexture texture)
```

**Parameters:**
- `texture` - RenderTexture to return to pool

**Important:** Always call this when done with a blended texture to avoid memory leaks.

**Example:**
```csharp
RenderTexture temp = blender.BlendTextures(textures);
// ... use texture ...
blender.ReturnTexture(temp);  // Return to pool
```

---

## Enums

### BlendMode

Determines how textures are combined.

```csharp
public enum BlendMode
{
    Additive,           // Simple weighted sum (FASTEST)
    AlphaWeighted,      // Alpha-weighted blending
    Multiplicative      // Multiplicative blending
}
```

**Performance Comparison:**
- `Additive`: 30% faster than AlphaWeighted
- `AlphaWeighted`: Standard performance
- `Multiplicative`: Similar to AlphaWeighted

**See:** [Blend Modes Documentation](BLEND_MODES.md) for detailed explanations

---

## Structs

### BlendRequest

Encapsulates parameters for a blend operation.

```csharp
public struct BlendRequest
{
    public Texture[] inputTextures;          // Textures to blend
    public float[] blendWeights;             // Blend weights
    public BlendMode blendMode;              // Blend mode
    public RenderTexture targetOutput;       // null = create new
    public int outputWidth;                  // Output resolution
    public int outputHeight;
    public bool linearColorSpace;            // Color space handling
    
    // Speed control flags
    public bool skipValidation;              // Skip null checks
    public bool useCachedArray;              // Use cached array
    public bool skipColorSpaceConversion;    // Skip conversion
}
```

**Usage:** Primarily for `BatchBlend()` operations

---

## Inspector Properties

### Shader Configuration

- **Image Processor Shader** - ComputeShader reference (required)

### Default Output Settings

- **Default Output Width** - Default output texture width (default: 2048)
- **Default Output Height** - Default output texture height (default: 2048)
- **Output Format** - RenderTexture format (default: ARGB32)

### Performance Settings

- **Use Texture Pooling** - Enable RenderTexture pooling (recommended: true)
- **Max Pooled Textures** - Maximum textures in pool (default: 5)
- **Enable Array Cache** - Cache Texture2DArray conversions (recommended: true)
- **Fast Mode** - Skip validation checks (use with caution)

---

## Profiler Markers

For performance profiling, the following markers are available:

- `TextureBlender.ConvertToArray` - Texture array conversion time
- `TextureBlender.Dispatch` - GPU compute shader execution
- `TextureBlender.AllocateResources` - Resource allocation overhead
- `TextureBlender.CacheCheck` - Cache lookup time

**Usage:** Open Unity Profiler → CPU view → Expand frame to see markers

---

## TextureBlenderResources

Internal resource management class (not directly used).

### Key Methods

```csharp
// Prewarm pools during initialization
public void PrewarmPool(int width, int height, RenderTextureFormat format, int count)
public void PrewarmBufferPool(int elementCount, int stride, int poolSize)

// Resource management (called internally by TextureBlender)
public RenderTexture GetOrCreateRenderTexture(int width, int height, RenderTextureFormat format)
public void ReturnRenderTexture(RenderTexture rt)
public ComputeBuffer GetOrCreateBuffer(int count, int stride)
public void ReturnBuffer(ComputeBuffer buffer)
public Texture2DArray GetOrCreateTextureArray(int hash, Texture2DArray textureArray)

// Cleanup (automatic on component destroy)
public void Dispose()
```

**Resource Types Managed:**
- **RenderTextures:** Pooled by (width, height, format) - reused for blend outputs
- **ComputeBuffers:** Pooled by element count - reused for weights, rotations, offsets
- **Texture2DArrays:** Cached by hash - reused for identical texture sets (35% speedup)

**All resources automatically cleaned up when TextureBlender component is destroyed.**

---

## TextureArrayBuilder

Static utility for Texture2DArray conversion.

### Public Methods

```csharp
// Convert textures to Texture2DArray
public static Texture2DArray BuildFromTextures(
    Texture[] textures,
    out int width,
    out int height,
    bool mipChain = false)

// Compute hash for caching
public static int ComputeTextureArrayHash(Texture[] textures)
```

**Performance:**
- ~1-2ms for 4×2048² textures with size mismatches
- ~0.5-1ms for uniform-sized textures (50% faster)

---

## Error Handling

The system logs errors for:
- Missing compute shader reference
- Null or empty texture arrays
- All textures being null
- Size mismatches in normal/base texture arrays

**Best Practice:** Check Console for error messages if blends fail.

---

## Memory Management

### Automatic Cleanup

- RenderTextures returned to pool automatically on component destroy
- ComputeBuffers released automatically
- Texture2DArrays automatically cleared and disposed on component destroy

### Manual Cleanup

```csharp
// Return individual textures to pool when done
blender.ReturnTexture(result);
```

**Note:** Cache clearing happens automatically when the component is destroyed. The texture array cache uses hash-based lookup, so modified textures automatically get new cache entries.

---

## Thread Safety

**Not thread-safe:** TextureBlender must be used from Unity main thread only.

For async operations, use `BlendTexturesAsync()` which properly handles Unity's threading model via UniTask.
