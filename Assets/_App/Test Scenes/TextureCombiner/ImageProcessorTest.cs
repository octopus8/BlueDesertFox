using UnityEngine;

public class ImageProcessorTest : MonoBehaviour
{
    [SerializeField] private ComputeShader imageProcessorComputeShader;
    [SerializeField] private Texture[]  textures;
    
    [SerializeField] private MeshRenderer meshRenderer;

    private RenderTexture resultTexture;

    private void Start()
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

        // Create RenderTexture for compute shader output
        resultTexture = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
        resultTexture.enableRandomWrite = true;
        resultTexture.Create();

        // Find the SetTextureUV kernel
        int kernelIndex = imageProcessorComputeShader.FindKernel("SetTextureUV");

        // Bind the RenderTexture to the compute shader
        imageProcessorComputeShader.SetTexture(kernelIndex, "Result", resultTexture);

        // Dispatch the compute shader (1024/8 = 128 thread groups per dimension)
        imageProcessorComputeShader.Dispatch(kernelIndex, 128, 128, 1);

        // Apply the resulting texture to the mesh renderer's material
        meshRenderer.material.mainTexture = resultTexture;
    }

    private void OnDestroy()
    {
        // Release RenderTexture to prevent memory leaks
        resultTexture?.Release();
    }
}
