# TextureBlender Code Examples

Practical code examples demonstrating common usage patterns.

## Table of Contents

1. [Basic Blending](#basic-blending)
2. [Custom Weights](#custom-weights)
3. [Texture Rotation](#texture-rotation)
4. [Async Operations](#async-operations)
5. [Terrain Splatting](#terrain-splatting)
6. [Normal Map Blending](#normal-map-blending)
7. [Light Map Combination](#light-map-combination)
8. [Real-Time Updates](#real-time-updates)
9. [Batch Processing](#batch-processing)
10. [Resource Management](#resource-management)
11. [Advanced Patterns](#advanced-patterns)

---

## Basic Blending

### Simple Equal Weight Blend

```csharp
using UnityEngine;

public class SimpleBlend : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    [SerializeField] private MeshRenderer targetRenderer;
    
    void Start()
    {
        // Blend with equal weights (null = equal distribution)
        RenderTexture result = blender.BlendTextures(textures);
        
        // Apply to material
        targetRenderer.material.mainTexture = result;
    }
    
    void OnDestroy()
    {
        // Clean up
        if (targetRenderer.material.mainTexture is RenderTexture rt)
        {
            blender.ReturnTexture(rt);
        }
    }
}
```

---

## Custom Weights

### Weighted Blend with Sliders

```csharp
using UnityEngine;

public class WeightedBlend : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    [SerializeField] private MeshRenderer targetRenderer;
    
    [Header("Weights (will be normalized)")]
    [SerializeField, Range(0f, 1f)] private float weight1 = 0.5f;
    [SerializeField, Range(0f, 1f)] private float weight2 = 0.3f;
    [SerializeField, Range(0f, 1f)] private float weight3 = 0.2f;
    
    private RenderTexture currentResult;
    
    void Start()
    {
        UpdateBlend();
    }
    
    void Update()
    {
        // Update blend when weights change
        if (Input.GetKeyDown(KeyCode.Space))
        {
            UpdateBlend();
        }
    }
    
    void UpdateBlend()
    {
        float[] weights = { weight1, weight2, weight3 };
        
        // Return old result to pool
        if (currentResult != null)
        {
            blender.ReturnTexture(currentResult);
        }
        
        // Blend with custom weights
        currentResult = blender.BlendTextures(textures, weights);
        targetRenderer.material.mainTexture = currentResult;
    }
    
    void OnDestroy()
    {
        blender.ReturnTexture(currentResult);
    }
}
```

---

## Texture Rotation

Rotate individual textures before blending for natural variation and pattern breaking. **UV coordinates automatically tile/wrap during rotation**, ensuring seamless results for repeating textures like terrain, bricks, or architectural patterns.

**Key Features:**
- Per-texture rotation (0-360°)
- **Automatic tiling** - UV wrapping for out-of-bounds coordinates (no edge artifacts)
- Zero-overhead when rotation is not used (98% faster)
- Ideal for terrain and procedural texture generation

### Basic Rotation Example

```csharp
using UnityEngine;

public class RotatedBlend : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    [SerializeField] private MeshRenderer targetRenderer;
    
    [Header("Rotation Per Texture (0-360 degrees)")]
    [SerializeField, Range(0f, 360f)] private float rotation1 = 0f;
    [SerializeField, Range(0f, 360f)] private float rotation2 = 45f;
    [SerializeField, Range(0f, 360f)] private float rotation3 = 90f;
    
    private RenderTexture currentResult;
    
    void Start()
    {
        UpdateBlend();
    }
    
    void UpdateBlend()
    {
        float[] weights = { 0.5f, 0.3f, 0.2f };
        float[] rotations = { rotation1, rotation2, rotation3 };
        
        // Return old result to pool
        if (currentResult != null)
        {
            blender.ReturnTexture(currentResult);
        }
        
        // Blend with rotation
        currentResult = blender.BlendTextures(textures, weights, rotations);
        targetRenderer.material.mainTexture = currentResult;
    }
    
    void OnDestroy()
    {
        blender.ReturnTexture(currentResult);
    }
}
```

### Terrain Variation with Rotation

```csharp
using UnityEngine;

public class TerrainTextureVariation : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] groundTextures;
    [SerializeField] private MeshRenderer terrainRenderer;
    
    void Start()
    {
        // Create variation by rotating textures
        float[] weights = { 0.4f, 0.3f, 0.3f };
        
        // Random rotations for natural variation
        float[] rotations = {
            Random.Range(0f, 360f),
            Random.Range(0f, 360f),
            Random.Range(0f, 360f)
        };
        
        RenderTexture result = blender.BlendTextures(
            groundTextures, weights, rotations, 
            TextureBlender.BlendMode.AlphaWeighted);
        
        terrainRenderer.material.mainTexture = result;
    }
}
```

### Base + Normal Maps with Rotation (Visual Coherence)

```csharp
using UnityEngine;

public class CoherentNormalBlend : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] baseTextures;
    [SerializeField] private Texture[] normalTextures;
    [SerializeField] private MeshRenderer targetRenderer;
    
    void Start()
    {
        float[] weights = { 0.5f, 0.3f, 0.2f };
        
        // Define rotations - CRITICAL: Use same for base and normals!
        float[] rotations = { 0f, 45f, 90f };
        
        // Blend base textures with rotation
        RenderTexture baseResult = blender.BlendTextures(
            baseTextures, weights, rotations, 
            TextureBlender.BlendMode.AlphaWeighted);
        
        // Blend normals with SAME rotation for visual coherence
        RenderTexture normalResult = blender.BlendNormalsWithBaseAlpha(
            normalTextures, baseTextures, weights, rotations, 
            TextureBlender.BlendMode.AlphaWeighted);
        
        // Apply both to material
        Material mat = targetRenderer.material;
        mat.SetTexture("_BaseMap", baseResult);
        mat.SetTexture("_BumpMap", normalResult);
    }
}
```

### Zero-Overhead Optimization (No Rotation)

```csharp
using UnityEngine;

public class OptimalPerformance : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    [SerializeField] private MeshRenderer targetRenderer;
    
    void Start()
    {
        float[] weights = { 0.5f, 0.3f, 0.2f };
        
        // Method 1: Pass null for rotations (zero overhead)
        RenderTexture result1 = blender.BlendTextures(
            textures, weights, null, 
            TextureBlender.BlendMode.AlphaWeighted);
        
        // Method 2: Pass zero array (also optimized via cached zeros)
        float[] rotations = { 0f, 0f, 0f };
        RenderTexture result2 = blender.BlendTextures(
            textures, weights, rotations, 
            TextureBlender.BlendMode.AlphaWeighted);
        
        // Method 3: Use basic overload without rotation parameter (fastest)
        RenderTexture result3 = blender.BlendTextures(
            textures, weights, 
            TextureBlender.BlendMode.AlphaWeighted);
        
        // All three have identical performance (~0.001ms rotation overhead)
        targetRenderer.material.mainTexture = result3;
    }
}
```

---

## Async Operations

### Async Blend During Loading

```csharp
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class AsyncLoadingBlend : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    [SerializeField] private UnityEngine.UI.Image loadingImage;
    
    private CancellationTokenSource cts;
    
    private async void Start()
    {
        cts = new CancellationTokenSource();
        
        try
        {
            // Show loading indicator
            loadingImage.gameObject.SetActive(true);
            
            // Async blend (non-blocking)
            RenderTexture result = await blender.BlendTexturesAsync(
                textures,
                null,  // Equal weights
                TextureBlender.BlendMode.AlphaWeighted,
                cts.Token);
            
            // Convert to Sprite for UI
            Sprite sprite = RenderTextureToSprite(result);
            loadingImage.sprite = sprite;
            
            // Hide loading after brief delay
            await UniTask.Delay(1000, cancellationToken: cts.Token);
            loadingImage.gameObject.SetActive(false);
            
            blender.ReturnTexture(result);
        }
        catch (System.OperationCanceledException)
        {
            Debug.Log("Async blend cancelled");
        }
    }
    
    Sprite RenderTextureToSprite(RenderTexture rt)
    {
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
    }
    
    void OnDestroy()
    {
        cts?.Cancel();
        cts?.Dispose();
    }
}
```

---

## Terrain Splatting

### Dynamic Terrain Texture Blending

```csharp
using UnityEngine;

public class TerrainTextureBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private MeshRenderer terrainRenderer;
    
    [Header("Terrain Layers")]
    [SerializeField] private Texture grassTexture;
    [SerializeField] private Texture dirtTexture;
    [SerializeField] private Texture rockTexture;
    [SerializeField] private Texture snowTexture;
    
    [Header("Splat Weights")]
    [SerializeField, Range(0f, 1f)] private float grassWeight = 0.4f;
    [SerializeField, Range(0f, 1f)] private float dirtWeight = 0.3f;
    [SerializeField, Range(0f, 1f)] private float rockWeight = 0.2f;
    [SerializeField, Range(0f, 1f)] private float snowWeight = 0.1f;
    
    private RenderTexture terrainTexture;
    private Texture[] terrainLayers;
    private float[] splatWeights;
    
    void Start()
    {
        // Create persistent RenderTexture for terrain
        terrainTexture = new RenderTexture(2048, 2048, 0, RenderTextureFormat.ARGB32);
        terrainTexture.enableRandomWrite = true;
        terrainTexture.Create();
        
        // Setup arrays
        terrainLayers = new Texture[] { grassTexture, dirtTexture, rockTexture, snowTexture };
        splatWeights = new float[4];
        
        // Initial blend
        UpdateTerrainBlend();
        
        // Apply to material
        terrainRenderer.material.mainTexture = terrainTexture;
    }
    
    void Update()
    {
        // Update weights array
        splatWeights[0] = grassWeight;
        splatWeights[1] = dirtWeight;
        splatWeights[2] = rockWeight;
        splatWeights[3] = snowWeight;
        
        // Blend on demand (e.g., when player paints terrain)
        if (Input.GetMouseButton(0))
        {
            UpdateTerrainBlend();
        }
    }
    
    void UpdateTerrainBlend()
    {
        // Blend to existing texture (no allocation)
        blender.BlendToExistingTexture(
            terrainTexture,
            terrainLayers,
            splatWeights,
            TextureBlender.BlendMode.AlphaWeighted);
    }
    
    void OnDestroy()
    {
        if (terrainTexture != null)
        {
            terrainTexture.Release();
        }
    }
}
```

---

## Normal Map Blending

### Terrain Normal Maps with Per-Pixel Alpha

```csharp
using UnityEngine;

public class TerrainNormalBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private MeshRenderer terrainRenderer;
    
    [Header("Base Textures (with alpha masks)")]
    [SerializeField] private Texture grassBase;
    [SerializeField] private Texture dirtBase;
    [SerializeField] private Texture rockBase;
    
    [Header("Normal Maps")]
    [SerializeField] private Texture grassNormal;
    [SerializeField] private Texture dirtNormal;
    [SerializeField] private Texture rockNormal;
    
    [Header("Layer Weights")]
    [SerializeField, Range(0f, 1f)] private float grassWeight = 0.5f;
    [SerializeField, Range(0f, 1f)] private float dirtWeight = 0.3f;
    [SerializeField, Range(0f, 1f)] private float rockWeight = 0.2f;
    
    private RenderTexture baseTexture;
    private RenderTexture normalTexture;
    
    void Start()
    {
        // Create persistent textures
        baseTexture = CreateRenderTexture(2048, 2048);
        normalTexture = CreateRenderTexture(2048, 2048);
        
        UpdateTextures();
        
        // Apply to material
        terrainRenderer.material.SetTexture("_BaseMap", baseTexture);
        terrainRenderer.material.SetTexture("_BumpMap", normalTexture);
    }
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.U))
        {
            UpdateTextures();
        }
    }
    
    void UpdateTextures()
    {
        Texture[] baseTextures = { grassBase, dirtBase, rockBase };
        Texture[] normalTextures = { grassNormal, dirtNormal, rockNormal };
        float[] weights = { grassWeight, dirtWeight, rockWeight };
        
        // Blend base textures
        blender.BlendToExistingTexture(
            baseTexture,
            baseTextures,
            weights,
            TextureBlender.BlendMode.AlphaWeighted);
        
        // Blend normal maps with per-pixel alpha from base textures
        blender.BlendNormalsWithBaseAlphaToExistingTexture(
            normalTexture,
            normalTextures,
            baseTextures,
            weights,
            TextureBlender.BlendMode.AlphaWeighted);
        
        Debug.Log("Terrain textures updated");
    }
    
    RenderTexture CreateRenderTexture(int width, int height)
    {
        RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }
    
    void OnDestroy()
    {
        baseTexture?.Release();
        normalTexture?.Release();
    }
}
```

---

## Light Map Combination

### Additive Light Blending

```csharp
using UnityEngine;

public class LightMapBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private MeshRenderer targetRenderer;
    
    [Header("Light Maps")]
    [SerializeField] private Texture[] lightMaps;
    
    [Header("Light Intensities")]
    [SerializeField] private float[] intensities;
    
    private RenderTexture combinedLightMap;
    
    void Start()
    {
        combinedLightMap = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGBFloat);
        combinedLightMap.enableRandomWrite = true;
        combinedLightMap.Create();
        
        UpdateLightMap();
        
        targetRenderer.material.SetTexture("_EmissionMap", combinedLightMap);
        targetRenderer.material.EnableKeyword("_EMISSION");
    }
    
    void UpdateLightMap()
    {
        // Use Additive mode for light accumulation (fastest)
        blender.BlendToExistingTexture(
            combinedLightMap,
            lightMaps,
            intensities,
            TextureBlender.BlendMode.Additive);
    }
    
    public void SetLightIntensity(int index, float intensity)
    {
        if (index >= 0 && index < intensities.Length)
        {
            intensities[index] = intensity;
            UpdateLightMap();
        }
    }
    
    void OnDestroy()
    {
        combinedLightMap?.Release();
    }
}
```

---

## Real-Time Updates

### Interactive Blend Weight Control

```csharp
using UnityEngine;

public class InteractiveBlender : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private MeshRenderer targetRenderer;
    [SerializeField] private Texture[] textures;
    
    private RenderTexture result;
    private float[] weights;
    private bool needsUpdate = true;
    
    void Start()
    {
        weights = new float[textures.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = 1f / textures.Length;  // Equal weights initially
        }
        
        result = new RenderTexture(2048, 2048, 0);
        result.enableRandomWrite = true;
        result.Create();
        
        targetRenderer.material.mainTexture = result;
    }
    
    void Update()
    {
        // Adjust weights with number keys
        for (int i = 0; i < Mathf.Min(textures.Length, 9); i++)
        {
            if (Input.GetKey(KeyCode.Alpha1 + i))
            {
                weights[i] = Mathf.Clamp01(weights[i] + Time.deltaTime);
                needsUpdate = true;
            }
            else if (Input.GetKey(KeyCode.LeftShift) && Input.GetKey(KeyCode.Alpha1 + i))
            {
                weights[i] = Mathf.Clamp01(weights[i] - Time.deltaTime);
                needsUpdate = true;
            }
        }
        
        // Update only when changed (optimization)
        if (needsUpdate)
        {
            blender.BlendToExistingTexture(
                result,
                textures,
                weights,
                TextureBlender.BlendMode.AlphaWeighted);
            
            needsUpdate = false;
        }
    }
    
    void OnGUI()
    {
        for (int i = 0; i < weights.Length; i++)
        {
            GUI.Label(new Rect(10, 10 + i * 20, 200, 20), 
                $"Layer {i + 1}: {weights[i]:F2}");
        }
        
        GUI.Label(new Rect(10, 10 + weights.Length * 20, 400, 40),
            "Press 1-9 to increase weight\nShift+1-9 to decrease weight");
    }
    
    void OnDestroy()
    {
        result?.Release();
    }
}
```

---

## Batch Processing

### Process Multiple Material Sets

```csharp
using UnityEngine;

public class BatchBlendProcessor : MonoBehaviour
{
    [System.Serializable]
    public class MaterialSet
    {
        public string name;
        public Texture[] textures;
        public float[] weights;
        public MeshRenderer targetRenderer;
    }
    
    [SerializeField] private TextureBlender blender;
    [SerializeField] private MaterialSet[] materialSets;
    
    void Start()
    {
        ProcessAllMaterials();
    }
    
    [ContextMenu("Process All Materials")]
    void ProcessAllMaterials()
    {
        var startTime = Time.realtimeSinceStartup;
        
        // Create batch requests
        var requests = new TextureBlender.BlendRequest[materialSets.Length];
        
        for (int i = 0; i < materialSets.Length; i++)
        {
            requests[i] = new TextureBlender.BlendRequest
            {
                inputTextures = materialSets[i].textures,
                blendWeights = materialSets[i].weights,
                blendMode = TextureBlender.BlendMode.AlphaWeighted,
                outputWidth = 1024,
                outputHeight = 1024
            };
        }
        
        // Execute batch
        RenderTexture[] results = blender.BatchBlend(requests);
        
        // Apply results
        for (int i = 0; i < results.Length; i++)
        {
            if (materialSets[i].targetRenderer != null)
            {
                materialSets[i].targetRenderer.material.mainTexture = results[i];
            }
        }
        
        var processingTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        Debug.Log($"Processed {materialSets.Length} material sets in {processingTime:F2}ms");
    }
}
```

---

## Resource Management

### Proper Cleanup Pattern

```csharp
using UnityEngine;

public class ProperResourceManagement : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    
    private RenderTexture managedResult;
    
    void Start()
    {
        // Create and blend
        managedResult = blender.BlendTextures(textures);
    }
    
    void OnDisable()
    {
        // Return to pool when disabled
        if (managedResult != null)
        {
            blender.ReturnTexture(managedResult);
            managedResult = null;
        }
    }
    
    void OnEnable()
    {
        // Re-blend when enabled
        if (textures != null && textures.Length > 0)
        {
            managedResult = blender.BlendTextures(textures);
        }
    }
    
    void OnDestroy()
    {
        // Final cleanup
        if (managedResult != null)
        {
            blender.ReturnTexture(managedResult);
            managedResult = null;
        }
    }
}
```

---

## Advanced Patterns

### Cached Weight Manager

```csharp
using UnityEngine;
using System.Collections.Generic;

public class WeightManager : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    
    // Reusable weight arrays (zero GC)
    private Dictionary<int, float[]> weightCache = new Dictionary<int, float[]>();
    
    public RenderTexture BlendWithCachedWeights(
        Texture[] textures,
        params float[] weights)
    {
        int count = textures.Length;
        
        // Get or create cached weight array
        if (!weightCache.ContainsKey(count))
        {
            weightCache[count] = new float[count];
        }
        
        float[] cachedWeights = weightCache[count];
        
        // Copy weights into cached array
        for (int i = 0; i < count; i++)
        {
            cachedWeights[i] = i < weights.Length ? weights[i] : 0f;
        }
        
        // Blend using cached array (no allocation)
        return blender.BlendTextures(textures, cachedWeights);
    }
}
```

### Blend Mode Selector

```csharp
using UnityEngine;

public class BlendModeSelector : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    [SerializeField] private MeshRenderer targetRenderer;
    
    private TextureBlender.BlendMode currentMode = TextureBlender.BlendMode.AlphaWeighted;
    private RenderTexture currentResult;
    
    void Update()
    {
        // Cycle through modes with Tab
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CycleBlendMode();
            UpdateBlend();
        }
    }
    
    void CycleBlendMode()
    {
        currentMode = (TextureBlender.BlendMode)(((int)currentMode + 1) % 3);
        Debug.Log($"Blend mode: {currentMode}");
    }
    
    void UpdateBlend()
    {
        // Return old result
        if (currentResult != null)
        {
            blender.ReturnTexture(currentResult);
        }
        
        // Blend with new mode
        currentResult = blender.BlendTextures(textures, null, currentMode);
        targetRenderer.material.mainTexture = currentResult;
    }
    
    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 20), 
            $"Current Mode: {currentMode} (Press Tab to cycle)");
    }
    
    void OnDestroy()
    {
        blender.ReturnTexture(currentResult);
    }
}
```

### Performance Monitoring

```csharp
using UnityEngine;
using System.Collections.Generic;

public class BlendPerformanceMonitor : MonoBehaviour
{
    [SerializeField] private TextureBlender blender;
    [SerializeField] private Texture[] textures;
    
    private Queue<float> recentBlendTimes = new Queue<float>(60);
    private float averageBlendTime;
    
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
        {
            MeasureBlendPerformance();
        }
    }
    
    void MeasureBlendPerformance()
    {
        var startTime = Time.realtimeSinceStartup;
        
        var result = blender.BlendTextures(textures);
        
        var blendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        // Track recent times
        recentBlendTimes.Enqueue(blendTime);
        if (recentBlendTimes.Count > 60)
        {
            recentBlendTimes.Dequeue();
        }
        
        // Calculate average
        float sum = 0;
        foreach (var time in recentBlendTimes)
        {
            sum += time;
        }
        averageBlendTime = sum / recentBlendTimes.Count;
        
        Debug.Log($"Blend time: {blendTime:F2}ms (avg: {averageBlendTime:F2}ms)");
        
        blender.ReturnTexture(result);
    }
    
    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 40),
            $"Press B to measure blend\nAverage: {averageBlendTime:F2}ms");
    }
}
```
