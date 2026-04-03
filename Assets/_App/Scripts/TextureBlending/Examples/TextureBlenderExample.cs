using UnityEngine;
using Cysharp.Threading.Tasks;

/// <summary>
/// Example component showing how to use TextureBlender in various scenarios.
/// Demonstrates simple blending, custom weights, different blend modes, and async operations.
/// </summary>
public class TextureBlenderExample : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextureBlender textureBlender;
    [SerializeField] private Texture[] texturesToBlend;
    [SerializeField] private float[] blendWeights;
    [SerializeField] private MeshRenderer targetRenderer;
    
    [Header("Blend Settings")]
    [SerializeField] private TextureBlender.BlendMode blendMode = TextureBlender.BlendMode.AlphaWeighted;
    [SerializeField] private bool useAsyncBlending = false;
    
    [Header("Performance Testing")]
    [SerializeField] private bool showPerformanceStats = true;
    [SerializeField] private TMPro.TextMeshProUGUI performanceText;
    
    private RenderTexture currentResult;
    private float lastBlendTime;
    
    private async void Start()
    {
        if (textureBlender == null)
        {
            Debug.LogError("TextureBlender reference is not assigned!");
            return;
        }
        
        if (texturesToBlend == null || texturesToBlend.Length == 0)
        {
            Debug.LogWarning("No textures assigned to blend!");
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
    /// Example 1: Simple blend with default settings (equal weights, alpha-weighted mode)
    /// </summary>
    private async UniTask Example1_SimpleBlend()
    {
        Debug.Log("Example 1: Simple blend with default settings");
        
        var startTime = Time.realtimeSinceStartup;
        
        // Simple one-line blend
        currentResult = textureBlender.BlendTextures(texturesToBlend);
        
        lastBlendTime = (Time.realtimeSinceStartup - startTime) * 1000f; // Convert to ms
        
        // Apply to renderer
        if (targetRenderer != null && currentResult != null)
        {
            targetRenderer.material.mainTexture = currentResult;
        }
        
        Debug.Log($"Blend completed in {lastBlendTime:F2}ms");
        
        if (showPerformanceStats && performanceText != null)
        {
            performanceText.text = $"Blend Time: {lastBlendTime:F2}ms\nTextures: {texturesToBlend.Length}\nMode: {blendMode}";
        }
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Example 2: Custom weights and blend mode
    /// </summary>
    private async UniTask Example2_CustomWeightsAndMode()
    {
        Debug.Log("Example 2: Custom weights and blend mode");
        
        var startTime = Time.realtimeSinceStartup;
        
        // Use custom weights and specified blend mode
        currentResult = textureBlender.BlendTextures(
            texturesToBlend, 
            blendWeights, 
            blendMode);
        
        lastBlendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        if (targetRenderer != null && currentResult != null)
        {
            targetRenderer.material.mainTexture = currentResult;
        }
        
        Debug.Log($"Custom blend completed in {lastBlendTime:F2}ms");
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Example 3: Async blend (non-blocking)
    /// </summary>
    private async UniTask Example3_AsyncBlend()
    {
        Debug.Log("Example 3: Async blend (non-blocking)");
        
        var startTime = Time.realtimeSinceStartup;
        
        // Async blend with cancellation token support
        currentResult = await textureBlender.BlendTexturesAsync(
            texturesToBlend, 
            blendWeights, 
            blendMode,
            this.GetCancellationTokenOnDestroy());
        
        lastBlendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        if (targetRenderer != null && currentResult != null)
        {
            targetRenderer.material.mainTexture = currentResult;
        }
        
        Debug.Log($"Async blend completed in {lastBlendTime:F2}ms");
    }
    
    /// <summary>
    /// Example 4: Blend to existing texture (no allocation)
    /// </summary>
    private async UniTask Example4_BlendToExistingTexture()
    {
        Debug.Log("Example 4: Blend to existing texture (no allocation)");
        
        // Create or reuse existing RenderTexture
        if (currentResult == null)
        {
            currentResult = new RenderTexture(2048, 2048, 0, RenderTextureFormat.ARGB32);
            currentResult.enableRandomWrite = true;
            currentResult.Create();
        }
        
        var startTime = Time.realtimeSinceStartup;
        
        // Blend directly to existing texture (fastest - no allocation)
        textureBlender.BlendToExistingTexture(
            currentResult, 
            texturesToBlend, 
            blendWeights,
            blendMode);
        
        lastBlendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = currentResult;
        }
        
        Debug.Log($"Blend to existing completed in {lastBlendTime:F2}ms (no allocation overhead)");
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Example 5: Batch blending multiple texture sets
    /// </summary>
    private async UniTask Example5_BatchBlending()
    {
        Debug.Log("Example 5: Batch blending");
        
        // Create multiple blend requests
        var requests = new TextureBlender.BlendRequest[]
        {
            new TextureBlender.BlendRequest
            {
                inputTextures = texturesToBlend,
                blendWeights = blendWeights,
                blendMode = TextureBlender.BlendMode.Additive,
                outputWidth = 1024,
                outputHeight = 1024
            },
            new TextureBlender.BlendRequest
            {
                inputTextures = texturesToBlend,
                blendWeights = blendWeights,
                blendMode = TextureBlender.BlendMode.AlphaWeighted,
                outputWidth = 1024,
                outputHeight = 1024
            },
            new TextureBlender.BlendRequest
            {
                inputTextures = texturesToBlend,
                blendWeights = blendWeights,
                blendMode = TextureBlender.BlendMode.Multiplicative,
                outputWidth = 1024,
                outputHeight = 1024
            }
        };
        
        var startTime = Time.realtimeSinceStartup;
        
        // Execute all blends
        RenderTexture[] results = textureBlender.BatchBlend(requests);
        
        lastBlendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        Debug.Log($"Batch blend of {results.Length} operations completed in {lastBlendTime:F2}ms");
        Debug.Log($"Average per blend: {lastBlendTime / results.Length:F2}ms");
        
        // Use first result
        if (results.Length > 0 && targetRenderer != null)
        {
            currentResult = results[0];
            targetRenderer.material.mainTexture = currentResult;
        }
        
        await UniTask.Yield();
    }
    
    /// <summary>
    /// Performance test: Blend the same textures multiple times to test caching
    /// </summary>
    [ContextMenu("Run Performance Test")]
    public void RunPerformanceTest()
    {
        if (textureBlender == null || texturesToBlend == null)
        {
            Debug.LogError("Cannot run performance test: missing references");
            return;
        }
        
        Debug.Log("=== PERFORMANCE TEST ===");
        Debug.Log($"Testing with {texturesToBlend.Length} textures");
        
        // First blend (includes array conversion)
        var startTime = Time.realtimeSinceStartup;
        var result = textureBlender.BlendTextures(texturesToBlend, blendWeights, blendMode);
        var firstBlendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        Debug.Log($"First blend (uncached): {firstBlendTime:F2}ms");
        
        // Return to pool
        textureBlender.ReturnTexture(result);
        
        // Second blend (should use cached array)
        startTime = Time.realtimeSinceStartup;
        result = textureBlender.BlendTextures(texturesToBlend, blendWeights, blendMode);
        var secondBlendTime = (Time.realtimeSinceStartup - startTime) * 1000f;
        
        Debug.Log($"Second blend (cached): {secondBlendTime:F2}ms");
        Debug.Log($"Speedup: {firstBlendTime / secondBlendTime:F2}x");
        
        // Keep result
        currentResult = result;
        if (targetRenderer != null)
        {
            targetRenderer.material.mainTexture = currentResult;
        }
        
        if (showPerformanceStats && performanceText != null)
        {
            performanceText.text = $"First: {firstBlendTime:F2}ms\nCached: {secondBlendTime:F2}ms\nSpeedup: {firstBlendTime / secondBlendTime:F2}x";
        }
    }
    
    private void OnDestroy()
    {
        // Clean up (return texture to pool or release)
        if (currentResult != null)
        {
            textureBlender?.ReturnTexture(currentResult);
        }
    }
    
    private void OnGUI()
    {
        if (!showPerformanceStats || performanceText != null) return;
        
        // Simple on-screen stats if TextMeshPro not assigned
        GUI.Label(new Rect(10, 10, 300, 100), 
            $"Last Blend Time: {lastBlendTime:F2}ms\n" +
            $"Texture Count: {texturesToBlend?.Length ?? 0}\n" +
            $"Blend Mode: {blendMode}");
    }
}

