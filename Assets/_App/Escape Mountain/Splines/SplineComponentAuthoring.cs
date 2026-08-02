using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;


/// <summary>
/// Authoring component that bakes a Unity <see cref="SplineContainer"/> into an ECS-friendly
/// <see cref="SplineDataBlob"/> blob asset at bake time. The blob stores pre-sampled positions,
/// tangents, and up-vectors so that Burst-compiled jobs can evaluate the spline without
/// accessing any managed Unity objects at runtime.
/// <para>Requires a <see cref="SplineContainer"/> on the same GameObject.</para>
/// </summary>
[RequireComponent(typeof(SplineContainer))]
public class SplineComponentAuthoring : MonoBehaviour
{
    /// <summary>
    /// Number of evenly-spaced samples to pre-calculate along the spline.
    /// Higher values give more accurate interpolation but use more blob memory.
    /// </summary>
    [Tooltip("Number of samples to pre-calculate along the spline. Higher values = more accuracy but more memory.")]
    public int sampleCount = 100;
    
    /// <summary>
    /// Bakes the <see cref="SplineContainer"/> into a <see cref="SplineDataComponent"/> holding a
    /// persistent <see cref="BlobAssetReference{T}"/> to the pre-sampled <see cref="SplineDataBlob"/>.
    /// </summary>
    public class SplineComponentBaker: Baker<SplineComponentAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(SplineComponentAuthoring authoring)
        {
            var splineContainer = GetComponent<SplineContainer>();

            if (splineContainer is null)
            {
                Debug.LogWarning($"From {nameof(SplineComponentBaker)}.Bake(). spline container is null");
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


/// <summary>
/// ECS component that stores a <see cref="BlobAssetReference{T}"/> to the pre-sampled
/// <see cref="SplineDataBlob"/> for this entity's spline. Systems use this reference to
/// evaluate spline positions in Burst-compiled jobs without touching managed objects.
/// </summary>
public struct SplineDataComponent : IComponentData
{
    /// <summary>Reference to the blob asset containing the pre-sampled spline data.</summary>
    public BlobAssetReference<SplineDataBlob> splineData;
}

/// <summary>
/// A single sample point along a pre-sampled spline, storing the world-space position,
/// forward tangent, and up vector at that point.
/// </summary>
public struct SplineSample
{
    /// <summary>World-space position on the spline at this sample.</summary>
    public float3 position;
    /// <summary>Normalized forward tangent direction of the spline at this sample.</summary>
    public float3 tangent;
    /// <summary>Normalized up vector of the spline at this sample.</summary>
    public float3 upVector;
}

/// <summary>
/// Blob asset containing uniformly pre-sampled spline data suitable for use in Burst-compiled
/// ECS jobs. Evaluating spline positions at runtime uses linear interpolation between samples
/// rather than the managed Unity Splines API.
/// </summary>
public struct SplineDataBlob
{
    /// <summary>Array of uniformly-spaced pre-sampled points along the spline.</summary>
    public BlobArray<SplineSample> samples;
    /// <summary>Total arc length of the spline in world units.</summary>
    public float totalLength;
    /// <summary>Whether the spline loops back to its start point.</summary>
    public bool isClosed;
    
    /// <summary>
    /// Evaluates the spline at the given distance ratio (0–1) using linear interpolation
    /// between the nearest pre-sampled points. Closed splines wrap; open splines clamp to [0, 1].
    /// </summary>
    /// <param name="t">Normalized distance along the spline in the range [0, 1].</param>
    /// <returns>Interpolated <see cref="SplineSample"/> with position, tangent, and up vector.</returns>
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

    /// <summary>
    /// Creates and returns a <see cref="BlobAssetReference{SplineDataBlob}"/> with the given
    /// spline sampled at <paramref name="sampleCount"/> evenly-spaced intervals in world space.
    /// The returned blob asset uses <see cref="Allocator.Persistent"/> and must be disposed
    /// when no longer needed (handled automatically by the ECS baking system).
    /// </summary>
    /// <param name="spline">The source Unity spline to sample.</param>
    /// <param name="transformMatrix">Local-to-world matrix of the spline container transform.</param>
    /// <param name="sampleCount">Number of samples to generate along the spline.</param>
    /// <returns>A persistent blob asset reference containing the pre-sampled spline data.</returns>
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
