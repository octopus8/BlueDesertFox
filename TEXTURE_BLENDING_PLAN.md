# Flexible Texture Blending System - Implementation Plan

## Overview
Create a flexible texture blending system using the ImageProcessor.compute shader's BlendTextures kernel that can blend **any number** of textures (not limited to 8), with improved architecture for reusability and extensibility.

## Current System Limitations
1. **Hard-coded 8 texture limit** - Fixed texture slots (InputTexture0-7) in compute shader
2. **Test-only implementation** - `ImageProcessorTest.cs` is a test component, not production-ready
3. **No reusable API** - Requires copying TestBlendTextures() code for each use case
4. **Manual resource management** - Users must manually handle ComputeBuffer creation/disposal
5. **Fixed blending mode** - Only supports alpha-weighted blending

## Goals
1. Create a **reusable MonoBehaviour component** that can blend N textures
2. Support **dynamic texture counts** (1 to unlimited)
3. Provide **clean public API** for runtime texture blending
4. Implement **automatic resource management** (no memory leaks)
5. Support **multiple blending modes** (additive, alpha-weighted, multiplicative, etc.)
6. Enable **batch processing** (blend multiple texture sets efficiently)
7. Maintain **VR compatibility** (OpenGL ES 3.0 support via RWStructuredBuffer)

## Proposed Architecture

### Component Structure
```
TextureBlender.cs (new)                    - Main reusable component
├── BlendMode enum (nested)                - Additive, AlphaWeighted, Multiplicative
├── BlendRequest struct (nested)           - Encapsulates blend operation parameters
└── Core functionality                     - Blending methods and resource management

ImageProcessorEnhanced.compute (new)       - Enhanced compute shader
├── BlendTexturesAdditive kernel           - Simple additive blending
├── BlendTexturesAlphaWeighted kernel      - Current alpha-weighted blending
└── BlendTexturesMultiplicative kernel     - Multiplicative blending
```

### Key Classes

#### 1. TextureBlender Component
```csharp
public class TextureBlender : MonoBehaviour
{
    // Nested types
    public enum BlendMode { Additive, AlphaWeighted, Multiplicative }
    public struct BlendRequest { /* ... */ }
    
    // Public API
    public RenderTexture BlendTextures(Texture[] textures, float[] weights = null, BlendMode mode = BlendMode.AlphaWeighted)
    public void BlendTexturesAsync(Texture[] textures, float[] weights, BlendMode mode, Action<RenderTexture> callback)
    public void BlendToExistingTexture(RenderTexture target, Texture[] textures, float[] weights, BlendMode mode)
    
    // Configuration
    [SerializeField] private ComputeShader imageProcessorShader;
    [SerializeField] private int defaultOutputWidth = 2048;
    [SerializeField] private int defaultOutputHeight = 2048;
    [SerializeField] private RenderTextureFormat outputFormat = RenderTextureFormat.ARGB32;
    
    // Automatic resource pooling
    private TextureBlenderResources resources;
}
```

#### 2. BlendRequest Struct (nested in TextureBlender)
```csharp
public struct BlendRequest
{
    public Texture[] inputTextures;
    public float[] blendWeights;
    public BlendMode blendMode;
    public RenderTexture targetOutput;  // null = create new
    public int outputWidth;
    public int outputHeight;
    public bool linearColorSpace;
}
```

#### 3. BlendMode Enum (nested in TextureBlender)
```csharp
public enum BlendMode
{
    Additive,           // Simple sum with weights
    AlphaWeighted,      // Current implementation (multiply by alpha)
    Multiplicative      // Multiply colors together
}
```

## Implementation Plan

### Phase 1: Enhanced Compute Shader (ImageProcessorEnhanced.compute)

**Goal**: Remove 8-texture hard limit by using TextureArray or runtime binding patterns

