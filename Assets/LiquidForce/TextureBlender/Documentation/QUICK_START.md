# TextureBlender Quick Start Guide

Get up and running with TextureBlender in 5 minutes.

## Step 1: Add Component to Scene

1. Create empty GameObject in your scene
2. Name it "TextureBlender"
3. Add `TextureBlender` component

## Step 2: Assign Compute Shader

1. Select the TextureBlender GameObject
2. In Inspector, find "Image Processor Shader" field
3. Assign `TextureBlenderComputeShader.compute` from `Assets/LiquidForce/TextureBlender/`

## Step 3: Configure Settings

### Default Output Settings
- **Output Width**: 2048 (adjust for your needs)
- **Output Height**: 2048
- **Output Format**: ARGB32

### Performance Settings
- ✓ **Use Texture Pooling** (recommended)
- **Max Pooled Textures**: 5
- ✓ **Enable Array Cache** (recommended)
- ☐ **Fast Mode** (only if inputs validated)

## Step 4: Basic Usage

### Option A: Simple Script

Create a script that uses TextureBlender:

```csharp
using UnityEngine;

public class MyTextureBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] texturesToBlend;
    [SerializeField] private MeshRenderer targetRenderer;
    
    void Start()
    {
        // Blend with equal weights
        RenderTexture result = blender.BlendTextures(texturesToBlend);
        
        // Apply to material
        targetRenderer.material.mainTexture = result;
    }
}
```

### Option B: Use Example Component

1. Add `TextureBlenderExample` component to any GameObject
2. Assign TextureBlender reference
3. Add texture layers in Inspector
4. Assign target renderer
5. Press Play

## Step 5: Test Performance

Use the context menu on `TextureBlenderExample`:
- Right-click component → "Run Performance Test"
- Check Console for blend times
- First blend includes array conversion
- Second blend uses cache (faster)

## Common First-Time Issues

### Issue: "ImageProcessorShader is not assigned"
**Solution**: Assign `TextureBlenderComputeShader.compute` in Inspector

### Issue: Slow performance
**Solution**: 
1. Enable "Use Texture Pooling"
2. Enable "Enable Array Cache"
3. Reduce output resolution for VR (1024×1024)

### Issue: Memory leaks
**Solution**: Call `blender.ReturnTexture(result)` when done with blended texture

## Next Steps

- Read **[API Reference](API_REFERENCE.md)** for all methods
- Check **[Code Examples](CODE_EXAMPLES.md)** for advanced patterns
- Review **[Performance Guide](PERFORMANCE_GUIDE.md)** for optimization
- Learn about **[Blend Modes](BLEND_MODES.md)** for different effects

## Quick Reference: Key Methods

```csharp
// Simple blend
RenderTexture result = blender.BlendTextures(textures);

// Custom weights
float[] weights = { 0.5f, 0.3f, 0.2f };
RenderTexture result = blender.BlendTextures(textures, weights);

// With texture rotation (0-360 degrees)
float[] rotations = { 0f, 45f, 90f };
RenderTexture result = blender.BlendTextures(textures, weights, rotations);

// Different blend mode
RenderTexture result = blender.BlendTextures(
    textures, 
    weights, 
    TextureBlender.BlendMode.Additive);

// Async blend
RenderTexture result = await blender.BlendTexturesAsync(
    textures, 
    weights, 
    BlendMode.AlphaWeighted,
    cancellationToken);

// Blend to existing texture (fastest)
blender.BlendToExistingTexture(existingRT, textures, weights);

// Blend normal maps with per-pixel alpha (IMPORTANT: Use same rotations!)
float[] rotations = { 0f, 45f, 90f };
RenderTexture baseResult = blender.BlendTextures(baseTextures, weights, rotations);
RenderTexture normalResult = blender.BlendNormalsWithBaseAlpha(
    normalTextures,
    baseTextures,
    weights,
    rotations);  // Same rotations for visual coherence!

// Return to pool when done
blender.ReturnTexture(result);
```

## VR Optimization Quick Setup

For VR applications:

1. Set output resolution to **1024×1024**
2. Enable **Use Texture Pooling**
3. Enable **Enable Array Cache**
4. Enable **Fast Mode** (if inputs validated)
5. Use **BlendMode.Additive** when possible (30% faster)

Target: <3ms per blend on Quest 2

