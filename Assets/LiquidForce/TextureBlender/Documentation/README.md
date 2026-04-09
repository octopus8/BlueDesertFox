# TextureBlender System Documentation

## Overview

The TextureBlender system provides GPU-accelerated texture blending for Unity projects, removing the traditional 8-texture limitation and offering high-performance blend operations with multiple blend modes and resource pooling.

**Performance Target**: <5ms for 4×2048² textures on RTX 3070, <2ms for cached repeat blends

## ⚠️ Version 3.0 Breaking Changes

**If upgrading from v2.x:** The API has changed significantly. See [Migration Guide](MIGRATION_V2_TO_V3.md) for upgrade instructions.

**Key Changes:**
- `BlendTextures()` now takes `RenderTexture target` as **first parameter**
- `BlendToExistingTexture()` **removed** (use `BlendTextures(target, ...)` instead)
- `weights` parameter no longer has default value (pass `null` for equal weights)

**Quick Migration:**
```csharp
// OLD v2.x:
RenderTexture result = blender.BlendTextures(textures, weights);
blender.BlendToExistingTexture(existingRT, textures, weights);

// NEW v3.0:
RenderTexture result = blender.BlendTextures(null, textures, weights);
RenderTexture same = blender.BlendTextures(existingRT, textures, weights);
```

## Key Features

- ✅ **Unlimited Textures** - No hard limit (GPU-dependent, typically 2048)
- ✅ **Texture Rotation** - Per-texture rotation (0-360°) with zero-overhead optimization and automatic tiling
- ✅ **UV Offset** - Per-texture UV panning/shifting with zero-overhead optimization and automatic tiling
- ✅ **Multiple Blend Modes** - Additive, AlphaWeighted, Multiplicative
- ✅ **Resource Pooling** - Automatic RenderTexture and ComputeBuffer reuse
- ✅ **Texture Array Caching** - Major speedup for repeated blends
- ✅ **VR Compatible** - OpenGL ES 3.0 with automatic fallback (Quest/Pico) ✅ FIXED April 2026
- ✅ **Zero Memory Leaks** - Automatic resource management
- ✅ **Normal Map Support** - Per-pixel alpha-weighted normal blending with rotation and offset
- ✅ **Seamless Tiling** - Automatic UV wrapping during rotation and offset for continuous textures

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
        // Blend textures with equal weights (target=null creates new)
        RenderTexture result = blender.BlendTextures(null, textures, null);
        
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

| Configuration | Resolution | Target Time (RTX 3070) | Quest 2 (OpenGL ES) | Quest 3 (Vulkan) |
|---------------|------------|------------------------|---------------------|------------------|
| 4 textures    | 1024×1024  | <2ms                   | <5ms (+2-5ms copy)  | <2ms             |
| 4 textures    | 2048×2048  | <5ms                   | <10ms (+2-5ms copy) | <5ms             |
| 8 textures    | 2048×2048  | <8ms                   | <15ms (+2-5ms copy) | <8ms             |
| 4 cached      | 2048×2048  | <2ms                   | <7ms (+2-5ms copy)  | <2ms             |

**Note**: Quest 2 requires OpenGL ES 3.0 buffer copy (fixed April 2026). Quest 3/Pro/Pico 4+ recommended to use Vulkan for full performance.

## Version History

### Current Version (v3.0.1) - April 9, 2026

**Critical VR Fix:**
- ✅ **FIXED**: OpenGL ES 3.0 compatibility on Quest/Pico VR headsets
- **Issue**: RWTexture2D writes not supported on OpenGL ES 3.0 - textures were black/empty
- **Solution**: Automatic buffer-to-texture copy fallback detected at runtime
- **Performance Impact**: +2-5ms overhead on Quest/Pico (unavoidable due to GPU→CPU→GPU transfer)
- **Desktop Impact**: Zero overhead - fallback path never executes
- **Debug Flag**: Added `forceBufferCopyPath` to test fallback in Editor
- **Profiler Marker**: Added `TextureBlender.BufferCopy` for performance monitoring
- **Resource Management**: Temporary Texture2D pooling to minimize allocations

### Version v3.0.0 ⚠️ BREAKING CHANGES

**Major API Refactoring:**
- **BREAKING**: `BlendTextures()` now takes `RenderTexture target` as first parameter
- **BREAKING**: `BlendToExistingTexture()` removed (use `BlendTextures(target, ...)` instead)
- **BREAKING**: `weights` parameter no longer has default value in main overload
- **Benefit**: Unified API, single method for both create-new and blend-to-existing
- **Migration Required**: See [MIGRATION_V2_TO_V3.md](MIGRATION_V2_TO_V3.md)

**Other Improvements:**
- Architectural improvements: Texture array building encapsulated in TextureBlenderResources
- Simplified configuration: Texture2DArray caching always enabled (removed toggle)
- Better architecture: Resources class owns complete lifecycle
- Consistent API pattern: All GetOrCreate* methods follow same pattern

**Features (Unchanged from v2.1):**
- Per-texture rotation (0-360°) with zero-overhead optimization
- Per-texture UV offset with zero-overhead optimization and automatic tiling
- Seamless tiling during rotation and offset with Wrap sampler mode
- Normal map blending with per-pixel alpha weighting and rotation/offset support
- Enhanced resource pooling
- Texture array caching for repeat blends (35% speedup, always enabled)
- Multiple blend modes (Additive, AlphaWeighted, Multiplicative)
- VR optimization support

### Previous Versions
- **v2.1.1**: Texture array builder encapsulation (non-breaking)
- **v2.1.0**: Removed enableArrayCache toggle (non-breaking)
- **v2.0**: Initial rotation and offset support, normal map blending
- **v1.0**: Legacy `ImageProcessorTest.cs` (deprecated - 8-texture limit)

## Support

For questions or issues:
1. Check **[Troubleshooting Guide](TROUBLESHOOTING.md)**
2. Review **[Code Examples](CODE_EXAMPLES.md)**
3. Enable profiler markers to diagnose performance
4. Use `TextureBlenderBenchmark` component to validate setup

## License

Part of BlueDesertFox VR project.
