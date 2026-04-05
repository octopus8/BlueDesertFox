# TextureBlender Blend Modes

Understanding the different blend modes and when to use them.

## Overview

TextureBlender supports three blend modes, each optimized for different use cases:

1. **Additive** - Simple weighted sum (FASTEST)
2. **AlphaWeighted** - Respects texture alpha channels
3. **Multiplicative** - Multiplication-based blending

## Blend Mode Details

### Additive Mode

**Formula:** `result = Σ(texture[i] * weight[i])`

**Performance:** FASTEST - 30% faster than AlphaWeighted

**Use Cases:**
- Light map combination
- Glow and emission effects
- HDR accumulation
- Adding brightness/intensity
- When alpha channels don't matter

**Example:**
```csharp
// Combine light maps
RenderTexture lightMap = blender.BlendTextures(
    lightMaps,
    intensities,
    TextureBlender.BlendMode.Additive);

// Result is sum of all lights scaled by intensity
```

**Characteristics:**
- Colors add together
- Can exceed 1.0 (useful for HDR)
- Ignores alpha channels
- Simple and fast computation
- Best for VR when alpha not needed

**Visual Effect:**
- Brightens the final image
- Colors accumulate
- No transparency handling

---

### AlphaWeighted Mode

**Formula:** `result = lerp(result, texture, weight * texture.a)`

**Performance:** Standard (baseline performance)

**Use Cases:**
- Terrain texture splatmapping
- Smooth transitions between textures
- Layer-based compositing
- When alpha matters for blending
- Traditional Photoshop-style blending

**Example:**
```csharp
// Blend terrain textures with alpha masks
RenderTexture terrain = blender.BlendTextures(
    terrainLayers,
    splatWeights,
    TextureBlender.BlendMode.AlphaWeighted);

// Each texture's contribution depends on its alpha
```

**Characteristics:**
- Respects texture alpha channels
- Smooth blending transitions
- Most versatile mode
- Natural-looking results
- Weights combined with alpha

**Visual Effect:**
- Smooth layering
- Transparency-aware
- Like Photoshop "Normal" blend mode

---

### Multiplicative Mode

**Formula:** `result *= lerp(white, texture, weight)`

**Performance:** Similar to AlphaWeighted

**Use Cases:**
- Masking operations
- Darkening effects
- Occlusion map combination
- Shadow accumulation
- Detail multiplication

**Example:**
```csharp
// Apply multiple occlusion masks
RenderTexture occlusion = blender.BlendTextures(
    occlusionMaps,
    strengths,
    TextureBlender.BlendMode.Multiplicative);

// Each mask darkens the result
```

**Characteristics:**
- Colors multiply together
- Darkens the image
- Can never exceed original brightness
- Good for detail preservation
- Useful for shadow/occlusion

**Visual Effect:**
- Darkens output
- Colors become more saturated
- Like Photoshop "Multiply" blend mode

---

## Performance Comparison

| Blend Mode      | Relative Speed | GPU Operations |
|----------------|----------------|----------------|
| Additive       | 100%           | Simplest       |
| AlphaWeighted  | ~77%           | Medium         |
| Multiplicative | ~77%           | Medium         |

**Recommendation:** Use Additive when possible for best VR performance.

---

## Weight Handling

### Equal Weights (null)

When `weights = null`, behavior depends on mode:

**Additive:**
```csharp
// Each texture weighted equally
weight[i] = 1.0 / textureCount
```

**AlphaWeighted:**
```csharp
// Each texture weighted as 1.0, alpha modulates
weight[i] = 1.0
```

**Multiplicative:**
```csharp
// Each texture weighted equally
weight[i] = 1.0 / textureCount
```

### Custom Weights

**Additive & AlphaWeighted:**
- Weights are normalized to sum to 1.0
- Example: `[2, 1, 1]` becomes `[0.5, 0.25, 0.25]`

**Multiplicative:**
- Weights control lerp strength
- `weight=0`: no effect (white)
- `weight=1`: full multiplication

---

## Choosing the Right Mode

### Decision Tree

```
Do you need alpha blending?
├─ NO → Use ADDITIVE (fastest)
│   ├─ Adding light/glow? → ADDITIVE ✓
│   └─ HDR accumulation? → ADDITIVE ✓
│
└─ YES → Check use case
    ├─ Smooth transitions? → ALPHAWEIGHTED ✓
    ├─ Terrain splatting? → ALPHAWEIGHTED ✓
    ├─ Darkening/masking? → MULTIPLICATIVE ✓
    └─ Occlusion maps? → MULTIPLICATIVE ✓
```

### Quick Reference

