using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Editor/baker helper that converts a trail path image (black line on white, red start dot)
/// into Z-progressing X-offset samples and a Burst-readable blob.
/// </summary>
public static class TrailImagePathUtility
{
    const byte RedMin = 200;
    const byte RedMaxGB = 80;
    const byte TrailLuminanceMax = 40;

    /// <summary>
    /// Managed sample data produced by <see cref="TryExtract"/>. X offsets are relative to the
    /// red-dot centroid; Z increases with image +Y (Unity texture bottom-left origin).
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
    /// Scans <paramref name="texture"/> into per-row centerline X offsets in meters.
    /// </summary>
    public static bool TryExtract(
        Texture2D texture,
        float metersPerPixel,
        out ExtractResult result,
        out string error)
    {
        result = null;
        error = null;

        if (texture == null)
        {
            error = "Path image is null.";
            return false;
        }

        if (!texture.isReadable)
        {
            error = $"Path image '{texture.name}' is not readable. Enable Read/Write in the texture import settings.";
            return false;
        }

        if (metersPerPixel <= 0f)
        {
            error = "Meters per pixel must be greater than 0.";
            return false;
        }

        Color32[] pixels = texture.GetPixels32();
        int width = texture.width;
        int height = texture.height;

        double redSumX = 0d;
        double redSumY = 0d;
        int redCount = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (!IsRed(pixels[i]))
                continue;

            redSumX += i % width;
            redSumY += i / width;
            redCount++;
        }

        if (redCount == 0)
        {
            error = $"Path image '{texture.name}' has no red start dot.";
            return false;
        }

        float startPx = (float)(redSumX / redCount);
        float startPy = (float)(redSumY / redCount);

        var rowX = new float[height];
        var rowValid = new bool[height];
        int firstValid = -1;
        int lastValid = -1;
        int validCount = 0;

        for (int y = 0; y < height; y++)
        {
            double sumX = 0d;
            int count = 0;
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (!IsTrailPixel(pixels[row + x]))
                    continue;

                sumX += x;
                count++;
            }

            if (count <= 0)
                continue;

            rowX[y] = (float)(sumX / count);
            rowValid[y] = true;
            if (firstValid < 0)
                firstValid = y;
            lastValid = y;
            validCount++;
        }

        if (validCount == 0)
        {
            error = $"Path image '{texture.name}' has no black trail pixels.";
            return false;
        }

        int sampleCount = lastValid - firstValid + 1;
        var xOffset = new float[sampleCount];
        float xMin = float.MaxValue;
        float xMax = float.MinValue;

        for (int i = 0; i < sampleCount; i++)
        {
            int y = firstValid + i;
            if (!rowValid[y])
            {
                xOffset[i] = float.NaN;
                continue;
            }

            float ox = (rowX[y] - startPx) * metersPerPixel;
            xOffset[i] = ox;
            if (ox < xMin) xMin = ox;
            if (ox > xMax) xMax = ox;
        }

        result = new ExtractResult
        {
            zMin = (firstValid - startPy) * metersPerPixel,
            zStep = metersPerPixel,
            xOffset = xOffset,
            xMin = xMin == float.MaxValue ? 0f : xMin,
            xMax = xMax == float.MinValue ? 0f : xMax
        };
        return true;
    }

    /// <summary>
    /// Builds a persistent blob from extracted samples. Caller must register it with
    /// <c>Baker.AddBlobAsset</c> (or dispose it).
    /// </summary>
    public static BlobAssetReference<TrailImagePathBlob> CreateBlob(ExtractResult data)
    {
        using var builder = new BlobBuilder(Allocator.Temp);
        ref var root = ref builder.ConstructRoot<TrailImagePathBlob>();
        root.zMin = data.zMin;
        root.zStep = data.zStep > 0f ? data.zStep : 1f;
        root.xMin = data.xMin;
        root.xMax = data.xMax;

        var array = builder.Allocate(ref root.xOffset, data.xOffset.Length);
        for (int i = 0; i < data.xOffset.Length; i++)
            array[i] = data.xOffset[i];

        return builder.CreateBlobAssetReference<TrailImagePathBlob>(Allocator.Persistent);
    }

    static bool IsRed(Color32 c)
    {
        return c.r > RedMin && c.g < RedMaxGB && c.b < RedMaxGB;
    }

    static bool IsTrailPixel(Color32 c)
    {
        if (IsRed(c))
            return true;

        int luminance = ((int)c.r + c.g + c.b) / 3;
        return luminance <= TrailLuminanceMax;
    }
}
