# Texture Blending System - API Reference

## Overview
The Texture Blending System provides a flexible, high-performance solution for blending multiple textures using GPU compute shaders. It removes the 8-texture limitation of the legacy system and adds support for multiple blend modes, texture rotation, and resource pooling.

**Location**: Relative to project root: `Assets/LiquidForce/TextureBlender/`

## Key Features
- ✅ **Unlimited textures** - No hard limit (GPU-dependent, typically 2048)
- ✅ **Texture rotation** - Per-texture rotation with zero-overhead optimization
- ✅ **UV offset** - Per-texture UV panning/shifting with zero-overhead optimization
- ✅ **Multiple blend modes** - Additive, AlphaWeighted, Multiplicative
- ✅ **High performance** - <5ms for 4×2048² textures, <2ms cached
- ✅ **Resource pooling** - Automatic RenderTexture and ComputeBuffer reuse
- ✅ **Texture array caching** - Major speedup for repeated blends
- ✅ **Normal map support** - Rotate and offset normals with their base textures
- ✅ **VR compatible** - OpenGL ES 3.0 support with automatic fallback (Quest/Pico)
- ✅ **Clean API** - Simple one-line blending
- ✅ **Zero memory leaks** - Automatic resource management

## Quick Start

### Basic Usage
```csharp
using UnityEngine;

public class MyScript : MonoBehaviour
{
    [SerializeField] private TextureBlender textureBlender;
    [SerializeField] private Texture[] texturesToBlend;
    [SerializeField] private MeshRenderer targetRenderer;
    
    private void Start()
    {
        // Simple one-line blend (target=null creates new RenderTexture)
        RenderTexture result = textureBlender.BlendTextures(null, texturesToBlend, null);
        
        // Apply to material
        targetRenderer.material.mainTexture = result;
    }
}
```

### Setup in Unity Editor
1. Create empty GameObject: "Texture Blender"
2. Add `TextureBlender` component
3. Assign `TextureBlenderComputeShader.compute` shader
4. Configure default output settings (2048x2048 recommended)
5. Enable performance features:
   - ✓ Use Texture Pooling
   - □ Fast Mode (skip validation)

**Note:** Texture2DArray caching is always enabled automatically

## API Reference

### TextureBlender Component

#### Public Methods

##### BlendTextures()
Blends multiple textures. Pass null for target to create new RenderTexture, or pass existing RenderTexture to blend into it.

```csharp
// Create new RenderTexture
public RenderTexture BlendTextures(
    RenderTexture target,         // null = create new, or pass existing RenderTexture
    Texture[] textures,           // Textures to blend (2 to unlimited)
    float[] weights,              // Blend weights (null = equal)
    BlendMode mode = BlendMode.AlphaWeighted)

// With rotation support
public RenderTexture BlendTextures(
    RenderTexture target,         // null = create new, or pass existing RenderTexture
    Texture[] textures,           // Textures to blend (2 to unlimited)
    float[] weights,              // Blend weights
    float[] rotationsDegrees,     // Rotation per texture (0-360 degrees)
    BlendMode mode = BlendMode.AlphaWeighted)

// With rotation and UV offset support
public RenderTexture BlendTextures(
    RenderTexture target,         // null = create new, or pass existing RenderTexture
    Texture[] textures,           // Textures to blend (2 to unlimited)
    float[] weights,              // Blend weights
    float[] rotationsDegrees,     // Rotation per texture (0-360 degrees)
    Vector2[] offsets,            // UV offsets per texture
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Performance**: <5ms for 4×2048² textures, <2ms for cached repeat blends
**Rotation Performance**: Zero overhead when all rotations are 0° (cached zero arrays)
**Offset Performance**: Zero overhead when all offsets are zero (cached zero arrays)

**Example**:
```csharp
// Equal weights, alpha-weighted blending (creates new RenderTexture)
RenderTexture result = blender.BlendTextures(null, myTextures, null);

