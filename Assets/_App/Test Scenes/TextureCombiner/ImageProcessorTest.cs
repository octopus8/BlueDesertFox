using UnityEngine;

public class ImageProcessorTest : MonoBehaviour
{
    [SerializeField] private ComputeShader imageProcessorComputeShader;
    [SerializeField] private Texture[]  textures;

    [SerializeField] private float[] blendValues;
    
    [SerializeField] private MeshRenderer meshRenderer;

    /// <summary>
    /// The output texture. A RenderTexture is used instead of a Texture to keep all data on the GPU - the compute shader
    /// writes directly to the RenderTexture, which is then used by the material without any CPU readback. This is the most performant approach.
    /// </summary>
    private RenderTexture outputTexture;
    
    /// <summary>
    /// A ComputeBuffer is used to store the output data from the compute shader. This is necessary for OpenGL ES 3.0 compatibility,
    /// as some platforms require a buffer to read back data from the GPU. The compute shader writes the pixel data
    /// to this buffer, which can then be used to update the RenderTexture or read back to the CPU if needed.
    /// </summary>
    private ComputeBuffer outputBuffer;
    
    /// <summary>
    /// A ComputeBuffer to pass blend weights for each texture to the GPU.
    /// </summary>
    private ComputeBuffer blendValuesBuffer;
    
    /// <summary>
    /// A small dummy texture used for empty/null texture slots to prevent shader errors.
    /// </summary>
    private Texture2D dummyTexture;
    
    // Kernel Names
    private const string setTextureRedBlueGradientKernelName = "SetTextureRedBlueGradient";
    private const string blendTexturesKernelName = "BlendTextures";
    
    // Parameter Names
    private const string textureWidthShaderParameter = "TextureWidth";
    private const string textureHeightShaderParameter = "TextureHeight";
    private const string outputTextureShaderParameter = "OutputTexture";
    private const string outputBufferShaderParameter = "OutputBuffer";
    private const string textureCountShaderParameter = "TextureCount";
    private const string blendValuesBufferParameter = "BlendValues";

    private void Start()
    {
        // Create a small black dummy texture for empty/null texture slots
        CreateDummyTexture();
        
        // Test blending textures
        // Define output texture dimensions
        int outputWidth = 2048;
        int outputHeight = 2048;
        
        // Create RenderTexture for compute shader output
        outputTexture = new RenderTexture(outputWidth, outputHeight, 0, RenderTextureFormat.ARGB32);
        outputTexture.enableRandomWrite = true;
        outputTexture.Create();
        
        TestBlendTextures(outputTexture);
        
        // Apply the resulting texture to the mesh renderer's material
        meshRenderer.material.mainTexture = outputTexture;
        
        
        // Uncomment to test gradient instead
        // DoTest();
    }

    /// <summary>
    /// Creates a small 1x1 black texture to use for empty/null texture slots.
    /// This prevents shader errors when not all texture slots are filled.
    /// </summary>
    private void CreateDummyTexture()
    {
        dummyTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        dummyTexture.SetPixel(0, 0, new Color(0, 0, 0, 0));
        dummyTexture.Apply();
    }


    private void DoTest()
    {
        // Validate required references
        if (imageProcessorComputeShader == null)
        {
            Debug.LogError("ImageProcessorComputeShader is not assigned!");
            return;
        }
        
        if (meshRenderer == null)
        {
            Debug.LogError("MeshRenderer is not assigned!");
            return;
        }

        int textureWidth = 1024;
        int textureHeight = 1024;
        int pixelCount = textureWidth * textureHeight;

        // Create RenderTexture for compute shader output
        outputTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32);
        outputTexture.enableRandomWrite = true;
        outputTexture.Create();

        // Create ComputeBuffer for output (needed for OpenGL ES 3.0 compatibility)
        outputBuffer = new ComputeBuffer(pixelCount, sizeof(float) * 4);

        // Find the SetTextureUV kernel
        int setTextureRedBlueGradientKernelID = imageProcessorComputeShader.FindKernel(setTextureRedBlueGradientKernelName);

        int currentKernelID = setTextureRedBlueGradientKernelID;

        // Set the texture width and height parameters for buffer indexing
        imageProcessorComputeShader.SetInt(textureWidthShaderParameter, textureWidth);
        imageProcessorComputeShader.SetInt(textureHeightShaderParameter, textureHeight);

        // Bind the RenderTexture to the compute shader
        imageProcessorComputeShader.SetTexture(currentKernelID, outputTextureShaderParameter, outputTexture);
        
        // Bind the ComputeBuffer to the compute shader
        imageProcessorComputeShader.SetBuffer(currentKernelID, outputBufferShaderParameter, outputBuffer);
        
        // Get the kernel thread group sizes.
        imageProcessorComputeShader.GetKernelThreadGroupSizes(currentKernelID, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);

