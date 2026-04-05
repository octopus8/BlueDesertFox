# TextureBlender Setup Guide

Complete setup and configuration guide for the TextureBlender system.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Initial Setup](#initial-setup)
3. [Inspector Configuration](#inspector-configuration)
4. [Project Settings](#project-settings)
5. [First Blend](#first-blend)
6. [Platform-Specific Setup](#platform-specific-setup)
7. [Optimization Configuration](#optimization-configuration)
8. [Testing Setup](#testing-setup)

---

## Prerequisites

### Unity Version

- **Required:** Unity 6 (6000.3.10f1) or higher
- **Render Pipeline:** URP 17.3.0 or higher

### Required Packages

Install via Package Manager:

1. **UniTask** (Cysharp.Threading.Tasks)
   - Window → Package Manager
   - Add package from git URL: `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`

2. **Universal RP** (if not already installed)
   - Window → Package Manager → Unity Registry
   - Search: "Universal RP"
   - Install version 17.3.0+

### Platform Support

| Platform | Supported | Notes |
|----------|-----------|-------|
| Windows  | ✅ Yes    | All features |
| Mac      | ✅ Yes    | All features |
| Linux    | ✅ Yes    | All features |
| Quest 2  | ✅ Yes    | Reduce resolution |
| Quest Pro| ✅ Yes    | Full performance |
| PCVR     | ✅ Yes    | All features |
| WebGL    | ❌ No     | No compute shader support |
| iOS      | ⚠️ Limited| Requires Metal |
| Android  | ⚠️ Limited| Requires Vulkan |

---

## Initial Setup

### Step 1: Verify Files

Ensure these files exist in `Assets/LiquidForce/TextureBlender/`:

```
TextureBlender/
├── TextureBlender.cs
├── TextureBlenderResources.cs
├── TextureArrayBuilder.cs
├── TextureBlenderComputeShader.compute
└── Documentation/ (this folder)
```

### Step 2: Create TextureBlender GameObject

**Option A: Manual Setup**
1. Create empty GameObject: `GameObject → Create Empty`
2. Name it: "TextureBlender"
3. Add component: `Add Component → TextureBlender`

**Option B: From Prefab (if available)**
1. Drag TextureBlender prefab into scene
2. Position anywhere (doesn't need to be visible)

### Step 3: Assign Compute Shader

1. Select TextureBlender GameObject
2. In Inspector, find "Image Processor Shader" field
3. Click the circle icon → Search: "TextureBlenderComputeShader"
4. Double-click to assign

**Verification:**
- Field should show "TextureBlenderComputeShader"
- No error in Console

### Step 4: Configure Basic Settings

**Recommended Initial Settings:**

```
Shader Configuration:
  Image Processor Shader: TextureBlenderComputeShader ✓

Default Output Settings:
  Default Output Width: 2048
  Default Output Height: 2048
  Output Format: ARGB32

Performance Settings:
  Use Texture Pooling: ✓
  Max Pooled Textures: 5
  Enable Array Cache: ✓
  Fast Mode: ☐ (leave unchecked initially)
```

---

## Inspector Configuration

### Shader Configuration

**Image Processor Shader**
- **Required:** Must be assigned
- **Value:** TextureBlenderComputeShader.compute
- **Purpose:** Contains GPU blend kernels

### Default Output Settings

**Default Output Width / Height**
- **Range:** 256 - 4096
- **Default:** 2048
- **VR Recommendation:** 1024
- **Desktop:** 2048 or higher
- **Purpose:** Size of created RenderTextures

**Output Format**
- **Default:** ARGB32
- **Options:**
  - ARGB32: Standard, 32-bit color
  - ARGBFloat: HDR, 128-bit color
  - ARGBHalf: HDR, 64-bit color
- **Recommendation:** ARGB32 for most cases

### Performance Settings

**Use Texture Pooling**
- **Default:** ✓ Enabled
- **When to disable:** Never (always keep enabled)
- **Benefit:** Saves 0.5-1ms per blend
- **Memory:** Pools up to Max Pooled Textures

**Max Pooled Textures**
- **Default:** 5
- **Range:** 1 - 20
- **Low Memory:** 3
- **Standard:** 5
- **High Performance:** 10
- **Memory per texture:** ~16MB for 2048×2048 ARGB32

**Enable Array Cache**
- **Default:** ✓ Enabled
- **When to disable:** If textures change frequently
- **Benefit:** Saves 1-2ms on repeat blends
- **Memory:** ~64MB per unique texture set

**Fast Mode**
- **Default:** ☐ Disabled
- **When to enable:** Only if inputs are validated elsewhere
- **Benefit:** Skips null checks (~0.1ms)
- **Risk:** Crashes if given invalid inputs

---

## Project Settings

### Graphics Settings

**For Desktop:**
1. Edit → Project Settings → Graphics
2. Scriptable Render Pipeline: UniversalRenderPipelineAsset
3. Quality → Rendering → Render Scale: 1.0

**For VR:**
1. Edit → Project Settings → XR Plugin Management
2. Enable OpenXR
3. OpenXR → Render Mode: Single Pass Instanced
4. Quality → Rendering → Render Scale: 1.0 - 1.2

### Quality Settings

**Recommended for Blending:**
1. Edit → Project Settings → Quality
2. V Sync Count: Don't Sync (let XR handle it)
3. Anisotropic Textures: Per Texture
4. Texture Quality: Full Res

### Player Settings

**VR Specific:**
1. Edit → Project Settings → Player
2. Color Space: Linear
3. Auto Graphics API: ✓ (or OpenGLES3+ for Quest)
4. Multithreaded Rendering: ✓

---

## First Blend

### Create Test Script

Create `TestTextureBlender.cs`:

```csharp
using UnityEngine;

public class TestTextureBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture texture1;
    [SerializeField] private Texture texture2;
    [SerializeField] private Texture texture3;
    [SerializeField] private MeshRenderer targetRenderer;
    
    void Start()
    {
        Debug.Log("=== TextureBlender Test ===");
        
        // Create texture array
        Texture[] textures = { texture1, texture2, texture3 };
        
        // Measure performance
        var startTime = Time.realtimeSinceStartup;
        
        // Blend textures
        RenderTexture result = blender.BlendTextures(textures);
        
        var blendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        // Check result
        if (result == null)
        {
            Debug.LogError("Blend failed - returned null!");
            return;
        }
        
        Debug.Log($"✓ Blend successful!");
        Debug.Log($"  Resolution: {result.width}×{result.height}");
        Debug.Log($"  Time: {blendTime:F2}ms");
        
        // Apply to renderer
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = result;
            Debug.Log("✓ Applied to material");
        }
    }
    
    void OnDestroy()
    {
        // Clean up
        if (targetRenderer != null && 
            targetRenderer.material.mainTexture is RenderTexture rt)
        {
            blender.ReturnTexture(rt);
            Debug.Log("✓ Texture returned to pool");
        }
    }
}
```

### Setup Test Scene

1. Create new scene: `File → New Scene`
2. Add TextureBlender GameObject (from Step 2)
3. Create test GameObject:
   - `GameObject → 3D Object → Cube`
   - Add `TestTextureBlender` script
4. Assign references in Inspector:
   - Blender: TextureBlender GameObject
   - Texture1/2/3: Any textures from project
   - Target Renderer: Cube's MeshRenderer
5. Press Play

### Expected Results

**Console Output:**
```
=== TextureBlender Test ===
✓ Blend successful!
  Resolution: 2048×2048
  Time: 4.23ms
✓ Applied to material
```

**Visual:**
- Cube should show blended texture
- Colors should mix from all 3 inputs

**If Errors:**
- See [Troubleshooting Guide](TROUBLESHOOTING.md)
- Check Console for specific error messages

---

## Platform-Specific Setup

### Quest 2 Standalone

**Settings:**
```
Default Output Width: 1024
Default Output Height: 1024
Max Pooled Textures: 3
Fast Mode: ✓
```

**Player Settings:**
1. Android Platform selected
2. Graphics API: OpenGLES3, Vulkan
3. Install Location: Auto
4. Minimum API Level: Android 10.0

**Test on Device:**
1. Build and Run to Quest 2
2. Expect: <3ms blend time
3. Monitor: No frame drops

### PCVR (Quest Link / AirLink)

**Settings:**
```
Default Output Width: 2048
Default Output Height: 2048
Max Pooled Textures: 5
Fast Mode: ☐
```

**Player Settings:**
1. Windows Platform selected
2. Graphics API: DirectX11 or DirectX12
3. VR Support: OpenXR

**Link Settings:**
1. Oculus App → Settings → Beta
2. Render Resolution: 1.0x
3. Bitrate: Automatic

### Desktop Non-VR

**Settings:**
```
Default Output Width: 2048 or 4096
Default Output Height: 2048 or 4096
Max Pooled Textures: 10
Fast Mode: ☐
```

**Quality Settings:**
1. Anti-aliasing: 4x or 8x
2. Texture Resolution: Full
3. Shadow Quality: High

---

## Optimization Configuration

### For Maximum Performance (VR)

```csharp
// Inspector:
Default Output Width: 1024
Default Output Height: 1024
Use Texture Pooling: ✓
Max Pooled Textures: 3
Enable Array Cache: ✓
Fast Mode: ✓

// Code:
var result = blender.BlendTextures(
    textures,
    weights,
    TextureBlender.BlendMode.Additive);  // Fastest mode
```

**Expected:** <2ms per blend on RTX 3070

### For Maximum Quality (Desktop)

```csharp
// Inspector:
Default Output Width: 4096
Default Output Height: 4096
Output Format: ARGBFloat  // HDR
Use Texture Pooling: ✓
Max Pooled Textures: 10
Enable Array Cache: ✓
Fast Mode: ☐

// Code:
var result = blender.BlendTextures(
    textures,
    weights,
    TextureBlender.BlendMode.AlphaWeighted);  // Quality mode
```

**Expected:** <10ms per blend on RTX 3070

### For Balanced (Standard Desktop)

```csharp
// Inspector:
Default Output Width: 2048
Default Output Height: 2048
Use Texture Pooling: ✓
Max Pooled Textures: 5
Enable Array Cache: ✓
Fast Mode: ☐

// Code:
var result = blender.BlendTextures(
    textures,
    weights,
    TextureBlender.BlendMode.AlphaWeighted);
```

**Expected:** <5ms per blend on RTX 3070

---

## Testing Setup

### Performance Test

Add `TextureBlenderExample` component:
1. Add component to any GameObject
2. Assign TextureBlender reference
3. Add texture layers
4. Right-click component → "Run Performance Test"
5. Check Console for results

**Expected Output:**
```
First blend (uncached) - Total: 4.52ms
Second blend (cached) - Total: 2.13ms
Speedup - Total: 2.12x
```

### Validation Test

Create validation script:

```csharp
[ContextMenu("Validate TextureBlender")]
void ValidateSetup()
{
    var blender = FindObjectOfType<TextureBlender>();
    
    if (blender == null)
    {
        Debug.LogError("❌ No TextureBlender in scene!");
        return;
    }
    
    Debug.Log("✓ TextureBlender found");
    
    // Test blend
    var tex = Texture2D.whiteTexture;
    var result = blender.BlendTextures(new[] { tex, tex });
    
    if (result == null)
    {
        Debug.LogError("❌ Blend failed!");
    }
    else
    {
        Debug.Log($"✓ Blend succeeded: {result.width}×{result.height}");
        blender.ReturnTexture(result);
    }
}
```

---

## Advanced Setup

### Multiple Blenders

For parallel blending operations:

```csharp
// Create multiple TextureBlender instances
GameObject blender1 = new GameObject("TextureBlender1");
blender1.AddComponent<TextureBlender>();

GameObject blender2 = new GameObject("TextureBlender2");
blender2.AddComponent<TextureBlender>();

// Each has independent pools and caches
// Use for different texture resolutions
```

### Custom Prewarming

Optimize startup time:

```csharp
void Awake()
{
    var blender = GetComponent<TextureBlender>();
    
    // Manually trigger initialization
    // (normally happens automatically)
    
    // Blend a dummy texture to prewarm caches
    var dummy = Texture2D.whiteTexture;
    var warmup = blender.BlendTextures(new[] { dummy });
    blender.ReturnTexture(warmup);
    
    Debug.Log("TextureBlender prewarmed");
}
```

### Shared Resources

For memory-constrained platforms:

```csharp
// Option 1: Singleton pattern
public class TextureBlenderManager : MonoBehaviour
{
    public static TextureBlender Instance { get; private set; }
    
    void Awake()
    {
        Instance = GetComponent<TextureBlender>();
    }
}

// Usage from anywhere:
var result = TextureBlenderManager.Instance.BlendTextures(textures);

// Option 2: Service locator
// Option 3: Dependency injection
```

---

## Troubleshooting Setup

### Issue: Can't Find TextureBlender Script

**Solution:**
1. Check Scripts folder structure
2. Reimport: Right-click folder → Reimport
3. Check Console for compile errors

### Issue: Compute Shader Won't Assign

**Solution:**
1. Verify .compute file exists
2. Check file isn't corrupted
3. Reimport compute shader
4. Restart Unity Editor

### Issue: UniTask Not Found

**Solution:**
1. Install UniTask package (see Prerequisites)
2. Window → Package Manager
3. Add from git URL
4. Restart Unity if needed

### Issue: Poor Performance After Setup

**Solution:**
1. Enable Texture Pooling
2. Enable Array Cache
3. Check output resolution (lower for VR)
4. Run Performance Test to benchmark

---

## Next Steps

After successful setup:

1. **Read API Reference:** [API_REFERENCE.md](API_REFERENCE.md)
2. **Study Examples:** [CODE_EXAMPLES.md](CODE_EXAMPLES.md)
3. **Learn Blend Modes:** [BLEND_MODES.md](BLEND_MODES.md)
4. **Optimize Performance:** [PERFORMANCE_GUIDE.md](PERFORMANCE_GUIDE.md)
5. **Integrate into Project:** Use in your systems

---

## Checklist

Before considering setup complete:

- [ ] TextureBlender GameObject in scene
- [ ] Compute shader assigned in Inspector
- [ ] Basic settings configured
- [ ] Test blend executed successfully
- [ ] Performance measured (<5ms on desktop)
- [ ] Texture pooling enabled
- [ ] Array caching enabled
- [ ] First integration working
- [ ] Platform-specific optimizations applied
- [ ] Documentation reviewed

---

## Support

If setup fails:
1. Check [Troubleshooting Guide](TROUBLESHOOTING.md)
2. Verify all Prerequisites met
3. Test with minimal example
4. Check Unity Console for errors
5. Review platform-specific requirements

