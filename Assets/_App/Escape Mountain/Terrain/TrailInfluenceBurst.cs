using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Bit flags for the three procedural terrain trails.
/// </summary>
public static class TrailMask
{
    public const byte Trail1 = 1;
    public const byte Trail2 = 2;
    public const byte Trail3 = 4;
}

/// <summary>
/// Trail carve influence at a world XZ point plus the nearest centerline sample Z used for slope.
/// </summary>
public struct TrailInfluenceResult
{
    public float influence;
    public float centerlineZ;
}

/// <summary>
/// Metadata describing a precomputed centerline X lookup table for one trail on one tile.
/// </summary>
public struct TrailCenterlineLUT
{
    public int offset;
    public int length;
    public float zOrigin;
    public float zStep;
}

/// <summary>
/// Burst helpers for trail corridor tests and LUT-based influence lookup.
/// </summary>
[BurstCompile]
public static class TrailInfluenceBurst
{
    public static float GetTrailMaxSearchRange(in TrailInstanceConfig trail)
    {
        return trail.width * 0.5f + trail.blendWidth;
    }

    /// <summary>
    /// Applies defaults for path settings that were left at 0 by Unity serialization on existing scenes.
    /// </summary>
    public static TrailPathConfig NormalizeTrailPathSettings(TrailPathConfig path)
    {
        // 0 means "never serialized / uninitialized" for existing scenes — use the designed default.
        if (path.straightLength <= 0f)
            path.straightLength = 80f;
        if (path.weaveFadeLength < 0f)
            path.weaveFadeLength = 0f;
        return path;
    }

    /// <summary>
    /// Radius of the fully-flat trail core used for static object spawn exclusion.
    /// The blend zone outside this radius allows spawning.
    /// </summary>
    public static float GetTrailFlatCoreRadius(in TrailInstanceConfig trail)
    {
        return trail.width * 0.5f;
    }

    /// <summary>
    /// Distance from startZ at which the shared straight run ends and weave begins
    /// (applies in both +Z and −Z).
    /// </summary>
    public static float GetStraightRunHalfLength(float straightLength)
    {
        return math.max(0f, straightLength);
    }

    /// <summary>
    /// Samples trail centerline X at world Z. All trails share startX for
    /// <paramref name="straightLength"/> meters on either side of <paramref name="startZ"/>,
    /// then fade into per-trail noise weave beyond that. Weave is relative to the noise at the
    /// straight-run edge so the path leaves startX continuously.
    /// </summary>
    public static float SampleCenterlineX(
        float worldZ,
        in TrailInstanceConfig trail,
        float startX,
        float startZ,
        float straightLength,
        float weaveFadeLength)
    {
        // Distance along Z from the shared fork. Straight run is symmetric in +Z/-Z.
        float along = math.abs(worldZ - startZ);
        float straight = math.max(straightLength, 0f);

        // Fully locked to the shared start X through the straight section.
        if (along <= straight)
            return startX;

        // Fade weave in after the straight section (0 at the edge → 1 after fadeLength).
        float fadeLength = math.max(weaveFadeLength, 0f);
        float fade = fadeLength > 0f
            ? math.smoothstep(0f, 1f, (along - straight) / fadeLength)
            : 1f;

        // Noise delta vs the straight-run edge keeps X continuous at the fork.
        float edgeZ = worldZ >= startZ ? startZ + straight : startZ - straight;
        float noiseAtZ = noise.snoise(new float2(worldZ * trail.frequency + trail.seed, 0f));
        float noiseAtEdge = noise.snoise(new float2(edgeZ * trail.frequency + trail.seed, 0f));
        return startX + trail.amplitude * (noiseAtZ - noiseAtEdge) * fade;
    }

    /// <summary>
    /// Samples trail centerline X at world Z using shared path fields from <see cref="TrailPathConfig"/>.
    /// Spline-authored blobs ignore straight-run / noise. Returns <see cref="float.NaN"/> when the
    /// spline path has no centerline at this Z (out of range or a gap).
    /// </summary>
    public static float SampleCenterlineX(
        float worldZ,
        in TrailInstanceConfig trail,
        in TrailPathConfig path,
        BlobAssetReference<TrailPathBlob> pathBlob)
    {
        if (pathBlob.IsCreated)
        {
            if (TrySampleAuthoredCenterlineX(worldZ, path.startX, path.startZ, pathBlob, out float x))
                return x;
            return float.NaN;
        }

        return SampleCenterlineX(
            worldZ, trail, path.startX, path.startZ, path.straightLength, path.weaveFadeLength);
    }

    /// <summary>
    /// Samples trail centerline X at world Z using shared path fields from <see cref="TrailPathConfig"/>.
    /// Noise-weave overload (no authored path blob).
    /// </summary>
    public static float SampleCenterlineX(float worldZ, in TrailInstanceConfig trail, in TrailPathConfig path)
    {
        return SampleCenterlineX(
            worldZ, trail, path.startX, path.startZ, path.straightLength, path.weaveFadeLength);
    }