        // Dispatch the compute shader with enough thread groups to cover the entire texture
        imageProcessorComputeShader.Dispatch(currentKernelID, textureWidth / (int)threadGroupSizeX, textureHeight / (int)threadGroupSizeY, (int)threadGroupSizeZ);

        // Apply the resulting texture to the mesh renderer's material
        meshRenderer.material.mainTexture = outputTexture;
    }
    
    /// <summary>
    /// Tests blending multiple textures using the BlendTextures compute shader kernel.
    /// Blends all textures in the textures array and displays the result on the mesh renderer.
    /// </summary>
    public void TestBlendTextures(RenderTexture outputTexture)
    {
        // Validate required references
        if (imageProcessorComputeShader == null)
        {
            Debug.LogError("ImageProcessorComputeShader is not assigned!");
            return;
        }
        
        if (meshRenderer == null)
        {
            Debug.LogError("MeshRenderer is not assigned!");
            return;
        }
        
        if (textures == null || textures.Length == 0)
        {
            Debug.LogError("No textures assigned to blend!");
            return;
        }
        
        // Clamp texture count to maximum supported (8 textures)
        int textureCount = Mathf.Min(textures.Length, 8);
        
        // Prepare blend values array (use provided values or default to equal weights)
        float[] blendWeights = new float[8];
        float totalWeight = 0f;
        
        for (int i = 0; i < textureCount; i++)
        {
            if (blendValues != null && i < blendValues.Length)
            {
                blendWeights[i] = blendValues[i];
            }
            else
            {
                // Default to equal weight if not specified
                blendWeights[i] = 1.0f;
            }
            totalWeight += blendWeights[i];
        }
        
        // Create ComputeBuffer for output (needed for OpenGL ES 3.0 compatibility)
        int pixelCount = outputTexture.width * outputTexture.height;
        outputBuffer = new ComputeBuffer(pixelCount, sizeof(float) * 4);
        
        // Create ComputeBuffer for blend values (8 floats for up to 8 textures)
        blendValuesBuffer = new ComputeBuffer(8, sizeof(float));
        blendValuesBuffer.SetData(blendWeights);
        
        // Find the BlendTextures kernel
        int blendTexturesKernelID = imageProcessorComputeShader.FindKernel(blendTexturesKernelName);
        
        // Set texture dimensions
        imageProcessorComputeShader.SetInt(textureWidthShaderParameter, outputTexture.width);
        imageProcessorComputeShader.SetInt(textureHeightShaderParameter, outputTexture.height);
        
        // Set texture count
        imageProcessorComputeShader.SetInt(textureCountShaderParameter, textureCount);
        
        // Bind output texture and buffer
        imageProcessorComputeShader.SetTexture(blendTexturesKernelID, outputTextureShaderParameter, outputTexture);
        imageProcessorComputeShader.SetBuffer(blendTexturesKernelID, outputBufferShaderParameter, outputBuffer);
        
        // Bind blend values buffer
        imageProcessorComputeShader.SetBuffer(blendTexturesKernelID, blendValuesBufferParameter, blendValuesBuffer);
        
        // Bind input textures (always bind all 8 slots to prevent shader errors)
        for (int i = 0; i < 8; i++)
        {
            string inputTextureName = $"InputTexture{i}";
            
            // Use actual texture if available and valid, otherwise use dummy texture
            if (i < textureCount && textures[i] != null)
            {
                imageProcessorComputeShader.SetTexture(blendTexturesKernelID, inputTextureName, textures[i]);
            }
            else
            {
                // Bind dummy texture for unused/null slots (prevents shader errors)
                imageProcessorComputeShader.SetTexture(blendTexturesKernelID, inputTextureName, dummyTexture);
                if (i < textureCount)
                {
                    Debug.LogWarning($"Texture slot {i} is null, using dummy texture");
                }
            }
        }
        
        // Get the kernel thread group sizes
        imageProcessorComputeShader.GetKernelThreadGroupSizes(blendTexturesKernelID, out uint threadGroupSizeX, out uint threadGroupSizeY, out uint threadGroupSizeZ);
        
        // Calculate dispatch dimensions
        int dispatchX = Mathf.CeilToInt(outputTexture.width / (float)threadGroupSizeX);
        int dispatchY = Mathf.CeilToInt(outputTexture.height / (float)threadGroupSizeY);
        
        // Dispatch the compute shader
        imageProcessorComputeShader.Dispatch(blendTexturesKernelID, dispatchX, dispatchY, (int)threadGroupSizeZ);
    }

    private void OnDestroy()
    {
        // Release RenderTexture to prevent memory leaks
        outputTexture?.Release();
        
        // Release ComputeBuffer to prevent memory leaks
        outputBuffer?.Release();
        
        // Release blend values buffer
        blendValuesBuffer?.Release();
        
        // Destroy dummy texture
        if (dummyTexture != null)
        {
            Destroy(dummyTexture);
        }
    }
}