#### Option A: Texture2DArray Approach (Recommended)
```hlsl
#pragma kernel BlendTexturesArray

Texture2DArray<float4> InputTexturesArray;
RWTexture2D<float4> OutputTexture;
RWStructuredBuffer<float4> OutputBuffer;
StructuredBuffer<float> BlendValues;
int TextureCount;
int TextureWidth;
int TextureHeight;

[numthreads(8,8,1)]
void BlendTexturesArray(uint3 id : SV_DispatchThreadID)
{
    float2 uv = (id.xy + 0.5) / float2(TextureWidth, TextureHeight);
    float4 blendedColor = float4(0, 0, 0, 0);
    
    // Loop through all textures in array
    for (int i = 0; i < TextureCount; i++)
    {
        float4 sample = InputTexturesArray.SampleLevel(samplerInputTextures, float3(uv, i), 0);
        float blendIntensity = BlendValues[i] * sample.a;
        blendedColor = blendedColor * (1.0 - blendIntensity) + sample * blendIntensity * sample.a;
    }
    
    OutputBuffer[id.y * TextureWidth + id.x] = blendedColor;
    OutputTexture[id.xy] = blendedColor;
}
```

**Pros**: 
- No texture count limit (up to GPU max array size, typically 2048)
- Clean loop-based blending
- Single kernel dispatch

**Cons**: 
- Requires converting textures to Texture2DArray at runtime (CPU cost)
- All textures must be same size and format

#### Option B: Multi-Pass Approach (Alternative)
Keep current 8-slot approach but blend in batches:
- Pass 1: Blend textures 0-7 → temp output
- Pass 2: Blend temp + textures 8-15 → temp output
- Pass N: Final result

**Pros**: 
- Works with different texture sizes
- No array conversion needed

**Cons**: 
- Multiple dispatches (slower for many textures)
- Requires intermediate RenderTextures

**Decision**: Use **Option A** for primary implementation, **Option B** as fallback for mismatched texture sizes.

#### Tasks:
1. **Create ImageProcessorEnhanced.compute**
   - Implement `BlendTexturesArray` kernel using Texture2DArray
   - Implement blend mode variants:
     - `BlendTexturesArrayAdditive` - Simple weighted sum
     - `BlendTexturesArrayAlphaWeighted` - Current alpha-weighted logic
     - `BlendTexturesArrayMultiplicative` - Color multiplication
   - Add proper documentation for each kernel
   - Keep LinearToSRGB conversion logic for color accuracy

2. **Create helper functions in shader**:
   ```hlsl
   float4 BlendAdditive(float4 base, float4 overlay, float weight);
   float4 BlendAlphaWeighted(float4 base, float4 overlay, float weight);
   float4 BlendMultiplicative(float4 base, float4 overlay, float weight);
   ```

### Phase 2: TextureBlender Component

#### Core Component Implementation

**File**: `Assets/_App/Scripts/TextureBlending/TextureBlender.cs`

```csharp
using UnityEngine;
using System;
using Cysharp.Threading.Tasks;  // For async support

public class TextureBlender : MonoBehaviour
{
    [Header("Shader Configuration")]
    [SerializeField] private ComputeShader imageProcessorShader;
    
    [Header("Default Output Settings")]
    [SerializeField] private int defaultOutputWidth = 2048;
    [SerializeField] private int defaultOutputHeight = 2048;
    [SerializeField] private RenderTextureFormat outputFormat = RenderTextureFormat.ARGB32;
    
    [Header("Performance Settings")]
    [SerializeField] private bool useTexturePooling = true;
    [SerializeField] private int maxPooledTextures = 5;
    
    // Resource management
    private TextureBlenderResources resources;
    private bool isInitialized = false;
    
    // Kernel IDs (cached for performance)
    private int kernelBlendArray;
    private int kernelBlendArrayAdditive;
    private int kernelBlendArrayAlphaWeighted;
    
    // Shader parameter IDs
    private static readonly int InputTexturesArrayID = Shader.PropertyToID("InputTexturesArray");
    private static readonly int OutputTextureID = Shader.PropertyToID("OutputTexture");
    private static readonly int OutputBufferID = Shader.PropertyToID("OutputBuffer");
    private static readonly int BlendValuesID = Shader.PropertyToID("BlendValues");
    private static readonly int TextureCountID = Shader.PropertyToID("TextureCount");
    private static readonly int TextureWidthID = Shader.PropertyToID("TextureWidth");
    private static readonly int TextureHeightID = Shader.PropertyToID("TextureHeight");
}
```

