# TextureBlender API Reference

Complete API documentation for the TextureBlender system.

## TextureBlender Component

Main component for GPU-accelerated texture blending.

### Public Methods

---

#### BlendTextures()

Blends multiple textures into a new RenderTexture.

```csharp
public RenderTexture BlendTextures(
    Texture[] textures,
    float[] weights = null,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `textures` - Array of textures to blend (any count)
- `weights` - Optional blend weights (null = equal weights)
- `mode` - Blend mode to use (default: AlphaWeighted)

**Returns:** New RenderTexture with blended result

**Performance:** <5ms for 4×2048² textures, <2ms for cached repeat blends

**Example:**
```csharp
// Equal weights
RenderTexture result = blender.BlendTextures(myTextures);

// Custom weights
float[] weights = { 0.5f, 0.3f, 0.2f };
RenderTexture result = blender.BlendTextures(myTextures, weights);

// Different mode
RenderTexture result = blender.BlendTextures(
    myTextures, 
    weights, 
    BlendMode.Additive);
```

---

#### BlendTexturesAsync()

Blends textures asynchronously (non-blocking).

```csharp
public async UniTask<RenderTexture> BlendTexturesAsync(
    Texture[] textures,
    float[] weights = null,
    BlendMode mode = BlendMode.AlphaWeighted,
    CancellationToken cancellationToken = default)
```

**Parameters:**
- `textures` - Array of textures to blend
- `weights` - Optional blend weights
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
```

---

#### BlendToExistingTexture()

Blends textures into an existing RenderTexture (no allocation).

```csharp
public void BlendToExistingTexture(
    RenderTexture target,
    Texture[] textures,
    float[] weights,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `target` - Existing RenderTexture to blend into
- `textures` - Array of textures to blend
- `weights` - Blend weights (required)
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
```

---

#### BlendNormalsWithBaseAlpha()

Blends normal maps with per-pixel alpha weighting from base textures.

```csharp
public RenderTexture BlendNormalsWithBaseAlpha(
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights = null,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `normalTextures` - Array of normal map textures
- `baseTextures` - Array of base textures (alpha used for per-pixel weighting)
- `weights` - Blend weights for each layer
- `mode` - Blend mode to use

**Returns:** New RenderTexture with blended normal map

**Note:** Each pixel's normal contribution is modulated by the corresponding base texture alpha channel.

**Example:**
```csharp
// Blend normals using base texture alpha for per-pixel weighting
RenderTexture normalMap = blender.BlendNormalsWithBaseAlpha(
    normalTextures,
    baseTextures,
    weights,
    BlendMode.AlphaWeighted);

material.SetTexture("_BumpMap", normalMap);
```

---

#### BlendNormalsWithBaseAlphaToExistingTexture()

Blends normal maps into existing RenderTexture with per-pixel alpha weighting.

```csharp
public void BlendNormalsWithBaseAlphaToExistingTexture(
    RenderTexture target,
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Parameters:**
- `target` - Existing RenderTexture to blend into
- `normalTextures` - Array of normal maps
- `baseTextures` - Array of base textures for alpha weighting
- `weights` - Blend weights (required)
- `mode` - Blend mode to use

**Performance:** Fastest option for normal blending when reusing render targets

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

#### ClearCache()

Clears the texture array cache.

```csharp
public void ClearCache()
```

**When to call:**
- When input textures have been modified
- When freeing memory after batch operations
- Before switching to completely different texture sets

**Example:**
```csharp
// Modify textures
texture1.SetPixels(newColors);
texture1.Apply();

// Clear cache so next blend uses updated texture
blender.ClearCache();
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

// Resource management (called internally)
public RenderTexture GetOrCreateRenderTexture(int width, int height, RenderTextureFormat format)
public void ReturnRenderTexture(RenderTexture rt)
public ComputeBuffer GetOrCreateBuffer(int count, int stride)
public void ReturnBuffer(ComputeBuffer buffer)
```

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
- Texture2DArrays tracked and disposed

### Manual Cleanup

```csharp
// Return individual textures
blender.ReturnTexture(result);

// Clear cache to free memory
blender.ClearCache();
```

---

## Thread Safety

**Not thread-safe:** TextureBlender must be used from Unity main thread only.

For async operations, use `BlendTexturesAsync()` which properly handles Unity's threading model via UniTask.

