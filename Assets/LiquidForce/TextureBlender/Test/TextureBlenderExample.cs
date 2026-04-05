using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// Example component showing how to use TextureBlender in various scenarios.
/// Demonstrates simple blending, custom weights, different blend modes, and async operations.
/// Supports blending both base textures and normal maps.
/// </summary>
public class TextureBlenderExample : MonoBehaviour
{
    /// <summary>
    /// Data structure containing base texture, normal texture, and blend weight.
    /// </summary>
    [System.Serializable]
    public class TextureLayer
    {
        public Texture baseTexture;
        public Texture normalTexture;
        [Range(0f, 1f)]
        public float weight = 1f;
    }
    
    [Header("References")]
    [SerializeField] private TextureBlender textureBlender;
    [SerializeField] private TextureLayer[] textureLayers;
    [SerializeField] private MeshRenderer targetRenderer;
    
    [Header("Material Properties")]
    [SerializeField] private string baseMapPropertyName = "_BaseMap";
    [SerializeField] private string normalMapPropertyName = "_BumpMap";
    
    [Header("Blend Settings")]
    [SerializeField] private TextureBlender.BlendMode blendMode = TextureBlender.BlendMode.AlphaWeighted;
    [SerializeField] private bool useAsyncBlending = false;
    
    [Header("Performance Testing")]
    [SerializeField] private bool showPerformanceStats = true;
    [SerializeField] private TMPro.TextMeshProUGUI performanceText;
    
    private RenderTexture currentBaseResult;
    private RenderTexture currentNormalResult;
    private Texture2D flatNormalMap;
    private float lastBaseBlendTime;
    private float lastNormalBlendTime;
    private float lastTotalBlendTime;
    
    private void Awake()
    {
        // Create reusable flat normal map (tangent space: 0.5, 0.5, 1.0 = no normal change)
        CreateFlatNormalMap();
    }
    
    private async void Start()
    {
        if (textureBlender == null)
        {
            Debug.LogError("TextureBlender reference is not assigned!");
            return;
        }
        
        if (textureLayers == null || textureLayers.Length == 0)
        {
            Debug.LogWarning("No texture layers assigned to blend!");
            return;
        }
        
        // Example 1: Simple blend with default settings
        await Example1_SimpleBlend();
        
        // Uncomment to run other examples:
        // await Example2_CustomWeightsAndMode();
        // await Example3_AsyncBlend();
        // await Example4_BlendToExistingTexture();
        // await Example5_BatchBlending();
    }
    
    /// <summary>
    /// Creates a 1x1 flat normal map for use when TextureLayer.normalTexture is null.
    /// Flat normal in tangent space is (0.5, 0.5, 1.0) normalized.
    /// </summary>
    private void CreateFlatNormalMap()
    {
        flatNormalMap = new Texture2D(1, 1, TextureFormat.RGB24, false, true); // linear = true for normal maps
        Color flatNormal = new Color(0.5f, 0.5f, 1f, 1f); // Tangent space flat normal
        flatNormalMap.SetPixel(0, 0, flatNormal);
        flatNormalMap.Apply();
        flatNormalMap.name = "FlatNormalMap";
    }
    