#### Public API Methods

1. **Simple Blend** (most common use case):
```csharp
/// <summary>
/// Blends multiple textures into a new RenderTexture.
/// </summary>
/// <param name="textures">Array of textures to blend (any count)</param>
/// <param name="weights">Optional blend weights (null = equal weights)</param>
/// <param name="mode">Blend mode to use</param>
/// <returns>New RenderTexture with blended result</returns>
public RenderTexture BlendTextures(
    Texture[] textures, 
    float[] weights = null, 
    BlendMode mode = BlendMode.AlphaWeighted)
```

2. **Async Blend** (for non-blocking operations):
```csharp
/// <summary>
/// Blends textures asynchronously and invokes callback when complete.
/// Useful for loading screens or background processing.
/// </summary>
public async UniTask<RenderTexture> BlendTexturesAsync(
    Texture[] textures, 
    float[] weights = null, 
    BlendMode mode = BlendMode.AlphaWeighted,
    CancellationToken cancellationToken = default)
```

3. **Blend to Existing** (for updating existing textures):
```csharp
/// <summary>
/// Blends textures into an existing RenderTexture (no allocation).
/// </summary>
public void BlendToExistingTexture(
    RenderTexture target, 
    Texture[] textures, 
    float[] weights, 
    BlendMode mode = BlendMode.AlphaWeighted)
```

4. **Batch Blend** (for multiple blend operations):
```csharp
/// <summary>
/// Executes multiple blend requests in a batch (efficient GPU usage).
/// </summary>
public RenderTexture[] BatchBlend(BlendRequest[] requests)
```

#### Tasks:
1. **Implement initialization**:
   - Validate compute shader reference in Awake()
   - Cache kernel IDs and shader property IDs
   - Initialize resource pools

2. **Implement BlendTextures() core method**:
   - Convert input textures to Texture2DArray (helper method)
   - Normalize blend weights (or use equal weights)
   - Create output RenderTexture
   - Create/reuse ComputeBuffer for blend weights
   - Set shader parameters
   - Dispatch compute shader
   - Return result RenderTexture

3. **Implement async variant**:
   - Use UniTask for async/await pattern
   - Support CancellationToken for cleanup
   - Yield after dispatch for frame pacing

4. **Implement resource management**:
   - Auto-dispose temporary resources
   - Pool RenderTextures for reuse
   - Track and clean up buffers in OnDestroy()

### Phase 3: Helper Utilities

#### Texture2DArray Conversion Utility

**File**: `Assets/_App/Scripts/TextureBlending/TextureArrayBuilder.cs`

```csharp
public static class TextureArrayBuilder
{
    /// <summary>
    /// Converts array of Texture2D into a Texture2DArray for compute shader.
    /// Handles size mismatches by scaling to largest texture dimensions.
    /// </summary>
    public static Texture2DArray BuildFromTextures(
        Texture[] textures, 
        out int width, 
        out int height,
        bool mipChain = false)
    {
        // Find largest texture dimensions
        // Create temporary RenderTextures to scale mismatched sizes
        // Copy into Texture2DArray layers
        // Return array and output dimensions
    }
    
    /// <summary>
    /// Creates a Texture2DArray from textures of the same size (fast path).
    /// </summary>
    public static Texture2DArray BuildFromUniformTextures(Texture2D[] textures)
    {
        // Assumes all textures same size
        // Direct copy without scaling
    }
}
```

#### Resource Pool

