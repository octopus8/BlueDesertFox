using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;


[RequireComponent(typeof(SplineContainer))]
public class SplineComponentAuthoring : MonoBehaviour
{
    [Tooltip("Number of samples to pre-calculate along the spline. Higher values = more accuracy but more memory.")]
    public int sampleCount = 100;
    
    public class SplineComponentBaker: Baker<SplineComponentAuthoring>
    {
        public override void Bake(SplineComponentAuthoring authoring)
        {
            var splineContainer = GetComponent<SplineContainer>();

            if (splineContainer is null)
            {
                Debug.Log($"From {nameof(SplineComponentBaker)}.Bake(). spline container is null");
                return;
            }

            var spline = splineContainer.Spline;
            float4x4 transformationMatrix = splineContainer.transform.localToWorldMatrix;
            
            // Create the pre-sampled spline data blob asset
            var splineDataBlobAssetRef = SplineDataBlob.CreateSplineDataBlobAssetRef(
                spline,
                transformationMatrix,
                authoring.sampleCount);
       
            var entity = GetEntity(TransformUsageFlags.Dynamic);
       
            AddBlobAsset(ref splineDataBlobAssetRef, out _);
       
            AddComponent(entity, new SplineDataComponent
            {
                splineData = splineDataBlobAssetRef,
            });
        }
    }
}


// Component to hold reference to the pre-sampled spline data
public struct SplineDataComponent : IComponentData
{
    public BlobAssetReference<SplineDataBlob> splineData;
}

// Struct to hold a single sample point along the spline
public struct SplineSample
{
    public float3 position;
    public float3 tangent;
    public float3 upVector;
}

// Blob asset containing pre-sampled spline data
public struct SplineDataBlob
{
    public BlobArray<SplineSample> samples;
    public float totalLength;
    public bool isClosed;
    
    /// <summary>
    /// Evaluates the spline at the given distance ratio (0-1) using the pre-sampled data.
    /// Uses linear interpolation between samples.
    /// </summary>
    public SplineSample Evaluate(float t)
    {
        // Clamp or wrap the t value
        if (isClosed)
        {
            t = t - math.floor(t); // Wrap around for closed splines
        }
        else
        {
            t = math.clamp(t, 0f, 1f);
        }
        
        // Find the appropriate sample index
        float floatIndex = t * (samples.Length - 1);
        int index0 = (int)math.floor(floatIndex);
        int index1 = math.min(index0 + 1, samples.Length - 1);
        
        // Handle closed loop wrapping
        if (isClosed && index1 >= samples.Length)
        {
            index1 = 0;
        }
        
        float fraction = floatIndex - index0;
        
        // Interpolate between samples
        SplineSample result;
        result.position = math.lerp(samples[index0].position, samples[index1].position, fraction);
        result.tangent = math.normalize(math.lerp(samples[index0].tangent, samples[index1].tangent, fraction));
        result.upVector = math.normalize(math.lerp(samples[index0].upVector, samples[index1].upVector, fraction));
        
        return result;
    }

    public static BlobAssetReference<SplineDataBlob> CreateSplineDataBlobAssetRef(
        Spline spline,
        float4x4 transformMatrix,
        int sampleCount)
    {
        // Create a temporary native spline to sample from
        using var nativeSpline = new NativeSpline(spline, transformMatrix, Allocator.Temp);
        
        float splineLength = nativeSpline.GetLength();
        
        // Create the blob asset
        using var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<SplineDataBlob>();
        
        // Allocate array for samples
        var samplesBuilder = builder.Allocate(ref root.samples, sampleCount);
        
        // Sample the spline at regular intervals
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)(sampleCount - 1);
            
            samplesBuilder[i] = new SplineSample
            {
                position = nativeSpline.EvaluatePosition(t),
                tangent = math.normalize(nativeSpline.EvaluateTangent(t)),
                upVector = nativeSpline.EvaluateUpVector(t)
            };
        }
        
        root.totalLength = splineLength;
        root.isClosed = spline.Closed;
        
        return builder.CreateBlobAssetReference<SplineDataBlob>(Allocator.Persistent);
    }
}