    /// <summary>
    /// Interpolates a spline-authored centerline. Both adjacent samples must be valid; gaps do not lerp.
    /// </summary>
    public static bool TrySampleAuthoredCenterlineX(
        float worldZ,
        float startX,
        float startZ,
        BlobAssetReference<TrailPathBlob> pathBlob,
        out float centerX)
    {
        centerX = 0f;
        if (!pathBlob.IsCreated)
            return false;

        ref var blob = ref pathBlob.Value;
        int count = blob.xOffset.Length;
        if (count <= 0)
            return false;

        float zStep = blob.zStep > 0f ? blob.zStep : 1f;
        float t = (worldZ - startZ - blob.zMin) / zStep;
        if (t < 0f || t > count - 1)
            return false;

        int i0 = (int)math.floor(t);
        int i1 = math.min(i0 + 1, count - 1);
        float x0 = blob.xOffset[i0];
        float x1 = blob.xOffset[i1];
        if (math.isnan(x0) || math.isnan(x1))
            return false;

        float frac = t - i0;
        centerX = startX + math.lerp(x0, x1, frac);
        return true;
    }

    public static byte GetActiveTrailMask(in TrailInstanceConfig trail1, in TrailInstanceConfig trail2, in TrailInstanceConfig trail3)
    {
        byte mask = 0;
        if (trail1.enabled) mask |= TrailMask.Trail1;
        if (trail2.enabled) mask |= TrailMask.Trail2;
        if (trail3.enabled) mask |= TrailMask.Trail3;
        return mask;
    }

    public static float GetMaxSearchRangeAcrossTrails(
        in TrailInstanceConfig trail1,
        in TrailInstanceConfig trail2,
        in TrailInstanceConfig trail3,
        byte activeMask)
    {
        float maxRange = 0f;
        if ((activeMask & TrailMask.Trail1) != 0)
            maxRange = math.max(maxRange, GetTrailMaxSearchRange(trail1));
        if ((activeMask & TrailMask.Trail2) != 0)
            maxRange = math.max(maxRange, GetTrailMaxSearchRange(trail2));
        if ((activeMask & TrailMask.Trail3) != 0)
            maxRange = math.max(maxRange, GetTrailMaxSearchRange(trail3));
        return maxRange;
    }

    public static int ComputeLutLength(float tileSize, float maxSearchRange, float lutStep)
    {
        float span = tileSize + 2f * maxSearchRange;
        return math.max(2, (int)math.ceil(span / lutStep) + 1);
    }

    public static float ComputeLutZOrigin(float tileWorldZ, float maxSearchRange)
    {
        return tileWorldZ - maxSearchRange;
    }

    public static byte ComputeTileTrailMask(
        float tileWorldX,
        float tileWorldZ,
        float tileSize,
        in TrailConfig config,
        in TrailPathConfig path,
        in TrailPaths trailPaths,
        byte activeMask)
    {
        byte mask = 0;
        if ((activeMask & TrailMask.Trail1) != 0 &&
            TileIntersectsTrailCorridor(tileWorldX, tileWorldZ, tileSize, config.trail1, path, trailPaths.trail1))
            mask |= TrailMask.Trail1;
        if ((activeMask & TrailMask.Trail2) != 0 &&
            TileIntersectsTrailCorridor(tileWorldX, tileWorldZ, tileSize, config.trail2, path, trailPaths.trail2))
            mask |= TrailMask.Trail2;
        if ((activeMask & TrailMask.Trail3) != 0 &&
            TileIntersectsTrailCorridor(tileWorldX, tileWorldZ, tileSize, config.trail3, path, trailPaths.trail3))
            mask |= TrailMask.Trail3;
        return mask;
    }

    public static bool TileIntersectsTrailCorridor(
        float tileWorldX,
        float tileWorldZ,
        float tileSize,
        in TrailInstanceConfig trail,
        in TrailPathConfig path,
        BlobAssetReference<TrailPathBlob> pathBlob)
    {
        if (!trail.enabled)
            return false;

        if (pathBlob.IsCreated)
            return TileIntersectsAuthoredTrailCorridor(tileWorldX, tileWorldZ, tileSize, trail, path, pathBlob);

        float searchRange = GetTrailMaxSearchRange(trail);
        float tileXMin = tileWorldX;
        float tileXMax = tileWorldX + tileSize;

        float z0 = tileWorldZ;
        float z1 = tileWorldZ + tileSize * 0.5f;
        float z2 = tileWorldZ + tileSize;

        if (CorridorOverlapsTileX(tileXMin, tileXMax, z0, trail, path, searchRange))
            return true;
        if (CorridorOverlapsTileX(tileXMin, tileXMax, z1, trail, path, searchRange))
            return true;
        if (CorridorOverlapsTileX(tileXMin, tileXMax, z2, trail, path, searchRange))
            return true;

        return false;
    }

