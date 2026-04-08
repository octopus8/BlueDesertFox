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
    /// Data structure containing base texture, normal texture, blend weight, and rotation.
    /// </summary>
    [System.Serializable]
    public class TextureLayer
    {
        /// <summary> Base texture to blend (e.g. albedo/diffuse). </summary>
        [Tooltip("The base texture.")]
        public Texture baseTexture;
        
        /// <summary> Normal map texture to blend. If null, a flat normal (0.5, 0.5, 1.0) will be used, which means no normal change. </summary>
        [Tooltip("The normal texture.")]
        public Texture normalTexture;

        /// <summary> Blend weight for this layer (0 = no contribution, 1 = full contribution). </summary>
        [Tooltip("Blend weight for this layer (0 = no contribution, 1 = full contribution).")]
        [Range(0f, 1f)]
        public float weight = 1f;
        
        /// <summary> Rotation in degrees to apply to this layer's textures before blending. </summary>
        [Tooltip("Rotation in degrees to apply to this layer's textures before blending.")]
        [Range(0f, 360f)]
        public float rotationDegrees = 0f;
        
        
        [Header("UV Offset (Tiling)")]

        /// <summary> Horizontal UV offset to apply to this layer's textures before blending. Wraps/tiles automatically. </summary>
        [Tooltip("Horizontal UV offset (wraps/tiles automatically)")]
        public float offsetX = 0f;
        
        /// <summary> Vertical UV offset to apply to this layer's textures before blending. Wraps/tiles automatically. </summary>
        [Tooltip("Vertical UV offset (wraps/tiles automatically)")]
        public float offsetY = 0f;
    }
    
    
    [Header("References")]
    
    /// <summary> The TextureBlender to use. </summary>
    [Tooltip("The TextureBlender to use")]
    [SerializeField] private TextureBlender textureBlender;

    /// <summary> Array of texture layers to blend. Each layer contains a base texture, an optional normal texture, a blend weight, and a rotation. </summary>
    [Tooltip("Array of texture layers to blend. Each layer contains a base texture, an optional normal texture, a blend weight, and a rotation.")]
    [SerializeField] private TextureLayer[] textureLayers;
    
    /// <summary> The target MeshRenderer whose material will have the blended textures applied. </summary>
    [Tooltip("The target MeshRenderer whose material will have the blended textures applied.")]
    [SerializeField] private MeshRenderer targetRenderer;

    
    [Header("Material Properties")]
    
    /// <summary> The name of the base map property in the shader (e.g. "_BaseMap" for URP/Lit). </summary>
    [Tooltip("The name of the base map property in the shader (e.g. '_BaseMap' for URP/Lit)")]
    [SerializeField] private string baseMapPropertyName = "_BaseMap";
    
    /// <summary> The name of the normal map property in the shader (e.g. "_BumpMap" for URP/Lit). </summary>
    [Tooltip("The name of the normal map property in the shader (e.g. '_BumpMap' for URP/Lit)")]
    [SerializeField] private string normalMapPropertyName = "_BumpMap";

    
    [Header("Blend Settings")]
    
    /// <summary> The blend mode to use. </summary>
    [Tooltip("The blend mode to use")]
    [SerializeField] private TextureBlender.BlendMode blendMode = TextureBlender.BlendMode.AlphaWeighted;
    
    
    [Header("Performance Testing")]
    
    /// <summary> Whether to show performance stats on screen. If false, stats will only be logged to the console. </summary>
    [Tooltip("Whether to show performance stats on screen. If false, stats will only be logged to the console.")]
    [SerializeField] private bool showPerformanceStats = true;
    
    /// <summary> Reference to a TextMeshProUGUI component to display performance stats. If null, stats will be shown using OnGUI instead. </summary>
    [Tooltip("Whether to show performance stats on screen.")]
    [SerializeField] private TMPro.TextMeshProUGUI performanceText;
    
    /// <summary> Current blended base texture. </summary>
    private RenderTexture currentBaseResult;
    
    /// <summary> Current blended normal texture. </summary>
    private RenderTexture currentNormalResult;
    
    /// <summary> Reusable flat normal map texture (1x1) used when a TextureLayer's normalTexture is null. </summary>
    private Texture2D flatNormalMap;
    
    /// <summary> Performance metrics for the last blend operation. These are updated after each blend and can be displayed on screen or logged. </summary>
    private float lastBaseBlendTime;
    
    /// <summary> Performance metric for the last normal blend operation. This is updated after each blend and can be displayed on screen or logged. Note that normal blending with per-pixel alpha modulation is typically more expensive than base texture blending, so this time may be significantly higher than lastBaseBlendTime, especially with many layers or high-resolution textures. </summary>
    private float lastNormalBlendTime;
    
    /// <summary> Performance metric for the total time taken by the last blend operation, including both base and normal blending. This is useful for understanding the overall cost of blending all textures together, especially when using per-pixel alpha modulation for normals, which can significantly increase the total time. This metric can be displayed on screen or logged to the console for performance analysis. </summary>
    private float lastTotalBlendTime;

    
    /// <summary>
    /// Initializes the example by creating a reusable flat normal map. This flat normal map is used for any texture layer
    /// that does not have a normal texture assigned, ensuring that those layers do not affect the normal blending result.
    /// The flat normal is represented as a 1x1 texture with a color value of (0.5, 0.5, 1.0) in tangent space, which corresponds
    /// to no change in the normal direction when blended. This setup allows the example to demonstrate blending with and without
    /// normal maps seamlessly, and ensures that the TextureBlender can handle cases where some layers only contribute base textures without normals.
    /// </summary>
    private void Awake()
    {
        // Create reusable flat normal map (tangent space: 0.5, 0.5, 1.0 = no normal change)
        CreateFlatNormalMap();
    }
    
    
    /// <summary>
    /// Starts the blending process by running through various examples of how to use TextureBlender. Each example demonstrates
    /// different features such as simple blending, custom weights, different blend modes, blending to existing textures, and batch
    /// blending multiple sets of textures. The results are applied to the target material, and performance metrics are logged and
    /// optionally displayed on screen. This method can be modified to run specific examples or to trigger blends based on user
    /// input or other events in a real application.
    /// </summary>
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
        
        await ExampleCommonBlend();
        
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
    /// Extracts separate arrays for base textures, normal textures, weights, and rotations from textureLayers.
    /// Substitutes flatNormalMap for any null normal textures.
    /// </summary>
    private void GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights, out float[] rotations, out Vector2[] offsets)
    {
        int count = textureLayers.Length;
        baseTextures = new Texture[count];
        normalTextures = new Texture[count];
        weights = new float[count];
        rotations = new float[count];
        offsets = new Vector2[count];
        
        for (int i = 0; i < count; i++)
        {
            baseTextures[i] = textureLayers[i].baseTexture;
            normalTextures[i] = textureLayers[i].normalTexture != null 
                ? textureLayers[i].normalTexture 
                : flatNormalMap;
            weights[i] = textureLayers[i].weight;
            rotations[i] = textureLayers[i].rotationDegrees;
            offsets[i] = new Vector2(textureLayers[i].offsetX, textureLayers[i].offsetY);
        }
    }
    

    /// <summary>
    /// Simple blend with per-pixel alpha-weighted normals. This example demonstrates how to blend multiple
    /// base textures together while also blending their corresponding normal maps using per-pixel alpha modulation based
    /// on the base textures' alpha channels. This allows for more accurate normal blending that takes into account the
    /// transparency of the base textures, resulting in better visual quality, especially when using textures with varying
    /// levels of transparency. The blended results are then applied to the target material, and performance metrics are logged
    /// and optionally displayed on screen. This example serves as a common use case.
    /// </summary>
    private async UniTask ExampleCommonBlend()
    {
        Debug.Log("Example 1: Simple blend with per-pixel alpha-weighted normals");
        
        // Extract texture arrays.
        // This is done here for demonstration, but in a real application you might want to cache these arrays if textureLayers doesn't change frequently.
        // This method converts the array of textureLayers into separate arrays for base textures, normal textures, and weights, which is the format expected by TextureBlender.
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights, out float[] rotations, out Vector2[] offsets);
        
        // Record the start time.
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Blend base textures
        var baseStartTime = Time.realtimeSinceStartup;
        currentBaseResult = textureBlender.BlendTextures(baseTextures, weights, rotations, offsets, blendMode);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // Blend normal textures with per-pixel base alpha modulation and rotation
        var normalStartTime = Time.realtimeSinceStartup;
        currentNormalResult = textureBlender.BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, rotations, offsets, blendMode);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;

        // Compute the end time.
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        // Apply to renderer
        ApplyTexturesToMaterial();

        // Log performance.
        Debug.Log($"Blend completed - Base: {lastBaseBlendTime:F2}ms, Normal (per-pixel alpha): {lastNormalBlendTime:F2}ms, Total: {lastTotalBlendTime:F2}ms");
        
        // Update the performance display.
        UpdatePerformanceDisplay();
        
        // End UniTask.
        await UniTask.Yield();
    }
    
    
    /// <summary>
    /// Example 2: Custom weights and blend mode
    /// </summary>
    private async UniTask Example2_CustomWeightsAndMode()
    {
        Debug.Log("Example 2: Custom weights and blend mode with per-pixel alpha normals");
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights, out float[] rotations, out Vector2[] offsets);
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Blend base textures
        var baseStartTime = Time.realtimeSinceStartup;
        currentBaseResult = textureBlender.BlendTextures(baseTextures, weights, rotations, offsets, blendMode);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // Blend normal textures with per-pixel base alpha modulation and rotation
        var normalStartTime = Time.realtimeSinceStartup;
        currentNormalResult = textureBlender.BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, rotations, offsets, blendMode);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        // Apply to renderer
        ApplyTexturesToMaterial();
        
        Debug.Log($"Custom blend completed - Base: {lastBaseBlendTime:F2}ms, Normal (per-pixel alpha): {lastNormalBlendTime:F2}ms, Total: {lastTotalBlendTime:F2}ms");
        
        UpdatePerformanceDisplay();
        
        await UniTask.Yield();
    }
    
    
    /// <summary>
    /// Example 4: Blend to existing texture (no allocation)
    /// </summary>
    private async UniTask Example4_BlendToExistingTexture()
    {
        Debug.Log("Example 4: Blend to existing texture (no allocation)");
        
        // Extract texture arrays
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights, out float[] rotations, out Vector2[] offsets);
        
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
        textureBlender.BlendToExistingTexture(currentBaseResult, baseTextures, weights, rotations, offsets, blendMode);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // Blend normal textures with per-pixel base alpha modulation and rotation
        var normalStartTime = Time.realtimeSinceStartup;
        textureBlender.BlendNormalsWithBaseAlphaToExistingTexture(currentNormalResult, normalTextures, baseTextures, weights, rotations, offsets, blendMode);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        // Apply to renderer
        ApplyTexturesToMaterial();
        
        Debug.Log($"Blend to existing completed - Base: {lastBaseBlendTime:F2}ms, Normal (per-pixel alpha): {lastNormalBlendTime:F2}ms, Total: {lastTotalBlendTime:F2}ms (no allocation overhead)");
        
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
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights, out float[] rotations, out Vector2[] offsets);
        
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
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // Execute all base blends
        var baseStartTime = Time.realtimeSinceStartup;
        RenderTexture[] baseResults = textureBlender.BatchBlend(baseRequests);
        lastBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // For normals, blend with per-pixel alpha (sequential for simplicity)
        var normalStartTime = Time.realtimeSinceStartup;
        RenderTexture[] normalResults = new RenderTexture[3];
        normalResults[0] = textureBlender.BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, rotations, offsets, TextureBlender.BlendMode.Additive);
        normalResults[1] = textureBlender.BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, rotations, offsets, TextureBlender.BlendMode.AlphaWeighted);
        normalResults[2] = textureBlender.BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, rotations, offsets, TextureBlender.BlendMode.Multiplicative);
        lastNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        lastTotalBlendTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        Debug.Log($"Batch blend of {baseResults.Length} base + {normalResults.Length} normal operations completed");
        Debug.Log($"Base: {lastBaseBlendTime:F2}ms ({lastBaseBlendTime / baseResults.Length:F2}ms avg)");
        Debug.Log($"Normal (per-pixel alpha): {lastNormalBlendTime:F2}ms ({lastNormalBlendTime / normalResults.Length:F2}ms avg)");
        Debug.Log($"Total: {lastTotalBlendTime:F2}ms");
        
        // Use alpha-weighted mode (index 1)
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
        GetTextureArrays(out Texture[] baseTextures, out Texture[] normalTextures, out float[] weights, out float[] rotations, out Vector2[] offsets);
        
        Debug.Log("=== PERFORMANCE TEST ===");
        Debug.Log($"Testing with {textureLayers.Length} texture layers");
        
        var totalStartTime = Time.realtimeSinceStartup;
        
        // First blend (includes array conversion) - Base
        var baseStartTime = Time.realtimeSinceStartup;
        var baseResult = textureBlender.BlendTextures(baseTextures, weights, rotations, offsets, blendMode);
        var firstBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        // First blend (includes array conversion) - Normal with per-pixel alpha
        var normalStartTime = Time.realtimeSinceStartup;
        var normalResult = textureBlender.BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, blendMode);
        var firstNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        var firstTotalTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        Debug.Log($"First blend (uncached) - Base: {firstBaseBlendTime:F2}ms, Normal (per-pixel alpha): {firstNormalBlendTime:F2}ms, Total: {firstTotalTime:F2}ms");
        
        // Return to pool
        textureBlender.ReturnTexture(baseResult);
        textureBlender.ReturnTexture(normalResult);
        
        // Second blend (should use cached array)
        totalStartTime = Time.realtimeSinceStartup;
        
        baseStartTime = Time.realtimeSinceStartup;
        baseResult = textureBlender.BlendTextures(baseTextures, weights, rotations, offsets, blendMode);
        var secondBaseBlendTime = (Time.realtimeSinceStartup - baseStartTime) * 1000f;
        
        normalStartTime = Time.realtimeSinceStartup;
        normalResult = textureBlender.BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, blendMode);
        var secondNormalBlendTime = (Time.realtimeSinceStartup - normalStartTime) * 1000f;
        
        var secondTotalTime = (Time.realtimeSinceStartup - totalStartTime) * 1000f;
        
        Debug.Log($"Second blend (cached) - Base: {secondBaseBlendTime:F2}ms, Normal (per-pixel alpha): {secondNormalBlendTime:F2}ms, Total: {secondTotalTime:F2}ms");
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
    
    /// <summary>
    /// Cleans up resources when the component is destroyed. This includes destroying the flat normal map texture and returning
    /// any blended textures to the pool if they were created. This ensures that there are no memory leaks and that resources
    /// are properly released when the component is removed from the scene or when the application is closed. It's important
    /// to clean up RenderTextures and other GPU resources to avoid unnecessary memory usage and potential performance issues.
    /// </summary>
    private void OnDestroy()
    {
        // Clean up flat normal map
        if (flatNormalMap != null)
        {
            Destroy(flatNormalMap);
            flatNormalMap = null;
        }
        
        // Return blended base texture to pool.
        if (currentBaseResult != null)
        {
            textureBlender?.ReturnTexture(currentBaseResult);
            currentBaseResult = null;
        }
        
        // Return blended normal texture to pool.
        if (currentNormalResult != null)
        {
            textureBlender?.ReturnTexture(currentNormalResult);
            currentNormalResult = null;
        }
    }
    
    
    /// <summary>
    /// Displays performance stats on screen using OnGUI if showPerformanceStats is true and performanceText is not assigned.
    /// This provides a fallback method for displaying performance metrics if a TextMeshProUGUI component is not available.
    /// The stats include the last blend times for base and normal textures, the total time, the number of texture layers,
    /// and the blend mode used. This can be useful for quick debugging and performance analysis without needing to
    /// set up a UI text component in the scene.
    /// </summary>
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

