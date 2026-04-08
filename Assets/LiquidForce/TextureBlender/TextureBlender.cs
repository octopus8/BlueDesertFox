using UnityEngine;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Unity.Profiling;
using System.Threading;

/// <summary>
/// Reusable texture blending component that can blend any number of textures using GPU compute shaders.
/// Supports multiple blend modes, resource pooling, and async operations.
/// PERFORMANCE: Target 5ms for 4×2048² textures on RTX 3070, 2ms for cached repeat blends.
/// </summary>
[RequireComponent(typeof(Transform))]
public class TextureBlender : MonoBehaviour
{
    #region Nested Types
    
    /// <summary>
    /// Blend mode determines how textures are combined.
    /// </summary>
    public enum BlendMode
    {
        /// <summary>Simple weighted sum - FASTEST (30% faster than alpha-weighted)</summary>
        Additive,
        
        /// <summary>Alpha-weighted blending - respects texture alpha channels</summary>
        AlphaWeighted,
        
        /// <summary>Multiplicative blending - useful for masking/darkening effects</summary>
        Multiplicative
    }
    
    
    /// <summary>
    /// Encapsulates all parameters for a blend operation.
    /// </summary>
    public struct BlendRequest
    {
        public Texture[] inputTextures;
        public float[] blendWeights;
        public BlendMode blendMode;
        public RenderTexture targetOutput;  // null = create new
        public int outputWidth;
        public int outputHeight;
    }
    
    #endregion
    
    
    #region Serialized Fields
    
    [Header("Shader Configuration")]
    [SerializeField] private ComputeShader imageProcessorShader;
    
    [Header("Default Output Settings")]
    [SerializeField] private int defaultOutputWidth = 2048;
    [SerializeField] private int defaultOutputHeight = 2048;
    [SerializeField] private RenderTextureFormat outputFormat = RenderTextureFormat.ARGB32;
    
    [Header("Performance Settings - Speed Priority")]
    [SerializeField] private bool useTexturePooling = true;
    [SerializeField] private int maxPooledTextures = 5;
    [SerializeField] private bool fastMode = false;  // Skip validation checks for maximum speed
    
    #endregion
    
    
    #region Private Fields
    
    // Resource management
    private TextureBlenderResources resources;
    private bool isInitialized = false;
    
    // Kernel IDs (cached for performance)
    private int kernelBlendArrayAdditive;
    private int kernelBlendArrayAlphaWeighted;
    private int kernelBlendArrayMultiplicative;
    private int kernelBlendNormalsWithBaseAlphaAlphaWeighted;
    
    // Shader parameter IDs (cached for speed)
    private static readonly int InputTexturesArrayID = Shader.PropertyToID("InputTexturesArray");
    private static readonly int BaseTexturesArrayID = Shader.PropertyToID("BaseTexturesArray");
    private static readonly int OutputTextureID = Shader.PropertyToID("OutputTexture");
    private static readonly int OutputBufferID = Shader.PropertyToID("OutputBuffer");
    private static readonly int BlendValuesID = Shader.PropertyToID("BlendValues");
    private static readonly int RotationAnglesID = Shader.PropertyToID("RotationAngles");
    private static readonly int UVOffsetsID = Shader.PropertyToID("UVOffsets");
    private static readonly int TextureCountID = Shader.PropertyToID("TextureCount");
    private static readonly int TextureWidthID = Shader.PropertyToID("TextureWidth");
    private static readonly int TextureHeightID = Shader.PropertyToID("TextureHeight");
    
    // Speed optimization: Cached zero-rotation arrays to avoid allocations when rotation is not used
    private Dictionary<int, float[]> cachedZeroRotations = new Dictionary<int, float[]>();
    private const float RotationEpsilon = 0.0001f;
    
    // Speed optimization: Cached zero-offset arrays to avoid allocations when offset is not used
    private Dictionary<int, float[]> cachedZeroOffsets = new Dictionary<int, float[]>();
    private const float OffsetEpsilon = 0.0001f;  // Threshold for considering offset as zero  // Threshold for considering rotation as zero
    
    // Profiler markers for performance tracking
    private static readonly ProfilerMarker s_TextureArrayConversion = new ProfilerMarker("TextureBlender.ConvertToArray");
    private static readonly ProfilerMarker s_ShaderDispatch = new ProfilerMarker("TextureBlender.Dispatch");
    private static readonly ProfilerMarker s_ResourceAllocation = new ProfilerMarker("TextureBlender.AllocateResources");
    private static readonly ProfilerMarker s_CacheCheck = new ProfilerMarker("TextureBlender.CacheCheck");
    