| Scenario                    | Recommended Mode |
|-----------------------------|------------------|
| Light maps                  | Additive         |
| Terrain splatting           | AlphaWeighted    |
| Glow effects                | Additive         |
| Layer compositing           | AlphaWeighted    |
| Shadow maps                 | Multiplicative   |
| Occlusion maps              | Multiplicative   |
| HDR accumulation            | Additive         |
| Smooth transitions          | AlphaWeighted    |
| Detail multiplication       | Multiplicative   |
| VR (when alpha not needed)  | Additive         |

---

## Visual Examples

### Additive: Light Combination

```
Texture1 (Red light):    RGB(1, 0, 0) * weight(0.5) = (0.5, 0, 0)
Texture2 (Green light):  RGB(0, 1, 0) * weight(0.5) = (0, 0.5, 0)
Result:                  (0.5, 0, 0) + (0, 0.5, 0) = (0.5, 0.5, 0) [Yellow]
```

### AlphaWeighted: Terrain Blending

```
Texture1 (Grass):  RGB(0, 1, 0), A=0.8, weight=1.0
Texture2 (Dirt):   RGB(0.6, 0.4, 0.2), A=0.2, weight=1.0

Step 1: Start with Grass weighted by alpha
  result = (0, 1, 0) * (1.0 * 0.8) = (0, 0.8, 0)

Step 2: Blend in Dirt weighted by alpha
  result = lerp((0, 0.8, 0), (0.6, 0.4, 0.2), 1.0 * 0.2)
  result ≈ (0.12, 0.72, 0.04) [Mostly grass with dirt tint]
```

### Multiplicative: Shadow Darkening

```
Base texture:     RGB(1, 1, 1) [White]
Shadow1:          RGB(0.8, 0.8, 0.8), weight=1.0
Shadow2:          RGB(0.9, 0.9, 0.9), weight=1.0

Step 1: Apply Shadow1
  result = (1, 1, 1) * lerp((1, 1, 1), (0.8, 0.8, 0.8), 1.0)
  result = (1, 1, 1) * (0.8, 0.8, 0.8) = (0.8, 0.8, 0.8)

Step 2: Apply Shadow2
  result = (0.8, 0.8, 0.8) * lerp((1, 1, 1), (0.9, 0.9, 0.9), 1.0)
  result = (0.8, 0.8, 0.8) * (0.9, 0.9, 0.9) = (0.72, 0.72, 0.72)
```

---

## Normal Map Blending

For normal maps, use `BlendNormalsWithBaseAlpha()`:

```csharp
RenderTexture normalMap = blender.BlendNormalsWithBaseAlpha(
    normalTextures,
    baseTextures,    // Alpha channel weights each pixel
    weights,
    blendMode);
```

**How it works:**
- Each pixel's normal contribution is weighted by the base texture alpha at that pixel
- Allows for smooth transitions between different surface details
- Prevents normal map artifacts at blend boundaries

**Example use case:**
```csharp
// Terrain with grass, dirt, rock normal maps
// Base textures have alpha masks for each layer
RenderTexture terrainNormals = blender.BlendNormalsWithBaseAlpha(
    new[] { grassNormal, dirtNormal, rockNormal },
    new[] { grassBase, dirtBase, rockBase },
    new[] { 1f, 1f, 1f },
    BlendMode.AlphaWeighted);
```

---

## Advanced: Custom Blend Modes

To add custom blend modes, modify the compute shader:

1. Add new kernel in `TextureBlenderComputeShader.compute`
2. Add kernel ID field in `TextureBlender.cs`
3. Cache kernel in `Initialize()`
4. Add enum value to `BlendMode`
5. Update `GetKernelForBlendMode()`

**Example compute shader kernel:**
```hlsl
[numthreads(8,8,1)]
void BlendTexturesArrayCustom(uint3 id : SV_DispatchThreadID)
{
    // Custom blending logic
    float4 result = float4(0, 0, 0, 0);
    
    for (int i = 0; i < TextureCount; i++)
    {
        float4 texColor = InputTexturesArray[uint3(id.xy, i)];
        float weight = BlendValues[i];
        
        // Your custom blend formula here
        result += CustomBlend(result, texColor, weight);
    }
    
    OutputTexture[id.xy] = result;
    OutputBuffer[id.y * TextureWidth + id.x] = result;
}
```

---

## Performance Tips by Mode

### Additive (Fastest)
- Best choice for VR
- No branching in shader
- Minimal GPU operations
- Use when alpha not needed

### AlphaWeighted
- Enable array caching for repeat blends
- Consider reducing resolution for VR
- Use texture pooling

### Multiplicative
- Same performance as AlphaWeighted
- Good for detail overlay
- Combine with Additive for base + details

### General
- Batch similar operations
- Reuse render targets with `BlendToExistingTexture()`
- Clear cache when switching texture sets