    private static bool TileIntersectsAuthoredTrailCorridor(
        float tileWorldX,
        float tileWorldZ,
        float tileSize,
        in TrailInstanceConfig trail,
        in TrailPathConfig path,
        BlobAssetReference<TrailPathBlob> pathBlob)
    {
        ref var blob = ref pathBlob.Value;
        int count = blob.xOffset.Length;
        if (count <= 0)
            return false;

        float searchRange = GetTrailMaxSearchRange(trail);
        float tileXMin = tileWorldX;
        float tileXMax = tileWorldX + tileSize;
        float zLo = tileWorldZ - searchRange;
        float zHi = tileWorldZ + tileSize + searchRange;

        float zStep = blob.zStep > 0f ? blob.zStep : 1f;
        float worldZMin = path.startZ + blob.zMin;
        float worldZMax = worldZMin + (count - 1) * zStep;
        if (zHi < worldZMin || zLo > worldZMax)
            return false;

        int i0 = (int)math.floor((zLo - worldZMin) / zStep);
        int i1 = (int)math.ceil((zHi - worldZMin) / zStep);
        i0 = math.clamp(i0, 0, count - 1);
        i1 = math.clamp(i1, 0, count - 1);

        for (int i = i0; i <= i1; i++)
        {
            float ox = blob.xOffset[i];
            if (math.isnan(ox))
                continue;

            float cx = path.startX + ox;
            if (tileXMax >= cx - searchRange && tileXMin <= cx + searchRange)
                return true;
        }

        return false;
    }

    private static bool CorridorOverlapsTileX(
        float tileXMin,
        float tileXMax,
        float worldZ,
        in TrailInstanceConfig trail,
        in TrailPathConfig path,
        float searchRange)
    {
        float centerX = SampleCenterlineX(worldZ, trail, path);
        float along = math.abs(worldZ - path.startZ);
        float amplitudePad = along <= math.max(0f, path.straightLength) ? 0f : trail.amplitude;
        float corridorMin = centerX - searchRange - amplitudePad;
        float corridorMax = centerX + searchRange + amplitudePad;
        return tileXMax >= corridorMin && tileXMin <= corridorMax;
    }

    public static void BuildTrailCenterlineLUT(
        NativeArray<float> centerlineX,
        int offset,
        float zOrigin,
        float zStep,
        int length,
        in TrailInstanceConfig trail,
        in TrailPathConfig path,
        BlobAssetReference<TrailPathBlob> pathBlob)
    {
        for (int i = 0; i < length; i++)
        {
            float sz = zOrigin + i * zStep;
            centerlineX[offset + i] = SampleCenterlineX(sz, trail, path, pathBlob);
        }
    }

    public static TrailInfluenceResult ComputeTrailInfluenceFromLUT(
        float fX,
        float fZ,
        in TrailInstanceConfig trail,
        in TrailCenterlineLUT lut,
        NativeArray<float> centerlineX)
    {
        if (!trail.enabled || lut.length <= 0)
            return default;

        float halfWidth = trail.width * 0.5f;
        float searchRange = halfWidth + trail.blendWidth;
        float rejectDist = searchRange + lut.zStep;

        int nearestIndex = (int)math.round((fZ - lut.zOrigin) / lut.zStep);
        nearestIndex = math.clamp(nearestIndex, 0, lut.length - 1);
        float nearestCenterX = centerlineX[lut.offset + nearestIndex];
        if (!math.isnan(nearestCenterX))
        {
            float crossDist = math.abs(fX - nearestCenterX);
            if (crossDist > rejectDist)
                return default;
        }

        int startIndex = (int)math.floor((fZ - searchRange - lut.zOrigin) / lut.zStep);
        int endIndex = (int)math.ceil((fZ + searchRange - lut.zOrigin) / lut.zStep);
        startIndex = math.clamp(startIndex, 0, lut.length - 1);
        endIndex = math.clamp(endIndex, 0, lut.length - 1);

        float minDist2D = float.MaxValue;
        int bestIndex = nearestIndex;
        for (int i = startIndex; i <= endIndex; i++)
        {
            float scx = centerlineX[lut.offset + i];
            if (math.isnan(scx))
                continue;

            float sz = lut.zOrigin + i * lut.zStep;
            float dx = fX - scx;
            float dz = fZ - sz;
            float d2 = dx * dx + dz * dz;
            if (d2 < minDist2D)
            {
                minDist2D = d2;
                bestIndex = i;
            }
        }

        if (minDist2D == float.MaxValue)
            return default;

        float centerlineZ = lut.zOrigin + bestIndex * lut.zStep;
        float minDist = math.sqrt(minDist2D);

        if (minDist < halfWidth)
            return new TrailInfluenceResult { influence = 1f, centerlineZ = centerlineZ };

        if (minDist < halfWidth + trail.blendWidth)
        {
            return new TrailInfluenceResult
            {
                influence = 1f - math.smoothstep(halfWidth, halfWidth + trail.blendWidth, minDist),
                centerlineZ = centerlineZ
            };
        }

        return default;
    }