**File**: `Assets/_App/Scripts/TextureBlending/TextureBlenderResources.cs`

```csharp
/// <summary>
/// Manages pooled resources for TextureBlender to minimize allocations.
/// </summary>
public class TextureBlenderResources : IDisposable
{
    private Queue<RenderTexture> renderTexturePool;
    private Queue<ComputeBuffer> bufferPool;
    private List<Texture2DArray> tempArrays;  // Track for disposal
    
    public RenderTexture GetOrCreateRenderTexture(int width, int height, RenderTextureFormat format);
    public void ReturnRenderTexture(RenderTexture rt);
    
    public ComputeBuffer GetOrCreateBuffer(int count, int stride);
    public void ReturnBuffer(ComputeBuffer buffer);
    
    public void Dispose() { /* Clean up all resources */ }
}
```

### Phase 4: Compute Shader Enhancements

#### Performance Optimization Guidelines

**Thread Group Size**: Use `[numthreads(8,8,1)]` for RTX series GPUs (64 threads per group = 2 warps)
- Tested alternatives: 16×16 (slower on mid-range), 4×4 (poor occupancy)
- 8×8 provides best balance of occupancy and register pressure

**Loop Unrolling**: 
```hlsl
// For small texture counts, unroll for speed
#pragma unroll
for (int i = 0; i < TextureCount; i++)
{
    // Will unroll if TextureCount is compile-time constant
}
```

**Register Usage**: Store sampled texture in local variable to avoid redundant samples:
```hlsl
float4 sample = InputTexturesArray.SampleLevel(samplerInputTextures, float3(uv, i), 0);
// Reuse 'sample' multiple times without re-sampling
```

**Branch Reduction**: Minimize divergent branches in loops (all blend modes use uniform loop structure)

#### Blend Mode Implementations

Each blend mode gets its own kernel for optimal performance:

```hlsl
// Additive blending (simple weighted sum)
[numthreads(8,8,1)]
void BlendTexturesArrayAdditive(uint3 id : SV_DispatchThreadID)
{
    float2 uv = (id.xy + 0.5) / float2(TextureWidth, TextureHeight);
    float4 blendedColor = float4(0, 0, 0, 0);
    
    for (int i = 0; i < TextureCount; i++)
    {
        float4 sample = InputTexturesArray.SampleLevel(samplerInputTextures, float3(uv, i), 0);
        sample = LinearToSRGB(sample);
        blendedColor += sample * BlendValues[i];
    }
    
    OutputBuffer[id.y * TextureWidth + id.x] = blendedColor;
    OutputTexture[id.xy] = blendedColor;
}

// Alpha-weighted blending (current implementation)
[numthreads(8,8,1)]
void BlendTexturesArrayAlphaWeighted(uint3 id : SV_DispatchThreadID)
{
    float2 uv = (id.xy + 0.5) / float2(TextureWidth, TextureHeight);
    float4 blendedColor = float4(0, 0, 0, 0);
    
    for (int i = 0; i < TextureCount; i++)
    {
        float4 sample = InputTexturesArray.SampleLevel(samplerInputTextures, float3(uv, i), 0);
        sample = LinearToSRGB(sample);
        float blendIntensity = BlendValues[i] * sample.a;
        blendedColor = blendedColor * (1.0 - blendIntensity) + sample * blendIntensity * sample.a;
    }
    
    OutputBuffer[id.y * TextureWidth + id.x] = blendedColor;
    OutputTexture[id.xy] = blendedColor;
}

// Multiplicative blending
[numthreads(8,8,1)]
void BlendTexturesArrayMultiplicative(uint3 id : SV_DispatchThreadID)
{
    float2 uv = (id.xy + 0.5) / float2(TextureWidth, TextureHeight);
    float4 blendedColor = float4(1, 1, 1, 1);  // Start with white
    
    for (int i = 0; i < TextureCount; i++)
    {
        float4 sample = InputTexturesArray.SampleLevel(samplerInputTextures, float3(uv, i), 0);
        sample = LinearToSRGB(sample);
        // Lerp between white (no effect) and sample color based on weight
        float4 weighted = lerp(float4(1,1,1,1), sample, BlendValues[i]);
        blendedColor *= weighted;
    }
    
    OutputBuffer[id.y * TextureWidth + id.x] = blendedColor;
    OutputTexture[id.xy] = blendedColor;
}
```

