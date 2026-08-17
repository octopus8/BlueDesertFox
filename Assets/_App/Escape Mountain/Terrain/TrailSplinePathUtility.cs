using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Editor/baker helper that converts a <see cref="SplineContainer"/> trail centerline into
/// Z-progressing X-offset samples (knot 0 = origin) and a Burst-readable blob.
/// </summary>
public static class TrailSplinePathUtility
{
    const float ZDecreaseEpsilon = 0.01f;
    const int MinDenseSamples = 32;
    const int MaxDenseSamples = 8192;

    /// <summary>
    /// Managed sample data produced by <see cref="TryExtract"/>. X offsets are relative to knot 0.
    /// </summary>
    public sealed class ExtractResult
    {
        public float zMin;
        public float zStep;
        public float[] xOffset;
        public float xMin;
        public float xMax;
    }

    /// <summary>
    /// Samples <paramref name="container"/> spline 0 into X offsets relative to knot 0.
    /// Spline Y is ignored. Fails when the path is closed, too short, or decreases in Z.
    /// </summary>
    public static bool TryExtract(
        SplineContainer container,
        float zStepMeters,
        out ExtractResult result,
        out string error)
    {
        result = null;
        error = null;

        if (container == null)
        {
            error = "Spline container is null.";
            return false;
        }

        Spline spline = container.Spline;
        if (spline == null || spline.Count < 2)
        {
            error = $"Spline '{container.name}' needs at least 2 knots.";
            return false;
        }

        if (spline.Closed)
        {
            error = $"Spline '{container.name}' is closed. Trail centerlines cannot double back in Z.";
            return false;
        }

        float zStep = math.max(zStepMeters, 0.25f);
        float4x4 matrix = container.transform.localToWorldMatrix;

        using var nativeSpline = new NativeSpline(spline, matrix, Allocator.Temp);
        float length = nativeSpline.GetLength();
        if (length < 0.01f)
        {
            error = $"Spline '{container.name}' is too short to use as a trail centerline.";
            return false;
        }

        int denseCount = math.clamp(
            (int)math.ceil(length / (zStep * 0.25f)) + 1,
            MinDenseSamples,
            MaxDenseSamples);

        var denseX = new float[denseCount];
        var denseZ = new float[denseCount];

        float3 knot0 = nativeSpline.EvaluatePosition(0f);
        for (int i = 0; i < denseCount; i++)
        {
            float t = i / (float)(denseCount - 1);
            float3 p = nativeSpline.EvaluatePosition(t);
            denseX[i] = p.x - knot0.x;
            denseZ[i] = p.z - knot0.z;
        }

        for (int i = 1; i < denseCount; i++)
        {
            if (denseZ[i] < denseZ[i - 1] - ZDecreaseEpsilon)
            {
                error =
                    $"Spline '{container.name}' doubles back in Z at sample {i} " +
                    $"({denseZ[i - 1]:F2} → {denseZ[i]:F2}). Trail paths must not decrease in Z.";
                return false;
            }
        }

        float zMin = denseZ[0];
        float zMax = denseZ[denseCount - 1];
        if (zMax - zMin < zStep * 0.5f)
        {
            error = $"Spline '{container.name}' has no meaningful Z extent after knot 0.";
            return false;
        }

        int sampleCount = math.max(2, (int)math.floor((zMax - zMin) / zStep) + 1);
        if ((sampleCount - 1) * zStep < zMax - 0.001f)
            sampleCount++;

        var xOffset = new float[sampleCount];
        float xMin = float.MaxValue;
        float xMax = float.MinValue;

        for (int i = 0; i < sampleCount; i++)
        {
            float z = zMin + i * zStep;
            float ox = SampleXAtZ(denseX, denseZ, denseCount, z);
            xOffset[i] = ox;
            if (ox < xMin) xMin = ox;
            if (ox > xMax) xMax = ox;
        }

        result = new ExtractResult
        {
            zMin = zMin,
            zStep = zStep,
            xOffset = xOffset,
            xMin = xMin,
            xMax = xMax
        };
        return true;
    }

    /// <summary>
    /// Builds a persistent blob from extracted samples. Caller must register it with
    /// <c>Baker.AddBlobAsset</c> (or dispose it).
    /// </summary>
    public static BlobAssetReference<TrailPathBlob> CreateBlob(ExtractResult data)
    {
        using var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<TrailPathBlob>();
        root.zMin = data.zMin;
        root.zStep = data.zStep > 0f ? data.zStep : 1f;
        root.xMin = data.xMin;
        root.xMax = data.xMax;

        var array = builder.Allocate(ref root.xOffset, data.xOffset.Length);
        for (int i = 0; i < data.xOffset.Length; i++)
            array[i] = data.xOffset[i];

        return builder.CreateBlobAssetReference<TrailPathBlob>(Allocator.Persistent);
    }

    static float SampleXAtZ(float[] x, float[] z, int count, float queryZ)
    {
        if (queryZ <= z[0])
            return x[0];
        if (queryZ >= z[count - 1])
            return x[count - 1];

        int lo = 0;
        int hi = count - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (z[mid] <= queryZ)
                lo = mid;
            else
                hi = mid;
        }

        float dz = z[hi] - z[lo];
        if (dz <= 1e-6f)
            return x[hi];

        float frac = (queryZ - z[lo]) / dz;
        return math.lerp(x[lo], x[hi], frac);
    }
}
