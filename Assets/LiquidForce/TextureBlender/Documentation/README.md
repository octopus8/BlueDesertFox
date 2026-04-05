# TextureBlender System Documentation

## Overview

The TextureBlender system provides GPU-accelerated texture blending for Unity projects, removing the traditional 8-texture limitation and offering high-performance blend operations with multiple blend modes and resource pooling.

**Performance Target**: <5ms for 4×2048² textures on RTX 3070, <2ms for cached repeat blends

## Key Features

- ✅ **Unlimited Textures** - No hard limit (GPU-dependent, typically 2048)
- ✅ **Multiple Blend Modes** - Additive, AlphaWeighted, Multiplicative
- ✅ **Resource Pooling** - Automatic RenderTexture and ComputeBuffer reuse
- ✅ **Texture Array Caching** - Major speedup for repeated blends
- ✅ **Async Support** - Non-blocking operations with UniTask
- ✅ **VR Compatible** - OpenGL ES 3.0 support
- ✅ **Zero Memory Leaks** - Automatic resource management
- ✅ **Normal Map Support** - Per-pixel alpha-weighted normal blending

## Documentation Files

### Getting Started
- **[Quick Start Guide](QUICK_START.md)** - Get up and running in 5 minutes
- **[Setup Guide](SETUP_GUIDE.md)** - Detailed setup and configuration

### Core Documentation
- **[API Reference](API_REFERENCE.md)** - Complete API documentation
- **[Blend Modes](BLEND_MODES.md)** - Understanding different blend modes
- **[Performance Guide](PERFORMANCE_GUIDE.md)** - Optimization and profiling

### Advanced Topics
- **[Architecture](ARCHITECTURE.md)** - System design and components
- **[Code Examples](CODE_EXAMPLES.md)** - Common usage patterns
- **[Troubleshooting](TROUBLESHOOTING.md)** - Common issues and solutions

## Quick Example

```csharp
using UnityEngine;

public class SimpleBlend : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    [SerializeField] private MeshRenderer target;
    
    void Start()
    {
        // Blend textures with equal weights
        RenderTexture result = blender.BlendTextures(textures);
        
        // Apply to material
        target.material.mainTexture = result;
    }
}
```

## System Requirements

- Unity 6 (6000.3.10f1) or higher
- URP 17.3.0 or higher
- UniTask package (Cysharp.Threading.Tasks)
- GPU with compute shader support
- OpenGL ES 3.0+ for VR

## Performance Targets

| Configuration | Resolution | Target Time (RTX 3070) | VR Target (Quest 2) |
|---------------|------------|------------------------|---------------------|
| 4 textures    | 1024×1024  | <2ms                   | <3ms                |
| 4 textures    | 2048×2048  | <5ms                   | <8ms                |
| 8 textures    | 2048×2048  | <8ms                   | <12ms               |
| 4 cached      | 2048×2048  | <2ms                   | <2ms                |

## Version History

### Current Version
- Normal map blending with per-pixel alpha weighting
- Enhanced resource pooling
- Texture array caching for repeat blends
- Multiple blend modes (Additive, AlphaWeighted, Multiplicative)
- VR optimization support

### Legacy
- `ImageProcessorTest.cs` - Deprecated (8-texture limit), marked with `[Obsolete]` attribute

## Support

For questions or issues:
1. Check **[Troubleshooting Guide](TROUBLESHOOTING.md)**
2. Review **[Code Examples](CODE_EXAMPLES.md)**
3. Enable profiler markers to diagnose performance
4. Use `TextureBlenderBenchmark` component to validate setup

## License

Part of BlueDesertFox VR project.