### Phase 5: Example Usage Component

**File**: `Assets/_App/Scripts/TextureBlending/TextureBlenderExample.cs`

```csharp
/// <summary>
/// Example component showing how to use TextureBlender in various scenarios.
/// </summary>
public class TextureBlenderExample : MonoBehaviour
{
    [SerializeField] private TextureBlender textureBlender;
    [SerializeField] private Texture[] texturesToBlend;
    [SerializeField] private float[] blendWeights;
    [SerializeField] private MeshRenderer targetRenderer;
    
    private async void Start()
    {
        // Example 1: Simple blend with default settings
        RenderTexture result1 = textureBlender.BlendTextures(texturesToBlend);
        targetRenderer.material.mainTexture = result1;
        
        // Example 2: Custom weights and blend mode
        RenderTexture result2 = textureBlender.BlendTextures(
            texturesToBlend, 
            blendWeights, 
            BlendMode.Multiplicative);
        
        // Example 3: Async blend (non-blocking)
        RenderTexture result3 = await textureBlender.BlendTexturesAsync(
            texturesToBlend, 
            blendWeights, 
            BlendMode.AlphaWeighted,
            this.GetCancellationTokenOnDestroy());
        
        // Example 4: Blend to existing texture (no allocation)
        RenderTexture existingTexture = new RenderTexture(2048, 2048, 0);
        textureBlender.BlendToExistingTexture(
            existingTexture, 
            texturesToBlend, 
            blendWeights);
    }
}
```

## Testing Strategy

### Unit Tests
**File**: `Assets/_App/Tests/Editor/TextureBlenderTests.cs`

```csharp
[TestFixture]
public class TextureBlenderTests
{
    [Test]
    public void BlendTextures_WithEqualWeights_ProducesAverageColor()
    [Test]
    public void BlendTextures_WithCustomWeights_RespectsWeighting()
    [Test]
    public void BlendTextures_WithZeroTextures_ThrowsException()
    [Test]
    public void BlendTextures_WithNullTextures_ThrowsException()
    [Test]
    public void BlendTextures_DisposesResourcesProperly()
    [Test]
    public void TextureArrayBuilder_HandlesUnequalSizes()
}
```

### Integration Tests
**File**: `Assets/_App/Test Scenes/TextureBlending/TextureBlendingTests.unity`

1. Test blending 2 textures
2. Test blending 10 textures
3. Test blending 100 textures (stress test)
4. Test different blend modes side-by-side
5. Test async blending with UI feedback
6. Test memory leaks (create/destroy 100 times)

## Performance Considerations

### Optimization Strategies

1. **Texture2DArray conversion**:
   - Cache converted arrays if input textures don't change
   - Use native GPU upload when possible
   - Consider async conversion for large textures

2. **ComputeBuffer pooling**:
   - Reuse buffers across blend operations
   - Preallocate common sizes
   - Auto-dispose after N frames of non-use

3. **RenderTexture pooling**:
   - Pool by size/format
   - LRU eviction policy
   - Configurable pool size limits

4. **Batch operations**:
   - Group multiple blend requests
   - Single shader bind per batch
   - Minimize CPU-GPU sync points

5. **VR optimization**:
   - Single-pass stereo support (if needed)
   - Lower default resolution for VR (1024x1024)
   - Option to skip LinearToSRGB conversion for performance