// Custom weights
float[] weights = { 0.5f, 0.3f, 0.2f };
RenderTexture result = blender.BlendTextures(null, myTextures, weights);

// With rotation (0-360 degrees per texture)
float[] rotations = { 0f, 45f, 90f };  // Rotate 2nd by 45°, 3rd by 90°
RenderTexture result = blender.BlendTextures(null, myTextures, weights, rotations);

// With UV offset (panning/shifting textures)
Vector2[] offsets = { new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(0.2f, 0.8f) };
RenderTexture result = blender.BlendTextures(null, myTextures, weights, null, offsets);

// With both rotation and offset
float[] rotations = { 0f, 45f, 90f };
Vector2[] offsets = { new Vector2(0.5f, 0), Vector2.zero, new Vector2(0.25f, 0.25f) };
RenderTexture result = blender.BlendTextures(null, myTextures, weights, rotations, offsets);

// Different blend mode
RenderTexture result = blender.BlendTextures(null, myTextures, null, BlendMode.Additive);
```

##### BlendTextures() - To Existing Texture
Blends into existing RenderTexture (fastest - no allocation).

```csharp
public RenderTexture BlendTextures(
    RenderTexture target,
    Texture[] textures,
    float[] weights,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Performance**: Fastest option when reusing render targets

**Example**:
```csharp
// Create once
RenderTexture reusableTarget = new RenderTexture(2048, 2048, 0);
reusableTarget.enableRandomWrite = true;
reusableTarget.Create();

// Blend multiple times (no allocation)
blender.BlendTextures(reusableTarget, textures1, weights);
// ... use result ...
blender.BlendTextures(reusableTarget, textures2, weights);
// ... use result ...
```

##### BatchBlend()
Executes multiple blend operations efficiently.

```csharp
public RenderTexture[] BatchBlend(BlendRequest[] requests)
```

**Example**:
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

##### ReturnTexture()
Returns RenderTexture to pool for reuse.

```csharp
public void ReturnTexture(RenderTexture texture)
```

**Example**:
```csharp
RenderTexture temp = blender.BlendTextures(null, textures, null);
// ... use texture ...
blender.ReturnTexture(temp);  // Return to pool for reuse
```

##### BlendNormalsWithBaseAlpha()
Blends normal maps with per-pixel alpha weighting from base textures.

```csharp
// Without rotation
public RenderTexture BlendNormalsWithBaseAlpha(
    Texture[] normalTextures,     // Normal maps to blend
    Texture[] baseTextures,       // Base textures (alpha for weighting)
    float[] weights = null,       // Blend weights
    BlendMode mode = BlendMode.AlphaWeighted)

// With rotation (recommended for visual coherence)
public RenderTexture BlendNormalsWithBaseAlpha(
    Texture[] normalTextures,     // Normal maps to blend
    Texture[] baseTextures,       // Base textures (alpha for weighting)
    float[] weights,              // Blend weights
    float[] rotationsDegrees,     // Rotation per texture (should match base)
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Important**: Rotation should match base texture rotation for visual coherence!

**Example**:
```csharp
// Blend base textures with rotation
float[] rotations = { 0f, 45f, 90f };
RenderTexture baseResult = blender.BlendTextures(
    baseTextures, weights, rotations, BlendMode.AlphaWeighted);

// Blend normals with SAME rotation for visual coherence
RenderTexture normalResult = blender.BlendNormalsWithBaseAlpha(
    normalTextures, baseTextures, weights, rotations, BlendMode.AlphaWeighted);

// Apply both to material
material.SetTexture("_BaseMap", baseResult);
material.SetTexture("_BumpMap", normalResult);
```

##### BlendNormalsWithBaseAlphaToExistingTexture()
Blends normal maps into existing RenderTexture (fastest).

```csharp
// Without rotation
public void BlendNormalsWithBaseAlphaToExistingTexture(
    RenderTexture target,
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    BlendMode mode = BlendMode.AlphaWeighted)

// With rotation
public void BlendNormalsWithBaseAlphaToExistingTexture(
    RenderTexture target,
    Texture[] normalTextures,
    Texture[] baseTextures,
    float[] weights,
    float[] rotationsDegrees,
    BlendMode mode = BlendMode.AlphaWeighted)
```

**Performance**: Fastest option for normal map blending when reusing render targets

**Example**:
```csharp
// Create persistent targets
RenderTexture baseTarget = new RenderTexture(2048, 2048, 0);
RenderTexture normalTarget = new RenderTexture(2048, 2048, 0);
baseTarget.enableRandomWrite = true;
normalTarget.enableRandomWrite = true;
baseTarget.Create();
normalTarget.Create();

// Blend both with same rotations (zero allocation)
float[] rotations = { 0f, 30f, 60f };
blender.BlendToExistingTexture(baseTarget, baseTextures, weights, rotations);
blender.BlendNormalsWithBaseAlphaToExistingTexture(
    normalTarget, normalTextures, baseTextures, weights, rotations);
```

### Blend Modes

#### BlendMode.Additive
Simple weighted sum of all textures.
- **Speed**: FASTEST (30% faster than alpha-weighted)
- **Use case**: Combining light maps, glow effects, HDR accumulation
- **Formula**: `result = Σ(texture[i] * weight[i])`

#### BlendMode.AlphaWeighted
Original alpha-weighted blending. Each texture's contribution weighted by its alpha channel.
- **Speed**: Standard
- **Use case**: Smooth transitions, transparency blending, terrain splatmaps
- **Formula**: `result = lerp(result, texture, weight * texture.a)`

#### BlendMode.Multiplicative
Multiplies texture colors together.
- **Speed**: Standard
- **Use case**: Masking, darkening effects, occlusion maps
- **Formula**: `result *= lerp(white, texture, weight)`

### BlendRequest Struct

```csharp
public struct BlendRequest
{
    public Texture[] inputTextures;          // Textures to blend
    public float[] blendWeights;             // Blend weights
    public BlendMode blendMode;              // Blend mode
    public RenderTexture targetOutput;       // null = create new
    public int outputWidth;                  // Output resolution
    public int outputHeight;
}
```

## Rotation Feature

### Overview
The rotation feature allows each texture to be rotated independently before blending, with zero performance overhead when rotation isn't needed.

### Key Benefits
- **Per-texture rotation**: Each texture can have a different rotation angle (0-360°)
- **Zero overhead optimization**: When all rotations are 0°, uses cached zero arrays (no allocation)
- **Visual coherence**: Rotate normal maps with their base textures for correct lighting
- **GPU-accelerated**: Rotation computed in shader with bilinear filtering

### Rotation Performance
| Rotation State | Overhead |
|----------------|----------|
| All rotations = 0° | **Zero** (cached arrays) |
| Any rotation > 0° | <0.1ms (degree-to-radian conversion) |

### Best Practices
1. **Always rotate normals with their base textures** to maintain visual coherence
2. **Use same rotation array** for both base and normal blending operations
3. **Pass null or all-zeros** when rotation isn't needed (zero overhead)
4. **Rotation is in degrees** (0-360°) for artist-friendly workflow

### Rotation Example: Terrain Variation
```csharp
public class ProceduralTerrainVariation : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] grassTextures;
    [SerializeField] private Texture[] grassNormals;
    
    public RenderTexture CreateVariedGrassPatch(int seed)
    {
        // Random rotation for variation
        Random.InitState(seed);
        float[] rotations = new float[grassTextures.Length];
        for (int i = 0; i < rotations.Length; i++)
        {
            rotations[i] = Random.Range(0f, 360f);  // Random rotation per layer
        }
        
        // Equal weights
        float[] weights = new float[grassTextures.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = 1f / grassTextures.Length;
        }
        
        // Blend with rotation
        return blender.BlendTextures(null, grassTextures, weights, rotations);
    }
}
```

## Performance Guide

### Target Performance
| Texture Count | Resolution  | Desktop (RTX 3070) | Quest 2 (OpenGL ES) | Quest 3 (Vulkan) | Quest 3 (OpenGL) |
|---------------|-------------|--------------------|--------------------|------------------|------------------|
| 4             | 1024x1024   | <2ms               | <5ms (+copy)       | <2ms             | <4ms (+copy)     |
| 4             | 2048x2048   | <5ms               | <10ms (+copy)      | <5ms             | <9ms (+copy)     |
| 8             | 2048x2048   | <8ms               | <15ms (+copy)      | <8ms             | <13ms (+copy)    |
| 16            | 2048x2048   | <10ms              | <17ms (+copy)      | <10ms            | <15ms (+copy)    |
| 4 (cached)    | 2048x2048   | <2ms               | <7ms (+copy)       | <2ms             | <6ms (+copy)     |

**Note**: 
- **Quest 2**: OpenGL ES 3.0 only, requires buffer copy (+2-5ms overhead) ✅ FIXED April 2026
- **Quest 3/Pro/Pico 4+**: **Use Vulkan for full performance** (no buffer copy), falls back to OpenGL ES 3.0 if needed
- Rotation adds negligible overhead due to GPU-accelerated sampling

### Performance Tips

1. **Texture2DArray Caching** (Always Enabled)
   - Automatically saves 1-2ms per repeat blend
   - Caches Texture2DArray conversions based on texture instance IDs
   - Cleared automatically on component destroy

2. **Use Texture Pooling** (Inspector: Use Texture Pooling)
   - Saves 0.5-1ms by avoiding RenderTexture allocation
   - Set Max Pooled Textures to 5-10 for best results

3. **BlendTextures() for Updates**
   - Fastest method when blending to existing target
   - Perfect for real-time parameter tweaking

4. **Fast Mode** (Inspector: Fast Mode)
   - Skips null checks and validation
   - Use only with validated inputs
   - Additional 0.1-0.2ms speedup

5. **Choose Right Blend Mode**
   - Additive is 30% faster than AlphaWeighted
   - Use Additive when alpha blending not needed

6. **Rotation Optimization**
   - Pass null or all-zeros when rotation not needed (zero overhead)
   - Rotations are cached per texture count

7. **VR Optimization**
   - Use 1024x1024 or lower resolution
   - Enable Fast Mode
   - Consider Additive blend mode

### Profiling

Use Unity Profiler markers:
- `TextureBlender.ConvertToArray` - Texture array conversion time
- `TextureBlender.Dispatch` - GPU compute shader execution
- `TextureBlender.AllocateResources` - Resource allocation overhead
- `TextureBlender.CacheCheck` - Cache lookup time

## Examples

### Example 1: Real-time Terrain Texture Blending
```csharp
public class TerrainTextureBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] terrainLayers;
    [SerializeField] private MeshRenderer terrainRenderer;
    
    private RenderTexture terrainTexture;
    
    private void Start()
    {
        // Create persistent render target
        terrainTexture = new RenderTexture(2048, 2048, 0);
        terrainTexture.enableRandomWrite = true;
        terrainTexture.Create();
        
        terrainRenderer.material.mainTexture = terrainTexture;
    }
    
    public void UpdateTerrainBlend(float[] layerWeights)
    {
        // Fast update - no allocation
        blender.BlendToExistingTexture(
            terrainTexture,
            terrainLayers,
            layerWeights,
            TextureBlender.BlendMode.AlphaWeighted);
    }
}
```

### Example 1b: Terrain with Texture Rotation & Normal Maps
```csharp
public class TerrainTextureBlenderWithRotation : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] baseTextures;
    [SerializeField] private Texture[] normalTextures;
    [SerializeField] private MeshRenderer terrainRenderer;
    
    private RenderTexture baseResult;
    private RenderTexture normalResult;
    
    private void Start()
    {
        // Create persistent render targets
        baseResult = new RenderTexture(2048, 2048, 0);
        normalResult = new RenderTexture(2048, 2048, 0);
        baseResult.enableRandomWrite = true;
        normalResult.enableRandomWrite = true;
        baseResult.Create();
        normalResult.Create();
        
        // Apply to material
        terrainRenderer.material.SetTexture("_BaseMap", baseResult);
        terrainRenderer.material.SetTexture("_BumpMap", normalResult);
    }
    
    public void UpdateTerrainBlend(float[] weights, float[] rotations)
    {
        // Blend base textures with rotation (zero allocation)
        blender.BlendToExistingTexture(baseResult, baseTextures, weights, rotations);
        
        // Blend normals with SAME rotation for visual coherence (zero allocation)
        blender.BlendNormalsWithBaseAlphaToExistingTexture(
            normalResult, normalTextures, baseTextures, weights, rotations);
    }
}
```

### Example 2: Batch Processing for Multiple Materials
```csharp
public class MultipleMaterialBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] wallTextures;
    [SerializeField] private Texture[] floorTextures;
    [SerializeField] private MeshRenderer wallRenderer;
    [SerializeField] private MeshRenderer floorRenderer;
    
    private void Start()
    {
        // Create batch request for multiple blends
        var requests = new TextureBlender.BlendRequest[]
        {
            new TextureBlender.BlendRequest
            {
                inputTextures = wallTextures,
                blendWeights = null,  // Equal weights
                blendMode = TextureBlender.BlendMode.AlphaWeighted,
                targetOutput = null,
                outputWidth = 2048,
                outputHeight = 2048
            },
            new TextureBlender.BlendRequest
            {
                inputTextures = floorTextures,
                blendWeights = null,
                blendMode = TextureBlender.BlendMode.Additive,
                targetOutput = null,
                outputWidth = 2048,
                outputHeight = 2048
            }
        };
        
        // Execute batch (efficient sequential processing)
        RenderTexture[] results = blender.BatchBlend(requests);
        
        // Apply results
        wallRenderer.material.mainTexture = results[0];
        floorRenderer.material.mainTexture = results[1];
    }
}
```

### Example 3: Performance Testing
```csharp
public class BlendPerformanceTester : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] testTextures;
    
    [ContextMenu("Test Performance")]
    private void TestPerformance()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // First blend (uncached)
        var result = blender.BlendTextures(null, testTextures, null);
        var firstTime = sw.ElapsedMilliseconds;
        
        blender.ReturnTexture(result);
        
        // Second blend (cached)
        sw.Restart();
        result = blender.BlendTextures(null, testTextures, null);
        var cachedTime = sw.ElapsedMilliseconds;
        
        Debug.Log($"First: {firstTime}ms, Cached: {cachedTime}ms, Speedup: {firstTime/(float)cachedTime:F2}x");
    }
}
```

### Example 4: Procedural Texture with Rotation
```csharp
public class ProceduralWallTexture : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] brickTextures;
    [SerializeField] private Texture[] brickNormals;
    [SerializeField] private MeshRenderer wallRenderer;
    
    private void Start()
    {
        // Create varied brick pattern with random rotations
        float[] weights = { 0.4f, 0.3f, 0.2f, 0.1f };
        float[] rotations = { 0f, 90f, 180f, 270f };  // 90° increments for brick variation
        
        // Blend base textures
        RenderTexture baseResult = blender.BlendTextures(
            null, brickTextures, weights, rotations, null, TextureBlender.BlendMode.AlphaWeighted);
        
        // Blend normals with SAME rotations
        RenderTexture normalResult = blender.BlendNormalsWithBaseAlpha(
            brickNormals, brickTextures, weights, rotations, TextureBlender.BlendMode.AlphaWeighted);
        
        // Apply to material
        wallRenderer.material.SetTexture("_BaseMap", baseResult);
        wallRenderer.material.SetTexture("_BumpMap", normalResult);
    }
}
```

## Compute Shader Details

### TextureBlenderComputeShader.compute

**Location**: `TextureBlenderComputeShader.compute` (same directory as TextureBlender.cs)

**Kernels**:
- `BlendTexturesArrayAdditive` - Additive blending (fastest)
- `BlendTexturesArrayAlphaWeighted` - Alpha-weighted blending
- `BlendTexturesArrayMultiplicative` - Multiplicative blending
- `BlendNormalsWithBaseAlphaAlphaWeighted` - Normal map blending with per-pixel alpha

**Thread Group Size**: `[numthreads(8,8,1)]`
- Optimized for RTX series GPUs
- 64 threads per group = 2 warps
- Best balance of occupancy and register pressure

**Inputs**:
- `Texture2DArray InputTexturesArray` - All input textures
- `Texture2DArray BaseTexturesArray` - Base textures (for normal blending only)
- `StructuredBuffer<float> BlendValues` - Blend weights
- `StructuredBuffer<float> RotationAngles` - Rotation angles in radians (NEW)
- `int TextureCount` - Number of textures
- `int TextureWidth/Height` - Output dimensions

**Outputs**:
- `RWTexture2D<float4> OutputTexture` - Direct texture write
- `RWStructuredBuffer<float4> OutputBuffer` - Buffer write (VR compatibility)

**Rotation Implementation**:
- Rotates UV coordinates around center (0.5, 0.5) per texture
- Uses bilinear filtering for smooth results
- Angles passed in radians (converted from degrees in C#)
- Zero-overhead when all angles are 0° (cached arrays)

## Troubleshooting

### Issue: Slow first blend
**Solution**: First blend includes Texture2DArray conversion (~1-2ms). Repeat blends use cache automatically (always enabled).

### Issue: Memory leaks
**Solution**: Always call `ReturnTexture()` when done, or enable Texture Pooling.

### Issue: Textures look wrong
**Solution**: Check that all textures are properly assigned. Null textures are replaced with transparent black.

### Issue: Rotated textures look blurry
**Solution**: Shader uses bilinear filtering. For pixel-perfect rotation, use 90° increments (0°, 90°, 180°, 270°).

### Issue: Normal maps don't match base texture rotation
**Solution**: Always use the SAME rotation array for both BlendTextures() and BlendNormalsWithBaseAlpha().

### Issue: Black textures on Quest/Pico VR headsets
**Solution (FIXED April 2026)**: OpenGL ES 3.0 now uses automatic buffer copy fallback. System detects platform and copies OutputBuffer to texture via CPU (adds 2-5ms overhead). Use `forceBufferCopyPath` debug flag in Inspector to test fallback path in Editor.

### Issue: Cache not working
**Solution**: Cache uses texture instance IDs and is always enabled. Cache is cleared automatically when the component is destroyed. Modified textures with the same instance ID will reuse cached arrays (hash doesn't change).

### Issue: Performance worse than expected
**Solution**: 
1. Check Unity Profiler markers
2. Ensure textures are GPU-readable
3. Enable Fast Mode if inputs are validated
4. Consider lower resolution for VR
5. Pass null for rotations if not needed (zero overhead)

## Migration from ImageProcessorTest

### Old Code
```csharp
public class MyOldCode : MonoBehaviour
{
    private void TestBlendTextures(RenderTexture outputTexture)
    {
        // Limited to 8 textures, manual buffer management, no rotation
        // ... complex setup code ...
    }
}
```

### New Code
```csharp
public class MyNewCode : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    
    private void BlendTextures()
    {
        // Unlimited textures, automatic resource management, optional rotation
        float[] rotations = { 0f, 45f, 90f };
        RenderTexture result = blender.BlendTextures(null, myTextures, myWeights, rotations, null);
    }
}
```

## Support

For questions or issues:
1. Check Unity Console for error messages
2. Enable profiler markers to diagnose performance
3. Use `TextureBlenderBenchmark` component to validate performance
4. Review `TextureBlenderExample` for usage patterns

## Credits

Built for BlueDesertFox VR project using Unity 6 (6000.3.10f1) with URP 17.3.0.