    /// <summary>
    /// Extracts separate arrays for base textures, normal textures, and weights from textureLayers.
    /// Substitutes flatNormalMap for any null normal textures.
    /// </summary>
    private void GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights)
    {
        int count = textureLayers.Length;
        baseTextures = new Texture[count];
        normalTextures = new Texture[count];
        weights = new float[count];
        
        for (int i = 0; i < count; i++)
        {
            baseTextures[i] = textureLayers[i].baseTexture;
            normalTextures[i] = textureLayers[i].normalTexture != null 
                ? textureLayers[i].normalTexture 
                : flatNormalMap;
            weights[i] = textureLayers[i].weight;
        }
    }
    
    /// <summary>
    /// Example 1: Simple blend with default settings (equal weights, alpha-weighted mode)
    /// </summary>
    private async UniTask Example1_SimpleBlend()
    {
        Debug.Log("Example 1: Simple blend with default settings");
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights);
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Blend base textures
        var baseStartTime = Time.realtimeSinceStartup;
        currentBaseResult = textureBlender.BlendTextures(baseTextures, weights, blendMode);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // Blend normal textures
        var normalStartTime = Time.realtimeSinceStartup;
        currentNormalResult = textureBlender.BlendTextures(normalTextures, weights, blendMode);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        // Apply to renderer
        ApplyTexturesToMaterial();
        
        Debug.Log($"Blend completed - Base: {lastBaseBlendTime:F2}ms, Normal: {lastNormalBlendTime:F2}ms, Total: {lastTotalBlendTime:F2}ms");
        
        UpdatePerformanceDisplay();
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Example 2: Custom weights and blend mode
    /// </summary>
    private async UniTask Example2_CustomWeightsAndMode()
    {
        Debug.Log("Example 2: Custom weights and blend mode");
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights);
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Blend base textures
        var baseStartTime = Time.realtimeSinceStartup;
        currentBaseResult = textureBlender.BlendTextures(baseTextures, weights, blendMode);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // Blend normal textures
        var normalStartTime = Time.realtimeSinceStartup;
        currentNormalResult = textureBlender.BlendTextures(normalTextures, weights, blendMode);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        // Apply to renderer
        ApplyTexturesToMaterial();
        
        Debug.Log($"Custom blend completed - Base: {lastBaseBlendTime:F2}ms, Normal: {lastNormalBlendTime:F2}ms, Total: {lastTotalBlendTime:F2}ms");
        
        UpdatePerformanceDisplay();
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Example 3: Async blend (non-blocking) with parallel execution for base and normal textures
    /// </summary>
    private async UniTask Example3_AsyncBlend()
    {
        Debug.Log("Example 3: Async blend (non-blocking with parallel execution)");
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights);
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Execute both blends in parallel using UniTask.WhenAll
        var cancellationToken = this.GetCancellationTokenOnDestroy();
        
        var baseBlendTask = textureBlender.BlendTexturesAsync(
            baseTextures, 
            weights, 
            blendMode,
            cancellationToken);
        
        var normalBlendTask = textureBlender.BlendTexturesAsync(
            normalTextures, 
            weights, 
            blendMode,
            cancellationToken);
        
        // Wait for both to complete
        var results = await UniTask.WhenAll(baseBlendTask, normalBlendTask);
        
        currentBaseResult = results.Item1;
        currentNormalResult = results.Item2;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        // Note: Individual times not available for parallel execution
        lastBaseBlendTime = lastTotalBlendTime; // Approximate
        lastNormalBlendTime = lastTotalBlendTime; // Approximate
        
        // Apply to renderer
        ApplyTexturesToMaterial();
        
        Debug.Log($"Parallel async blend completed in {lastTotalBlendTime:F2}ms");
        
        UpdatePerformanceDisplay();
    }
    
    /// <summary>
    /// Example 4: Blend to existing texture (no allocation)
    /// </summary>
    private async UniTask Example4_BlendToExistingTexture()
    {
        Debug.Log("Example 4: Blend to existing texture (no allocation)");
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights);
        
        // Create or reuse existing RenderTextures
        if (currentBaseResult == null)
        {
            currentBaseResult = new RenderTexture(2048, 2048, 0, RenderTextureFormat.ARGB32);
            currentBaseResult.enableRandomWrite = true;
            currentBaseResult.Create();
        }
        
        if (currentNormalResult == null)
        {
            currentNormalResult = new RenderTexture(2048, 2048, 0, RenderTextureFormat.ARGB32);
            currentNormalResult.enableRandomWrite = true;
            currentNormalResult.Create();
        }
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Blend base textures
        var baseStartTime = Time.realtimeSinceStartup;
        textureBlender.BlendToExistingTexture(currentBaseResult, baseTextures, weights, blendMode);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // Blend normal textures
        var normalStartTime = Time.realtimeSinceStartup;
        textureBlender.BlendToExistingTexture(currentNormalResult, normalTextures, weights, blendMode);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        // Apply to renderer
        ApplyTexturesToMaterial();
        
        Debug.Log($"Blend to existing completed - Base: {lastBaseBlendTime:F2}ms, Normal: {lastNormalBlendTime:F2}ms, Total: {lastTotalBlendTime:F2}ms (no allocation overhead)");
        
        UpdatePerformanceDisplay();
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Example 5: Batch blending multiple texture sets
    /// </summary>
    private async UniTask Example5_BatchBlending()
    {
        Debug.Log("Example 5: Batch blending");
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights);
        
        // Create multiple blend requests for base textures
        var baseRequests = new TextureBlender.BlendRequest[]
        {
            new TextureBlender.BlendRequest
            {
                inputTextures = baseTextures,
                blendWeights = weights,
                blendMode = TextureBlender.BlendMode.Additive,
                outputWidth = 1024,
                outputHeight = 1024
            },
            new TextureBlender.BlendRequest
            {
                inputTextures = baseTextures,
                blendWeights = weights,
                blendMode = TextureBlender.BlendMode.AlphaWeighted,
                outputWidth = 1024,
                outputHeight = 1024
            },
            new TextureBlender.BlendRequest
            {
                inputTextures = baseTextures,
                blendWeights = weights,
                blendMode = TextureBlender.BlendMode.Multiplicative,
                outputWidth = 1024,
                outputHeight = 1024
            }
        };
        
        // Create multiple blend requests for normal textures
        var normalRequests = new TextureBlender.BlendRequest[]
        {
            new TextureBlender.BlendRequest
            {
                inputTextures = normalTextures,
                blendWeights = weights,
                blendMode = TextureBlender.BlendMode.Additive,
                outputWidth = 1024,
                outputHeight = 1024
            },
            new TextureBlender.BlendRequest
            {
                inputTextures = normalTextures,
                blendWeights = weights,
                blendMode = TextureBlender.BlendMode.AlphaWeighted,
                outputWidth = 1024,
                outputHeight = 1024
            },
            new TextureBlender.BlendRequest
            {
                inputTextures = normalTextures,
                blendWeights = weights,
                blendMode = TextureBlender.BlendMode.Multiplicative,
                outputWidth = 1024,
                outputHeight = 1024
            }
        };
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Execute all base blends
        var baseStartTime = Time.realtimeSinceStartup;
        RenderTexture[] baseResults = textureBlender.BatchBlend(baseRequests);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // Execute all normal blends
        var normalStartTime = Time.realtimeSinceStartup;
        RenderTexture[] normalResults = textureBlender.BatchBlend(normalRequests);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        Debug.Log($"Batch blend of {baseResults.Length} base + {normalResults.Length} normal operations completed");
        Debug.Log($"Base: {lastBaseBlendTime:F2}ms ({lastBaseBlendTime / baseResults.Length:F2}ms avg)");
        Debug.Log($"Normal: {lastNormalBlendTime:F2}ms ({lastNormalBlendTime / normalResults.Length:F2}ms avg)");
        Debug.Log($"Total: {lastTotalBlendTime:F2}ms");
        
        // Use first result (alpha-weighted mode at index 1)
        if (baseResults.Length > 1 && normalResults.Length > 1)
        {
            currentBaseResult = baseResults[1];
            currentNormalResult = normalResults[1];
            ApplyTexturesToMaterial();
        }
        
        UpdatePerformanceDisplay();
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Performance test: Blend the same textures multiple times to test caching
    /// </summary>
    [ContextMenu("Run Performance Test")]
    public void RunPerformanceTest()
    {
        if (textureBlender == null || textureLayers == null)
        {
            Debug.LogError("Cannot run performance test: missing references");
            return;
        }
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights);
        
        Debug.Log("=== PERFORMANCE TEST ===");
        Debug.Log($"Testing with {textureLayers.Length} texture layers");
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // First blend (includes array conversion) - Base
        var baseStartTime = Time.realtimeSinceStartup;
        var baseResult = textureBlender.BlendTextures(baseTextures, weights, blendMode);
        var firstBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // First blend (includes array conversion) - Normal
        var normalStartTime = Time.realtimeSinceStartup;
        var normalResult = textureBlender.BlendTextures(normalTextures, weights, blendMode);
        var firstNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        var firstTotalTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        Debug.Log($"First blend (uncached) - Base: {firstBaseBlendTime:F2}ms, Normal: {firstNormalBlendTime:F2}ms, Total: {firstTotalTime:F2}ms");
        
        // Return to pool
        textureBlender.ReturnTexture(baseResult);
        textureBlender.ReturnTexture(normalResult);
        
        // Second blend (should use cached array)
        totalStartTime = Time.realtimeSinceStartup;
        
        baseStartTime = Time.realtimeSinceStartup;
        baseResult = textureBlender.BlendTextures(baseTextures, weights, blendMode);
        var secondBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        normalStartTime = Time.realtimeSinceStartup;
        normalResult = textureBlender.BlendTextures(normalTextures, weights, blendMode);
        var secondNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        var secondTotalTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        Debug.Log($"Second blend (cached) - Base: {secondBaseBlendTime:F2}ms, Normal: {secondNormalBlendTime:F2}ms, Total: {secondTotalTime:F2}ms");
        Debug.Log($"Speedup - Base: {firstBaseBlendTime / secondBaseBlendTime:F2}x, Normal: {firstNormalBlendTime / secondNormalBlendTime:F2}x, Total: {firstTotalTime / secondTotalTime:F2}x");
        
        // Keep results
        currentBaseResult = baseResult;
        currentNormalResult = normalResult;
        
        lastBaseBlendTime = secondBaseBlendTime;
        lastNormalBlendTime = secondNormalBlendTime;
        lastTotalBlendTime = secondTotalTime;
        
        ApplyTexturesToMaterial();
        UpdatePerformanceDisplay();
    }
    
    /// <summary>
    /// Applies blended textures to the target material using configured property names.
    /// </summary>
    private void ApplyTexturesToMaterial()
    {
        if (targetRenderer == null)
        {
            Debug.LogWarning("Target renderer is not assigned!");
            return;
        }
        
        if (currentBaseResult != null)
        {
            targetRenderer.material.SetTexture(baseMapPropertyName, currentBaseResult);
        }
        
        if (currentNormalResult != null)
        {
            targetRenderer.material.SetTexture(normalMapPropertyName, currentNormalResult);
        }
    }
    
    /// <summary>
    /// Updates the performance display text with current blend times.
    /// </summary>
    private void UpdatePerformanceDisplay()
    {
        if (showPerformanceStats && performanceText != null)
        {
            performanceText.text = $"Base: {lastBaseBlendTime:F2}ms\n" +
                                  $"Normal: {lastNormalBlendTime:F2}ms\n" +
                                  $"Total: {lastTotalBlendTime:F2}ms\n" +
                                  $"Layers: {textureLayers?.Length ?? 0}\n" +
                                  $"Mode: {blendMode}";
        }
    }
    
    private void OnDestroy()
    {
        // Clean up flat normal map
        if (flatNormalMap != null)
        {
            Destroy(flatNormalMap);
            flatNormalMap = null;
        }
        
        // Clean up (return textures to pool or release)
        if (currentBaseResult != null)
        {
            textureBlender?.ReturnTexture(currentBaseResult);
            currentBaseResult = null;
        }
        
        if (currentNormalResult != null)
        {
            textureBlender?.ReturnTexture(currentNormalResult);
            currentNormalResult = null;
        }
    }
    
    private void OnGUI()
    {
        if (!showPerformanceStats || performanceText != null) return;
        
        // Simple on-screen stats if TextMeshPro not assigned
        GUI.Label(new Rect(10, 10, 300, 120), 
            $"Base Blend: {lastBaseBlendTime:F2}ms\n" +
            $"Normal Blend: {lastNormalBlendTime:F2}ms\n" +
            $"Total Time: {lastTotalBlendTime:F2}ms\n" +
            $"Texture Layers: {textureLayers?.Length ?? 0}\n" +
            $"Blend Mode: {blendMode}");
    }
}