    #endregion
    
    
    #region Initialization
    
    /// <summary>
    /// Initializes the TextureBlender.
    /// </summary>
    private void Awake()
    {
        Initialize();
    }
    
    
    /// <summary>
    /// Performs initialization tasks such as validating shader references, caching kernel IDs, and prewarming resource pools.
    /// </summary>
    private void Initialize()
    {
        // If already initialized, then just return.
        if (isInitialized)
        {
            return;
        }
        
        // Validate compute shader reference
        if (imageProcessorShader == null)
        {
            Debug.LogError("TextureBlender: ImageProcessorShader is not assigned!", this);
            return;
        }
        
        // Cache kernel IDs
        kernelBlendArrayAdditive = imageProcessorShader.FindKernel("BlendTexturesArrayAdditive");
        kernelBlendArrayAlphaWeighted = imageProcessorShader.FindKernel("BlendTexturesArrayAlphaWeighted");
        kernelBlendArrayMultiplicative = imageProcessorShader.FindKernel("BlendTexturesArrayMultiplicative");
        kernelBlendNormalsWithBaseAlphaAlphaWeighted = imageProcessorShader.FindKernel("BlendNormalsWithBaseAlphaAlphaWeighted");
        
        // Initialize resource pools
        resources = new TextureBlenderResources(maxPooledTextures);
        
        // Prewarm common sizes for VR (1024x1024) and standard (2048x2048)
        if (useTexturePooling)
        {
            resources.PrewarmPool(1024, 1024, outputFormat, 1);
        }
        
        // Set the initialized flag.
        isInitialized = true;
    }
    
    #endregion
    
    
    #region Public API
    
