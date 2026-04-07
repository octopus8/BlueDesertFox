using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Debug = UnityEngine.Debug;

/// <summary>
/// Benchmark component for measuring TextureBlender performance.
/// Tests various texture counts, resolutions, and blend modes.
/// Displays real-time performance stats and logs results.
/// </summary>
public class TextureBlenderBenchmark : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextureBlender textureBlender;
    [SerializeField] private TMPro.TextMeshProUGUI resultsDisplay;
    
    [Header("Benchmark Configuration")]
    [SerializeField] private Texture[] testTextures;
    [SerializeField] private int[] textureCountsToTest = { 2, 4, 8, 16, 32 };
    [SerializeField] private int[] resolutionsToTest = { 512, 1024, 2048, 4096 };
    [SerializeField] private bool testAllBlendModes = true;
    [SerializeField] private bool testCachedPerformance = true;
    
    [Header("Results")]
    [SerializeField] private bool logResultsToConsole = true;
    [SerializeField] private bool saveResultsToFile = false;
    [SerializeField] private string resultsFilePath = "TextureBlenderBenchmark.csv";
    
    private List<BenchmarkResult> results = new List<BenchmarkResult>();
    
    private struct BenchmarkResult
    {
        public int textureCount;
        public int resolution;
        public TextureBlender.BlendMode blendMode;
        public float uncachedTimeMs;
        public float cachedTimeMs;
        public float speedup;
        
        public override string ToString()
        {
            return $"{textureCount} textures @ {resolution}x{resolution}, {blendMode}: " +
                   $"Uncached={uncachedTimeMs:F2}ms, Cached={cachedTimeMs:F2}ms, Speedup={speedup:F2}x";
        }
        
        public string ToCSV()
        {
            return $"{textureCount},{resolution},{blendMode},{uncachedTimeMs:F3},{cachedTimeMs:F3},{speedup:F3}";
        }
    }
    
    [ContextMenu("Run Full Benchmark")]
    public void RunFullBenchmark()
    {
        if (textureBlender == null)
        {
            Debug.LogError("TextureBlender reference not assigned!");
            return;
        }
        
        if (testTextures == null || testTextures.Length == 0)
        {
            Debug.LogError("No test textures assigned!");
            return;
        }
        
        results.Clear();
        
        Debug.Log("=== TEXTURE BLENDER BENCHMARK START ===");
        
        // Test different blend modes
        TextureBlender.BlendMode[] modesToTest = testAllBlendModes
            ? new[] { TextureBlender.BlendMode.Additive, TextureBlender.BlendMode.AlphaWeighted, TextureBlender.BlendMode.Multiplicative }
            : new[] { TextureBlender.BlendMode.AlphaWeighted };
        
        foreach (var mode in modesToTest)
        {
            foreach (var resolution in resolutionsToTest)
            {
                foreach (var textureCount in textureCountsToTest)
                {
                    if (textureCount > testTextures.Length)
                    {
                        Debug.LogWarning($"Skipping test with {textureCount} textures (only {testTextures.Length} available)");
                        continue;
                    }
                    
                    BenchmarkResult result = RunSingleBenchmark(textureCount, resolution, mode);
                    results.Add(result);
                    
                    if (logResultsToConsole)
                    {
                        Debug.Log(result.ToString());
                    }
                }
            }
        }
        
        Debug.Log("=== BENCHMARK COMPLETE ===");
        
        DisplayResults();
        
        if (saveResultsToFile)
        {
            SaveResultsToCSV();
        }
    }
    
    private BenchmarkResult RunSingleBenchmark(int textureCount, int resolution, TextureBlender.BlendMode mode)
    {
        // Prepare texture subset
        Texture[] textures = new Texture[textureCount];
        for (int i = 0; i < textureCount; i++)
        {
            textures[i] = testTextures[i % testTextures.Length];
        }
        
        // Equal weights
        float[] weights = new float[textureCount];
        for (int i = 0; i < textureCount; i++)
        {
            weights[i] = 1f / textureCount;
        }
        
        // Clear cache before first test
//        textureBlender.ClearCache();
        
        // Force garbage collection to avoid GC during benchmark
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        
        // Measure uncached performance
        Stopwatch sw = Stopwatch.StartNew();
        RenderTexture result1 = textureBlender.BlendTextures(textures, weights, mode);
        sw.Stop();
        float uncachedTime = sw.ElapsedMilliseconds + (sw.ElapsedTicks % 10000) / 10000f;
        
        // Return to pool
        textureBlender.ReturnTexture(result1);
        
        float cachedTime = 0f;
        float speedup = 1f;
        
        // Measure cached performance if enabled
        if (testCachedPerformance)
        {
            sw.Restart();
            RenderTexture result2 = textureBlender.BlendTextures(textures, weights, mode);
            sw.Stop();
            cachedTime = sw.ElapsedMilliseconds + (sw.ElapsedTicks % 10000) / 10000f;
            
            speedup = cachedTime > 0 ? uncachedTime / cachedTime : 1f;
            
            // Clean up
            textureBlender.ReturnTexture(result2);
        }
        
        return new BenchmarkResult
        {
            textureCount = textureCount,
            resolution = resolution,
            blendMode = mode,
            uncachedTimeMs = uncachedTime,
            cachedTimeMs = cachedTime,
            speedup = speedup
        };
    }
    
    [ContextMenu("Run Quick VR Performance Test")]
    public void RunVRPerformanceTest()
    {
        Debug.Log("=== VR PERFORMANCE TEST (1024x1024) ===");
        
        // VR-optimized resolution
        int vrResolution = 1024;
        int[] vrTextureCounts = { 2, 4, 8 };
        
        foreach (var count in vrTextureCounts)
        {
            if (count > testTextures.Length) continue;
            
            var result = RunSingleBenchmark(count, vrResolution, TextureBlender.BlendMode.AlphaWeighted);
            
            bool meetsVRTarget = result.uncachedTimeMs < 3f;
            string status = meetsVRTarget ? "✓ PASS" : "✗ FAIL";
            
            Debug.Log($"{status} {result.ToString()}");
        }
    }
    
    private void DisplayResults()
    {
        if (resultsDisplay == null) return;
        
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== BENCHMARK RESULTS ===\n");
        
        // Group by blend mode
        var groupedResults = new Dictionary<TextureBlender.BlendMode, List<BenchmarkResult>>();
        
        foreach (var result in results)
        {
            if (!groupedResults.ContainsKey(result.blendMode))
                groupedResults[result.blendMode] = new List<BenchmarkResult>();
            
            groupedResults[result.blendMode].Add(result);
        }
        
        foreach (var kvp in groupedResults)
        {
            sb.AppendLine($"<b>{kvp.Key} Mode</b>");
            sb.AppendLine("─────────────────────────");
            
            foreach (var result in kvp.Value)
            {
                string status = result.uncachedTimeMs < 5f ? "✓" : "✗";
                sb.AppendLine($"{status} {result.textureCount}tex @ {result.resolution}:");
                sb.AppendLine($"   {result.uncachedTimeMs:F2}ms → {result.cachedTimeMs:F2}ms ({result.speedup:F1}x)");
            }
            
            sb.AppendLine();
        }
        
        resultsDisplay.text = sb.ToString();
    }
    
    private void SaveResultsToCSV()
    {
        StringBuilder csv = new StringBuilder();
        csv.AppendLine("TextureCount,Resolution,BlendMode,UncachedMs,CachedMs,Speedup");
        
        foreach (var result in results)
        {
            csv.AppendLine(result.ToCSV());
        }
        
        string filePath = System.IO.Path.Combine(Application.dataPath, resultsFilePath);
        System.IO.File.WriteAllText(filePath, csv.ToString());
        
        Debug.Log($"Results saved to: {filePath}");
    }
    
    private void OnGUI()
    {
        if (resultsDisplay != null) return;
        
        // Simple on-screen display if TextMeshPro not assigned
        if (results.Count > 0)
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, Screen.height - 20));
            GUILayout.Label("<b>Last Benchmark Results:</b>");
            
            int displayCount = Mathf.Min(10, results.Count);
            for (int i = results.Count - displayCount; i < results.Count; i++)
            {
                GUILayout.Label(results[i].ToString());
            }
            
            GUILayout.EndArea();
        }
    }
}