    /// <summary>
    /// LUT-based minimum 2D distance to trail centerline (fast path for spawn exclusion).
    /// </summary>
    public static float ComputeMinDistanceToTrailFromLUT(
        float fX,
        float fZ,
        in TrailInstanceConfig trail,
        in TrailCenterlineLUT lut,
        NativeArray<float> centerlineX)
    {
        if (!trail.enabled || lut.length <= 0)
            return float.MaxValue;

        float searchRange = GetTrailMaxSearchRange(trail);
        float rejectDist = searchRange + lut.zStep;

        int nearestIndex = (int)math.round((fZ - lut.zOrigin) / lut.zStep);
        nearestIndex = math.clamp(nearestIndex, 0, lut.length - 1);
        float nearestCenterX = centerlineX[lut.offset + nearestIndex];
        if (!math.isnan(nearestCenterX))
        {
            float crossDist = math.abs(fX - nearestCenterX);
            if (crossDist > rejectDist)
                return float.MaxValue;
        }

        int startIndex = (int)math.floor((fZ - searchRange - lut.zOrigin) / lut.zStep);
        int endIndex = (int)math.ceil((fZ + searchRange - lut.zOrigin) / lut.zStep);
        startIndex = math.clamp(startIndex, 0, lut.length - 1);
        endIndex = math.clamp(endIndex, 0, lut.length - 1);

        float minDist2D = float.MaxValue;
        for (int i = startIndex; i <= endIndex; i++)
        {
            float scx = centerlineX[lut.offset + i];
            if (math.isnan(scx))
                continue;

            float sz = lut.zOrigin + i * lut.zStep;
            float dx = fX - scx;
            float dz = fZ - sz;
            float d2 = dx * dx + dz * dz;
            if (d2 < minDist2D)
                minDist2D = d2;
        }

        return math.sqrt(minDist2D);
    }

    /// <summary>
    /// Returns true when a point lies inside the flat trail core (spawn exclusion zone).
    /// Points in the blend zone return false and may spawn static objects.
    /// </summary>
    public static bool IsInsideTrailExclusionZoneFromLUT(
        float fX,
        float fZ,
        in TrailInstanceConfig trail,
        in TrailCenterlineLUT lut,
        NativeArray<float> centerlineX)
    {
        if (!trail.enabled)
            return false;

        float exclusionRadius = GetTrailFlatCoreRadius(trail);
        return ComputeMinDistanceToTrailFromLUT(fX, fZ, trail, lut, centerlineX) < exclusionRadius;
    }

    /// <summary>
    /// On-demand minimum distance for sparse checks (e.g. static object spawn exclusion).
    /// </summary>
    public static float ComputeMinDistanceToTrail(
        float fX,
        float fZ,
        in TrailInstanceConfig trail,
        in TrailPathConfig path,
        BlobAssetReference<TrailPathBlob> pathBlob,
        float lutStep)
    {
        if (!trail.enabled)
            return float.MaxValue;

        float searchRange = GetTrailMaxSearchRange(trail);
        float zStart = fZ - searchRange;
        float zEnd = fZ + searchRange;
        int count = math.max(2, (int)math.ceil((zEnd - zStart) / lutStep) + 1);
        float step = count > 1 ? (zEnd - zStart) / (count - 1) : 0f;

        float minDist2D = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            float sz = zStart + i * step;
            float scx = SampleCenterlineX(sz, trail, path, pathBlob);
            if (math.isnan(scx))
                continue;

            float dx = fX - scx;
            float dz = fZ - sz;
            float d2 = dx * dx + dz * dz;
            if (d2 < minDist2D)
                minDist2D = d2;
        }

        return math.sqrt(minDist2D);
    }

    /// <summary>
    /// On-demand flat-core exclusion check (spawn exclusion = flat core only; blend zone allows objects).
    /// </summary>
    public static bool IsInsideTrailExclusionZone(
        float fX,
        float fZ,
        in TrailInstanceConfig trail,
        in TrailPathConfig path,
        BlobAssetReference<TrailPathBlob> pathBlob,
        float lutStep)
    {
        if (!trail.enabled)
            return false;

        float exclusionRadius = GetTrailFlatCoreRadius(trail);
        return ComputeMinDistanceToTrail(fX, fZ, trail, path, pathBlob, lutStep) < exclusionRadius;
    }
}