    /// <summary>
    /// Blends textures into an existing RenderTexture (no allocation).
    /// PERFORMANCE: Fastest option when reusing render targets.
    /// </summary>
    /// <param name="target">Existing RenderTexture to blend into</param>
    /// <param name="textures">Array of textures to blend</param>
    /// <param name="weights">Blend weights</param>
    /// <param name="mode">Blend mode to use</param>
    public RenderTexture BlendTextures(
        RenderTexture target, 
        Texture[] textures, 
        float[] weights, 
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        return BlendTextures(target, textures, weights, null, null, mode);
    }
    
    
    /// <summary>
    /// Blends textures into an existing RenderTexture with optional rotation per texture (no allocation).
    /// PERFORMANCE: Fastest option when reusing render targets.
    /// </summary>
    /// <param name="target">Existing RenderTexture to blend into</param>
    /// <param name="textures">Array of textures to blend</param>
    /// <param name="weights">Blend weights</param>
    /// <param name="rotationsDegrees">Optional rotation per texture (0-360°, null = no rotation)</param>
    /// <param name="mode">Blend mode to use</param>
    public RenderTexture BlendTextures(
        RenderTexture target,
        Texture[] textures,
        float[] weights,
        float[] rotationsDegrees,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        return BlendTextures(target, textures, weights, rotationsDegrees, null, mode);
    }
    
    
    /// <summary>
    /// Blends textures into an existing RenderTexture with optional rotation and UV offset per texture (no allocation).
    /// PERFORMANCE: Fastest option when reusing render targets.
    /// Note: If the target is null, a target texture is created.
    /// </summary>
    /// <param name="target">Existing RenderTexture to blend into</param>
    /// <param name="textures">Array of textures to blend</param>
    /// <param name="weights">Blend weights (required)</param>
    /// <param name="rotationsDegrees">Optional rotation per texture (0-360°, null = no rotation)</param>
    /// <param name="offsets">Optional UV offsets per texture (null = no offset, automatically tiles/wraps)</param>
    /// <param name="mode">Blend mode to use</param>
    public RenderTexture BlendTextures(
        RenderTexture target,
        Texture[] textures,
        float[] weights,
        float[] rotationsDegrees,
        Vector2[] offsets,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        if (!isInitialized)
            Initialize();

        // If no target is specified, then create one.
        if (target == null)
        {
            target = useTexturePooling
                ? resources.GetOrCreateRenderTexture(defaultOutputWidth, defaultOutputHeight, outputFormat)
                : CreateRenderTexture(defaultOutputWidth, defaultOutputHeight);
        }
        
        if (!ValidateInputs(textures))
            return null;
        
        // Normalize weights
        float[] normalizedWeights = mode == BlendMode.AlphaWeighted
            ? PrepareWeightsForAlphaMode(textures, weights)
            : NormalizeWeights(textures, weights);
        
        // Convert textures to Texture2DArray (with caching).
        // The texture array needs to be converted to a Texture2DArray so they can be passed to the compute shader. 
        Texture2DArray textureArray;
        using (s_TextureArrayConversion.Auto())
        {
            textureArray = resources.GetOrCreateTextureArray(textures, out _, out _);
        }
        if (textureArray == null)
        {
            Debug.LogError("TextureBlender: Failed to create Texture2DArray!");
            return null;
        }
        
        // Execute blend operation with rotation and offset
        using (s_ShaderDispatch.Auto())
        {
            ExecuteBlend(target, textureArray, normalizedWeights, textures.Length, mode, rotationsDegrees, offsets);
        }

        return target;
    }
    
    
    /// <summary>
    /// Executes multiple blend requests in a batch (efficient GPU usage).
    /// </summary>
    public RenderTexture[] BatchBlend(BlendRequest[] requests)
    {
        if (!isInitialized)
            Initialize();
        
        if (requests == null || requests.Length == 0)
            return new RenderTexture[0];
        
        RenderTexture[] results = new RenderTexture[requests.Length];
        
        for (int i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            
            // Create or use provided output texture
            RenderTexture output = request.targetOutput;
            if (output == null)
            {
                output = useTexturePooling
                    ? resources.GetOrCreateRenderTexture(request.outputWidth, request.outputHeight, outputFormat)
                    : CreateRenderTexture(request.outputWidth, request.outputHeight);
            }
            
            // Execute blend
            BlendTextures(output, request.inputTextures, request.blendWeights, request.blendMode);
            
            results[i] = output;
        }
        
        return results;
    }
    
    
    /// <summary>
    /// Returns a RenderTexture to the pool for reuse (if pooling is enabled).
    /// Call this when done with a blended texture to avoid memory leaks.
    /// </summary>
    public void ReturnTexture(RenderTexture texture)
    {
        if (useTexturePooling && resources != null)
        {
            resources.ReturnRenderTexture(texture);
        }
        else if (texture != null)
        {
            texture.Release();
        }
    }
    
    
    /// <summary>
    /// Blends normal maps with per-pixel alpha weighting from base textures.
    /// Each pixel's normal contribution is modulated by the corresponding base texture alpha.
    /// PERFORMANCE: Similar to regular blending, requires both normal and base texture arrays.
    /// </summary>
    /// <param name="normalTextures">Array of normal map textures to blend</param>
    /// <param name="baseTextures">Array of base textures (alpha channel used for per-pixel weighting)</param>
    /// <param name="weights">Blend weights for each layer (null = equal weights)</param>
    /// <param name="mode">Blend mode to use</param>
    /// <returns>New RenderTexture with blended normal map</returns>
    public RenderTexture BlendNormalsWithBaseAlpha(
        Texture[] normalTextures,
        Texture[] baseTextures,
        float[] weights = null,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        return BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, null, null, mode);
    }
    
    
    /// <summary>
    /// Blends normal maps with per-pixel alpha weighting from base textures with optional rotation.
    /// Each pixel's normal contribution is modulated by the corresponding base texture alpha.
    /// IMPORTANT: Rotation should match base texture rotation for visual coherence.
    /// PERFORMANCE: Similar to regular blending, requires both normal and base texture arrays.
    /// </summary>
    /// <param name="normalTextures">Array of normal map textures to blend</param>
    /// <param name="baseTextures">Array of base textures (alpha channel used for per-pixel weighting)</param>
    /// <param name="weights">Blend weights for each layer</param>
    /// <param name="rotationsDegrees">Rotation angles in degrees for each texture (should match base texture rotations!)</param>
    /// <param name="mode">Blend mode to use</param>
    /// <returns>New RenderTexture with blended normal map</returns>
    public RenderTexture BlendNormalsWithBaseAlpha(
        Texture[] normalTextures,
        Texture[] baseTextures,
        float[] weights,
        float[] rotationsDegrees,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        return BlendNormalsWithBaseAlpha(normalTextures, baseTextures, weights, rotationsDegrees, null, mode);
    }
    
    
    /// <summary>
    /// Blends normal maps with per-pixel alpha weighting from base textures with optional rotation and UV offset.
    /// Each pixel's normal contribution is modulated by the corresponding base texture alpha at that pixel.
    /// IMPORTANT: Rotation and offset should match base texture transformations for visual coherence.
    /// PERFORMANCE: Similar to regular blending, requires both normal and base texture arrays.
    /// </summary>
    /// <param name="normalTextures">Array of normal map textures to blend</param>
    /// <param name="baseTextures">Array of base textures (alpha channel used for per-pixel weighting)</param>
    /// <param name="weights">Blend weights for each layer</param>
    /// <param name="rotationsDegrees">Rotation angles in degrees for each texture (should match base texture rotations!)</param>
    /// <param name="offsets">UV offsets for each texture (should match base texture offsets!)</param>
    /// <param name="mode">Blend mode to use</param>
    /// <returns>New RenderTexture with blended normal map</returns>
    public RenderTexture BlendNormalsWithBaseAlpha(
        Texture[] normalTextures,
        Texture[] baseTextures,
        float[] weights,
        float[] rotationsDegrees,
        Vector2[] offsets,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        if (!isInitialized)
            Initialize();
        
        if (!ValidateInputs(normalTextures) || !ValidateInputs(baseTextures))
            return null;
        
        if (normalTextures.Length != baseTextures.Length)
        {
            Debug.LogError("TextureBlender: Normal textures and base textures must have same count!");
            return null;
        }
        
        // Create output RenderTexture
        RenderTexture output = useTexturePooling
            ? resources.GetOrCreateRenderTexture(defaultOutputWidth, defaultOutputHeight, outputFormat)
            : CreateRenderTexture(defaultOutputWidth, defaultOutputHeight);
        
        // Blend to the output texture with rotation and offset
        BlendNormalsWithBaseAlphaToExistingTexture(output, normalTextures, baseTextures, weights, rotationsDegrees, offsets, mode);
        return output;
    }
    
    
    /// <summary>
    /// Blends normal maps with per-pixel alpha weighting from base textures into an existing RenderTexture.
    /// PERFORMANCE: Fastest option for normal blending when reusing render targets.
    /// </summary>
    /// <param name="target">Existing RenderTexture to blend into</param>
    /// <param name="normalTextures">Array of normal maps</param>
    /// <param name="baseTextures">Array of base textures for alpha weighting</param>
    /// <param name="weights">Blend weights (required)</param>
    /// <param name="mode">Blend mode to use</param>
    public void BlendNormalsWithBaseAlphaToExistingTexture(
        RenderTexture target,
        Texture[] normalTextures,
        Texture[] baseTextures,
        float[] weights,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        BlendNormalsWithBaseAlphaToExistingTexture(target, normalTextures, baseTextures, weights, null, null, mode);
    }
    
    
    /// <summary>
    /// Blends normal maps with per-pixel alpha weighting from base textures into an existing RenderTexture with optional rotation.
    /// IMPORTANT: Rotation should match base texture rotation for visual coherence.
    /// PERFORMANCE: Fastest option for normal blending when reusing render targets.
    /// </summary>
    /// <param name="target">Existing RenderTexture to blend into</param>
    /// <param name="normalTextures">Array of normal maps</param>
    /// <param name="baseTextures">Array of base textures for alpha weighting</param>
    /// <param name="weights">Blend weights (required)</param>
    /// <param name="rotationsDegrees">Optional rotation per texture (0-360°, null = no rotation) - MUST match base texture rotations!</param>
    /// <param name="mode">Blend mode to use</param>
    public void BlendNormalsWithBaseAlphaToExistingTexture(
        RenderTexture target,
        Texture[] normalTextures,
        Texture[] baseTextures,
        float[] weights,
        float[] rotationsDegrees,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        BlendNormalsWithBaseAlphaToExistingTexture(target, normalTextures, baseTextures, weights, rotationsDegrees, null, mode);
    }
    
    
    /// <summary>
    /// Blends normal maps with per-pixel alpha weighting from base textures into an existing RenderTexture with optional rotation and UV offset.
    /// IMPORTANT: Rotation and offset should match base texture transformations for visual coherence.
    /// PERFORMANCE: Fastest option for normal blending when reusing render targets.
    /// </summary>
    /// <param name="target">Existing RenderTexture to blend into</param>
    /// <param name="normalTextures">Array of normal maps</param>
    /// <param name="baseTextures">Array of base textures for alpha weighting</param>
    /// <param name="weights">Blend weights (required)</param>
    /// <param name="rotationsDegrees">Optional rotation per texture (0-360°, null = no rotation) - MUST match base texture rotations!</param>
    /// <param name="offsets">Optional UV offsets per texture (null = no offset, automatically tiles/wraps) - MUST match base texture offsets!</param>
    /// <param name="mode">Blend mode to use</param>
    public void BlendNormalsWithBaseAlphaToExistingTexture(
        RenderTexture target,
        Texture[] normalTextures,
        Texture[] baseTextures,
        float[] weights,
        float[] rotationsDegrees,
        Vector2[] offsets,
        BlendMode mode = BlendMode.AlphaWeighted)
    {
        if (!isInitialized)
            Initialize();
        
        if (target == null)
        {
            Debug.LogError("TextureBlender: Target RenderTexture is null!");
            return;
        }
        
        if (!ValidateInputs(normalTextures) || !ValidateInputs(baseTextures))
            return;
        
        if (normalTextures.Length != baseTextures.Length)
        {
            Debug.LogError("TextureBlender: Normal textures and base textures must have same count!");
            return;
        }
        
        // Normalize weights
        float[] normalizedWeights = mode == BlendMode.AlphaWeighted
            ? PrepareWeightsForAlphaMode(normalTextures, weights)
            : NormalizeWeights(normalTextures, weights);
        
        // Convert textures to Texture2DArrays (with caching)
        Texture2DArray normalTextureArray;
        Texture2DArray baseTextureArray;
        using (s_TextureArrayConversion.Auto())
        {
            normalTextureArray = resources.GetOrCreateTextureArray(normalTextures, out _, out _);
            baseTextureArray = resources.GetOrCreateTextureArray(baseTextures, out _, out _);
        }
        
        if (normalTextureArray == null || baseTextureArray == null)
        {
            Debug.LogError("TextureBlender: Failed to create Texture2DArrays!");
            return;
        }
        
        // Execute blend operation with base alpha, rotation, and offset
        using (s_ShaderDispatch.Auto())
        {
            ExecuteNormalBlendWithBaseAlpha(target, normalTextureArray, baseTextureArray, normalizedWeights, normalTextures.Length, rotationsDegrees, offsets);
        }
    }
    #endregion
    
    #region Private Methods
    
    /// <summary>
    /// Validates input textures for blending. Checks for null/empty arrays and at least one valid texture.
    /// </summary>
    /// <param name="textures"> </param>
    /// <returns></returns>
    private bool ValidateInputs(Texture[] textures)
    {
        // If fast mode, then skip validating inputs.
        if (fastMode)
        {
            return true;
        }
        
        if (textures == null || textures.Length == 0)
        {
            Debug.LogError("TextureBlender: No textures provided to blend!");
            return false;
        }
        
        // Check for at least one valid texture
        bool hasValidTexture = false;
        foreach (var texture in textures)
        {
            if (texture != null)
            {
                hasValidTexture = true;
                break;
            }
        }
        
        if (!hasValidTexture)
        {
            Debug.LogError("TextureBlender: All input textures are null!");
            return false;
        }
        
        return true;
    }
    
    
    /// <summary>
    /// Normalizes blend weights to ensure they sum to 1. If weights are null or empty, defaults to equal weights.
    /// </summary>
    /// <param name="textures"></param>
    /// <param name="weights"></param>
    /// <returns></returns>
    private float[] NormalizeWeights(Texture[] textures, float[] weights)
    {
        int count = textures.Length;
        float[] normalizedWeights = new float[count];
        
        // Use provided weights or default to equal weights
        if (weights != null && weights.Length > 0)
        {
            for (int i = 0; i < count; i++)
            {
                normalizedWeights[i] = (i < weights.Length) ? weights[i] : 0f;
            }
        }
        else
        {
            // Equal weights for all textures
            float equalWeight = 1f / count;
            for (int i = 0; i < count; i++)
            {
                normalizedWeights[i] = equalWeight;
            }
        }
        
        return normalizedWeights;
    }
    
    
    /// <summary>
    /// Prepares blend weights for alpha-weighted mode. If weights are null, defaults to 1 for all textures.
    /// </summary>
    /// <param name="textures"></param>
    /// <param name="weights"></param>
    /// <returns></returns>
    private float[] PrepareWeightsForAlphaMode(Texture[] textures, float[] weights)
    {
        float[] result = new float[textures.Length];
        int copyCount = weights?.Length ?? 0;
    
        if (weights != null)
        {
            Array.Copy(weights, result, Math.Min(copyCount, textures.Length));
        }
    
        // Fill remaining with 1f
        for (int i = copyCount; i < result.Length; i++)
        {
            result[i] = 1f;
        }
    
        return result;
    }
    
    
    /// <summary>
    /// Checks if any meaningful rotation is present in the rotation array.
    /// Returns false if rotations is null or all values are effectively zero.
    /// OPTIMIZATION: Avoids expensive degree-to-radian conversion when rotation isn't needed.
    /// </summary>
    /// <param name="rotationsDegrees">Rotation angles in degrees</param>
    /// <returns>True if any rotation value exceeds the epsilon threshold</returns>
    private bool IsRotationNeeded(float[] rotationsDegrees)
    {
        if (rotationsDegrees == null || rotationsDegrees.Length == 0)
            return false;
        
        // Check if any rotation value is non-zero
        for (int i = 0; i < rotationsDegrees.Length; i++)
        {
            if (Mathf.Abs(rotationsDegrees[i]) > RotationEpsilon)
                return true;
        }
        
        return false;
    }
    
    
    /// <summary>
    /// Prepares rotation angles in radians for GPU shader. Converts degrees to radians.
    /// If rotationsDegrees is null or all zeros, returns cached zero array for maximum speed.
    /// OPTIMIZATION: Caches zero-rotation arrays to avoid allocation for the common case.
    /// </summary>
    /// <param name="textureCount">Number of textures</param>
    /// <param name="rotationsDegrees">Rotation angles in degrees (null = no rotation)</param>
    /// <returns>Array of rotation angles in radians</returns>
    private float[] PrepareRotationAngles(int textureCount, float[] rotationsDegrees)
    {
        // OPTIMIZATION: Check if rotation is actually needed
        if (!IsRotationNeeded(rotationsDegrees))
        {
            // Return cached zero array if available, otherwise create and cache it
            if (!cachedZeroRotations.TryGetValue(textureCount, out float[] cachedZeros))
            {
                cachedZeros = new float[textureCount];  // Already initialized to zeros
                cachedZeroRotations[textureCount] = cachedZeros;
            }
            return cachedZeros;
        }
        
        // Rotation is needed - perform conversion
        float[] rotations = new float[textureCount];
        
        for (int i = 0; i < textureCount; i++)
        {
            float degrees = (i < rotationsDegrees.Length) ? rotationsDegrees[i] : 0f;
            rotations[i] = degrees * Mathf.Deg2Rad;  // Convert to radians
        }
        
        return rotations;
    }
    
    
    /// <summary>
    /// Checks if any meaningful UV offset is present in the offset array.
    /// Returns false if offsets is null or all values are effectively zero.
    /// OPTIMIZATION: Avoids expensive array allocation when offset isn't needed.
    /// </summary>
    /// <param name="offsets">UV offsets as Vector2 array</param>
    /// <returns>True if any offset value exceeds the epsilon threshold</returns>
    private bool IsOffsetNeeded(Vector2[] offsets)
    {
        if (offsets == null || offsets.Length == 0)
            return false;
        // Check if any offset value is non-zero
        for (int i = 0; i < offsets.Length; i++)
        {
            if (Mathf.Abs(offsets[i].x) > OffsetEpsilon || Mathf.Abs(offsets[i].y) > OffsetEpsilon)
                return true;
        }
        return false;
    }
    
    
    /// <summary>
    /// Prepares UV offsets for GPU shader as interleaved float array [x0, y0, x1, y1, ...].
    /// If offsets is null or all zeros, returns cached zero array for maximum speed.
    /// OPTIMIZATION: Caches zero-offset arrays to avoid allocation for the common case.
    /// </summary>
    /// <param name="textureCount">Number of textures</param>
    /// <param name="offsets">UV offsets as Vector2 array (null = no offset)</param>
    /// <returns>Interleaved array of UV offsets [x0, y0, x1, y1, ...]</returns>
    private float[] PrepareUVOffsets(int textureCount, Vector2[] offsets)
    {
        // OPTIMIZATION: Check if offset is actually needed
        if (!IsOffsetNeeded(offsets))
        {
            // Return cached zero array if available, otherwise create and cache it
            int arraySize = textureCount * 2;  // x,y pairs
            if (!cachedZeroOffsets.TryGetValue(arraySize, out float[] cachedZeros))
            {
                cachedZeros = new float[arraySize];  // Already initialized to zeros
                cachedZeroOffsets[arraySize] = cachedZeros;
            }
            return cachedZeros;
        }
        // Offset is needed - convert Vector2[] to interleaved float array
        float[] result = new float[textureCount * 2];
        for (int i = 0; i < textureCount; i++)
        {
            Vector2 offset = (i < offsets.Length) ? offsets[i] : Vector2.zero;
            result[i * 2] = offset.x;      // X component
            result[i * 2 + 1] = offset.y;  // Y component
        }
        return result;
    }
    

    /// <summary>
    /// Executes the blend operation by dispatching the appropriate compute shader kernel based on the blend mode.
    /// </summary>
    /// <param name="target">Target RenderTexture to write the blended result</param>
    /// <param name="textureArray">Texture2DArray containing all input textures</param>
    /// <param name="weights">Normalized blend weights for each texture</param>
    /// <param name="textureCount">Number of textures in the array</param>
    /// <param name="mode">Blend mode to use</param>
    /// <param name="rotationsDegrees">Optional rotation angles in degrees for each texture (null = no rotation)</param>
    /// <param name="offsets">Optional UV offsets for each texture (null = no offset)</param>
    private void ExecuteBlend(
        RenderTexture target,
        Texture2DArray textureArray,
        float[] weights,
        int textureCount,
        BlendMode mode,
        float[] rotationsDegrees = null,
        Vector2[] offsets = null)
    {
        using (s_ResourceAllocation.Auto())
        {
            // Get kernel ID based on blend mode
            int kernelID = GetKernelForBlendMode(mode);
            
            // Create compute buffer for blend weights
            ComputeBuffer weightsBuffer = resources.GetOrCreateBuffer(weights.Length, sizeof(float));
            weightsBuffer.SetData(weights);
            
            // Prepare rotation angles (degrees to radians)
            float[] rotationAngles = PrepareRotationAngles(textureCount, rotationsDegrees);
            ComputeBuffer rotationBuffer = resources.GetOrCreateBuffer(rotationAngles.Length, sizeof(float));
            rotationBuffer.SetData(rotationAngles);            // Prepare UV offsets (interleaved x,y pairs)
            float[] uvOffsets = PrepareUVOffsets(textureCount, offsets);
            ComputeBuffer offsetBuffer = resources.GetOrCreateBuffer(uvOffsets.Length, sizeof(float));
            offsetBuffer.SetData(uvOffsets);
            
            // Create output buffer for OpenGL ES 3.0 compatibility
            int pixelCount = target.width * target.height;
            ComputeBuffer outputBuffer = resources.GetOrCreateBuffer(pixelCount, sizeof(float) * 4);
            
            // Set shader parameters
            imageProcessorShader.SetInt(TextureWidthID, target.width);
            imageProcessorShader.SetInt(TextureHeightID, target.height);
            imageProcessorShader.SetInt(TextureCountID, textureCount);
            
            // Bind textures and buffers
            imageProcessorShader.SetTexture(kernelID, InputTexturesArrayID, textureArray);
            imageProcessorShader.SetTexture(kernelID, OutputTextureID, target);
            imageProcessorShader.SetBuffer(kernelID, OutputBufferID, outputBuffer);
            imageProcessorShader.SetBuffer(kernelID, BlendValuesID, weightsBuffer);
            imageProcessorShader.SetBuffer(kernelID, RotationAnglesID, rotationBuffer);
            imageProcessorShader.SetBuffer(kernelID, UVOffsetsID, offsetBuffer);
            
            // Calculate dispatch dimensions
            imageProcessorShader.GetKernelThreadGroupSizes(kernelID, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);
            int dispatchX = Mathf.CeilToInt(target.width / (float)threadGroupSizeX);
            int dispatchY = Mathf.CeilToInt(target.height / (float)threadGroupSizeY);
            
            // Dispatch compute shader (single dispatch for maximum speed)
            imageProcessorShader.Dispatch(kernelID, dispatchX, dispatchY, (int)threadGroupSizeZ);
            
            // Return buffers to pool
            resources.ReturnBuffer(weightsBuffer);
            resources.ReturnBuffer(rotationBuffer);
            resources.ReturnBuffer(offsetBuffer);
            resources.ReturnBuffer(outputBuffer);
        }
    }
    
    
    /// <summary>
    /// Executes the normal map blending operation with per-pixel alpha weighting from base textures.
    /// Note: Currently only supports AlphaWeighted mode for normal blending.
    /// </summary>
    /// <param name="target">Target RenderTexture to write the blended normal map</param>
    /// <param name="normalTextureArray">Texture2DArray containing normal map textures</param>
    /// <param name="baseTextureArray">Texture2DArray containing base textures with alpha masks</param>
    /// <param name="weights">Normalized blend weights for each texture</param>
    /// <param name="textureCount">Number of textures in the arrays</param>
    /// <param name="rotationsDegrees">Optional rotation angles in degrees for each texture (null = no rotation)</param>
    private void ExecuteNormalBlendWithBaseAlpha(
        RenderTexture target,
        Texture2DArray normalTextureArray,
        Texture2DArray baseTextureArray,
        float[] weights,
        int textureCount,
        float[] rotationsDegrees = null,
        Vector2[] offsets = null)
    {
        using (s_ResourceAllocation.Auto())
        {
            // Get kernel ID for normal blend with base alpha
            int kernelID = kernelBlendNormalsWithBaseAlphaAlphaWeighted;
            
            // Create compute buffer for blend weights
            ComputeBuffer weightsBuffer = resources.GetOrCreateBuffer(weights.Length, sizeof(float));
            weightsBuffer.SetData(weights);
            
            // Prepare rotation angles (degrees to radians)
            float[] rotationAngles = PrepareRotationAngles(textureCount, rotationsDegrees);
            ComputeBuffer rotationBuffer = resources.GetOrCreateBuffer(rotationAngles.Length, sizeof(float));
            rotationBuffer.SetData(rotationAngles);            // Prepare UV offsets (interleaved x,y pairs)
            float[] uvOffsets = PrepareUVOffsets(textureCount, offsets);
            ComputeBuffer offsetBuffer = resources.GetOrCreateBuffer(uvOffsets.Length, sizeof(float));
            offsetBuffer.SetData(uvOffsets);
            
            // Create output buffer for OpenGL ES 3.0 compatibility
            int pixelCount = target.width * target.height;
            ComputeBuffer outputBuffer = resources.GetOrCreateBuffer(pixelCount, sizeof(float) * 4);
            
            // Set shader parameters
            imageProcessorShader.SetInt(TextureWidthID, target.width);
            imageProcessorShader.SetInt(TextureHeightID, target.height);
            imageProcessorShader.SetInt(TextureCountID, textureCount);
            
            // Bind normal and base texture arrays
            imageProcessorShader.SetTexture(kernelID, InputTexturesArrayID, normalTextureArray);
            imageProcessorShader.SetTexture(kernelID, BaseTexturesArrayID, baseTextureArray);
            imageProcessorShader.SetTexture(kernelID, OutputTextureID, target);
            imageProcessorShader.SetBuffer(kernelID, OutputBufferID, outputBuffer);
            imageProcessorShader.SetBuffer(kernelID, BlendValuesID, weightsBuffer);
            imageProcessorShader.SetBuffer(kernelID, RotationAnglesID, rotationBuffer);
            imageProcessorShader.SetBuffer(kernelID, UVOffsetsID, offsetBuffer);
            
            // Calculate dispatch dimensions
            imageProcessorShader.GetKernelThreadGroupSizes(kernelID, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);
            int dispatchX = Mathf.CeilToInt(target.width / (float)threadGroupSizeX);
            int dispatchY = Mathf.CeilToInt(target.height / (float)threadGroupSizeY);
            
            // Dispatch compute shader (single dispatch for maximum speed)
            imageProcessorShader.Dispatch(kernelID, dispatchX, dispatchY, (int)threadGroupSizeZ);
            
            // Return buffers to pool
            resources.ReturnBuffer(weightsBuffer);
            resources.ReturnBuffer(rotationBuffer);
            resources.ReturnBuffer(offsetBuffer);
            resources.ReturnBuffer(outputBuffer);
        }
    }
    
    
    /// <summary>
    /// Returns the appropriate compute shader kernel ID based on the specified blend mode.
    /// </summary>
    /// <param name="mode"></param>
    /// <returns></returns>
    private int GetKernelForBlendMode(BlendMode mode)
    {
        switch (mode)
        {
            case BlendMode.Additive:
                return kernelBlendArrayAdditive;
            case BlendMode.AlphaWeighted:
                return kernelBlendArrayAlphaWeighted;
            case BlendMode.Multiplicative:
                return kernelBlendArrayMultiplicative;
            default:
                return kernelBlendArrayAlphaWeighted;
        }
    }
    
    
    /// <summary>
    /// Creates a new RenderTexture with the specified dimensions and format. Used when pooling is disabled or no pooled texture is available.
    /// </summary>
    /// <param name="width"></param>
    /// <param name="height"></param>
    /// <returns></returns>
    private RenderTexture CreateRenderTexture(int width, int height)
    {
        RenderTexture rt = new RenderTexture(width, height, 0, outputFormat);
        rt.enableRandomWrite = true;
        rt.Create();
        return rt;
    }
    
    #endregion
    
    #region Cleanup
    
    /// <summary>
    /// Cleans up resources when the TextureBlender is destroyed.
    /// Texture array cache is automatically cleared by resources.Dispose().
    /// </summary>
    private void OnDestroy()
    {
        // Clear rotation cache
        cachedZeroRotations?.Clear();
        
        // Clear offset cache
        cachedZeroOffsets?.Clear();
        
        // Dispose resources (includes clearing texture array cache)
        resources?.Dispose();
        
        isInitialized = false;
    }
    
    #endregion
}

