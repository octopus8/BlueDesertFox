# Texture Blending System - Quick Reference

## Setup (30 seconds)

1. Create GameObject: "TextureBlender"
2. Add `TextureBlender` component
3. Assign `ImageProcessorEnhanced.compute` shader (in Inspector)
4. Done!

## Basic Usage

```csharp
// Get reference
[SerializeField] private TextureBlender blender;

// Blend textures
RenderTexture result = blender.BlendTextures(myTextures);

// Apply to material
material.mainTexture = result;
```

## Common Patterns

### Pattern 1: Simple Blend (Equal Weights)
```csharp
RenderTexture result = blender.BlendTextures(textures);
```

### Pattern 2: Custom Weights
```csharp
float[] weights = { 0.5f, 0.3f, 0.2f };
RenderTexture result = blender.BlendTextures(textures, weights);
```

### Pattern 3: Different Blend Mode
```csharp
// Additive (fastest - 30% faster than alpha)
RenderTexture result = blender.BlendTextures(textures, null, BlendMode.Additive);

// Multiplicative (masking/darkening)
RenderTexture result = blender.BlendTextures(textures, null, BlendMode.Multiplicative);
```

### Pattern 4: Async (Non-Blocking)
```csharp
private async void Start()
{
    RenderTexture result = await blender.BlendTexturesAsync(textures);
    material.mainTexture = result;
}
```

### Pattern 5: Reuse RenderTexture (Fastest)
```csharp
// Create once
RenderTexture target = new RenderTexture(2048, 2048, 0);
target.enableRandomWrite = true;
target.Create();

// Blend multiple times (no allocation)
blender.BlendToExistingTexture(target, textures1, weights1);
// ... use ...
blender.BlendToExistingTexture(target, textures2, weights2);
// ... use ...
```

### Pattern 6: Cleanup
```csharp
// Return to pool for reuse
blender.ReturnTexture(result);

// Or release manually
result.Release();
```

## Performance Tips

⚡ **Enable Array Cache** - 1-2ms speedup for repeat blends
⚡ **Use Texture Pooling** - 0.5-1ms speedup (avoid allocation)
⚡ **BlendToExistingTexture()** - Fastest when updating frequently
⚡ **Additive Mode** - 30% faster than AlphaWeighted
⚡ **Lower Resolution for VR** - Use 1024×1024 or smaller
⚡ **Fast Mode** - Skip validation for trusted inputs

## Inspector Settings

### Recommended Settings (High Performance)
- ✅ Use Texture Pooling
- ✅ Enable Array Cache
- Max Pooled Textures: 5-10
- □ Fast Mode (enable after validation)

### VR Settings
- Default Output Width/Height: 1024
- ✅ Use Texture Pooling
- ✅ Enable Array Cache
- ✅ Fast Mode

## Performance Targets

| Use Case | Target |
|----------|--------|
| VR (4 textures @ 1024) | <3ms |
| Desktop (4 textures @ 2048) | <5ms |
| Cached repeat blend | <2ms |

## Blend Modes Cheat Sheet

| Mode | Speed | Use Case |
|------|-------|----------|
| **Additive** | ⚡⚡⚡ Fastest | Light maps, glow, HDR |
| **AlphaWeighted** | ⚡⚡ Standard | Terrain, smooth transitions |
| **Multiplicative** | ⚡⚡ Standard | Masks, occlusion, darkening |

## Troubleshooting

**Problem**: Slow first blend
**Solution**: Normal - includes array conversion (~1-2ms). Repeat blends use cache.

**Problem**: Memory leak
**Solution**: Call `blender.ReturnTexture(result)` when done.

**Problem**: Wrong colors
**Solution**: Check all textures assigned (nulls become transparent black).

**Problem**: Performance worse than expected
**Solution**: Enable Array Cache + Texture Pooling, use Fast Mode, check Profiler.

## Profiler Markers

Look for these in Unity Profiler:
- `TextureBlender.ConvertToArray` - Array conversion time
- `TextureBlender.Dispatch` - GPU execution time
- `TextureBlender.AllocateResources` - Resource allocation
- `TextureBlender.CacheCheck` - Cache lookup

## Examples

See these files for complete examples:
- `TextureBlenderExample.cs` - Usage patterns
- `TextureBlenderBenchmark.cs` - Performance testing

## API Quick Reference

```csharp
// Main methods
RenderTexture BlendTextures(Texture[], float[], BlendMode)
UniTask<RenderTexture> BlendTexturesAsync(Texture[], float[], BlendMode, CancellationToken)
void BlendToExistingTexture(RenderTexture, Texture[], float[], BlendMode)
RenderTexture[] BatchBlend(BlendRequest[])

// Resource management
void ReturnTexture(RenderTexture)
void ClearCache()

// Enums
BlendMode.Additive
BlendMode.AlphaWeighted
BlendMode.Multiplicative
```

## Complete Documentation

See `TEXTURE_BLENDING_SYSTEM.md` for:
- Full API reference
- Advanced usage patterns
- Performance optimization guide
- Migration from ImageProcessorTest