### Memory Budget
Estimate for typical use case (blending 4 textures at 2048x2048):
- Input Texture2DArray: 4 × 2048 × 2048 × 4 bytes = 64 MB
- Output RenderTexture: 2048 × 2048 × 4 bytes = 16 MB
- Output ComputeBuffer: 2048 × 2048 × 16 bytes (float4) = 64 MB
- Blend weights buffer: 4 × 4 bytes = 16 bytes (negligible)
- **Total: ~144 MB GPU memory per blend operation**

## File Structure

```
Assets/
├── _App/
│   └── Scripts/
│       └── TextureBlending/
│           ├── TextureBlender.cs                 # Main component (includes BlendMode enum and BlendRequest struct)
│           ├── TextureBlenderResources.cs         # Resource pooling
│           ├── TextureArrayBuilder.cs             # Texture2DArray conversion
│           └── Examples/
│               └── TextureBlenderExample.cs       # Usage examples
├── Shaders/
│   └── Compute/
│       └── ImageProcessorEnhanced.compute        # Enhanced shader
└── Test Scenes/
    └── TextureBlending/
        ├── TextureBlendingTests.unity            # Integration tests
        └── TestAssets/
            └── TestTextures/                      # Test texture assets
```

## Migration Path from Current System

### Step 1: Keep existing ImageProcessorTest.cs
- Mark as deprecated but functional
- Add comment pointing to new TextureBlender component

### Step 2: Side-by-side testing
- Create test scene with both implementations
- Verify identical output for same inputs
- Performance comparison

### Step 3: Update documentation
- Update AGENTS.md with new TextureBlender usage
- Create TEXTURE_BLENDING_SYSTEM.md with API reference

## Success Criteria

✅ Can blend 2-100+ textures without code changes  
✅ Simple one-line API: `BlendTextures(textures, weights, mode)`  
✅ No memory leaks after 1000 blend operations  
✅ Performance <16ms for 4 textures @ 2048x2048 on mid-range GPU  
✅ Works in VR (OpenGL ES 3.0 compatibility maintained)  
✅ Supports async operations for background processing  
✅ Clean resource management (auto-dispose)  
✅ Multiple blend modes available  
✅ 100% test coverage for core functionality  

## Future Enhancements (Post-MVP)

1. **Custom blend functions**: Allow users to provide custom HLSL blend code
2. **GPU readback**: Optional CPU texture output for saving to disk
3. **Real-time preview**: Editor window for live blend preview
4. **Blend curves**: AnimationCurve-based weight interpolation
5. **Spatial blending**: Use mask texture to control blend per-pixel
6. **Mipmap support**: Generate mipmaps for blended output
7. **Compression support**: Output to compressed formats (DXT, ETC)
8. **3D texture support**: Blend volume textures (for VFX)

## Dependencies

- **Unity DOTS**: None (pure MonoBehaviour)
- **UniTask**: For async/await support (already in project)
- **Compute Shader support**: Requires Unity 2020.3+ with compute shader capability
- **VR packages**: Already in project (OpenXR, XR Hands)

## Estimated Effort

- Phase 1 (Compute Shader): 4-6 hours
- Phase 2 (TextureBlender Component): 6-8 hours
- Phase 3 (Utilities): 4-6 hours
- Phase 4 (Blend Modes): 2-3 hours
- Phase 5 (Examples & Tests): 3-4 hours
- **Total: 19-27 hours**

## Notes for AI Agent

1. **Start with Phase 1**: Get compute shader working with Texture2DArray first
2. **Test incrementally**: Create simple test scene after each phase
3. **Follow existing patterns**: Use same structure as current ImageProcessorTest.cs
4. **Maintain VR compatibility**: Always write to both OutputTexture and OutputBuffer
5. **Document as you go**: Add XML comments to all public APIs
6. **Use project conventions**: 
   - SerializeField with [field: SerializeField] for auto-properties
   - UniTask for async operations
   - Shader.PropertyToID for parameter caching
   - IDisposable pattern for resource cleanup

---

This plan provides a comprehensive roadmap for creating a flexible texture blending system that overcomes the current 8-texture limitation while maintaining performance and VR compatibility.

