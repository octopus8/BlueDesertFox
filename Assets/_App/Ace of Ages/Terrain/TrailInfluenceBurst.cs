using Unity.Burst;
using Unity.Collections;
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
    /// Radius of the fully-flat trail core used for static object spawn exclusion.
    /// The blend zone outside this radius allows spawning.
    /// </summary>
    public static float GetTrailFlatCoreRadius(in TrailInstanceConfig trail)
    {
        return trail.width * 0.5f;
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
        in TrailInstanceConfig trail1,
        in TrailInstanceConfig trail2,
        in TrailInstanceConfig trail3,
        byte activeMask)
    {
        byte mask = 0;
        if ((activeMask & TrailMask.Trail1) != 0 &&
            TileIntersectsTrailCorridor(tileWorldX, tileWorldZ, tileSize, trail1))
            mask |= TrailMask.Trail1;
        if ((activeMask & TrailMask.Trail2) != 0 &&
            TileIntersectsTrailCorridor(tileWorldX, tileWorldZ, tileSize, trail2))
            mask |= TrailMask.Trail2;
        if ((activeMask & TrailMask.Trail3) != 0 &&
            TileIntersectsTrailCorridor(tileWorldX, tileWorldZ, tileSize, trail3))
            mask |= TrailMask.Trail3;
        return mask;
    }

    public static bool TileIntersectsTrailCorridor(
        float tileWorldX,
        float tileWorldZ,
        float tileSize,
        in TrailInstanceConfig trail)
    {
        if (!trail.enabled)
            return false;

        float searchRange = GetTrailMaxSearchRange(trail);
        float tileXMin = tileWorldX;
        float tileXMax = tileWorldX + tileSize;

        float z0 = tileWorldZ;
        float z1 = tileWorldZ + tileSize * 0.5f;
        float z2 = tileWorldZ + tileSize;

        if (CorridorOverlapsTileX(tileXMin, tileXMax, z0, trail, searchRange))
            return true;
        if (CorridorOverlapsTileX(tileXMin, tileXMax, z1, trail, searchRange))
            return true;
        if (CorridorOverlapsTileX(tileXMin, tileXMax, z2, trail, searchRange))
            return true;

        return false;
    }

    private static bool CorridorOverlapsTileX(
        float tileXMin,
        float tileXMax,
        float worldZ,
        in TrailInstanceConfig trail,
        float searchRange)
    {
        float centerX = trail.amplitude * noise.snoise(new float2(worldZ * trail.frequency + trail.seed, 0f));
        float corridorMin = centerX - searchRange - trail.amplitude;
        float corridorMax = centerX + searchRange + trail.amplitude;
        return tileXMax >= corridorMin && tileXMin <= corridorMax;
    }

    public static void BuildTrailCenterlineLUT(
        NativeArray<float> centerlineX,
        int offset,
        float zOrigin,
        float zStep,
        int length,
        in TrailInstanceConfig trail)
    {
        for (int i = 0; i < length; i++)
        {
            float sz = zOrigin + i * zStep;
            centerlineX[offset + i] = trail.amplitude * noise.snoise(new float2(sz * trail.frequency + trail.seed, 0f));
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
        float crossDist = math.abs(fX - nearestCenterX);
        if (crossDist > rejectDist)
            return default;

        int startIndex = (int)math.floor((fZ - searchRange - lut.zOrigin) / lut.zStep);
        int endIndex = (int)math.ceil((fZ + searchRange - lut.zOrigin) / lut.zStep);
        startIndex = math.clamp(startIndex, 0, lut.length - 1);
        endIndex = math.clamp(endIndex, 0, lut.length - 1);

        float minDist2D = float.MaxValue;
        int bestIndex = nearestIndex;
        for (int i = startIndex; i <= endIndex; i++)
        {
            float sz = lut.zOrigin + i * lut.zStep;
            float scx = centerlineX[lut.offset + i];
            float dx = fX - scx;
            float dz = fZ - sz;
            float d2 = dx * dx + dz * dz;
            if (d2 < minDist2D)
            {
                minDist2D = d2;
                bestIndex = i;
            }
        }

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
        float crossDist = math.abs(fX - nearestCenterX);
        if (crossDist > rejectDist)
            return float.MaxValue;

        int startIndex = (int)math.floor((fZ - searchRange - lut.zOrigin) / lut.zStep);
        int endIndex = (int)math.ceil((fZ + searchRange - lut.zOrigin) / lut.zStep);
        startIndex = math.clamp(startIndex, 0, lut.length - 1);
        endIndex = math.clamp(endIndex, 0, lut.length - 1);

        float minDist2D = float.MaxValue;
        for (int i = startIndex; i <= endIndex; i++)
        {
            float sz = lut.zOrigin + i * lut.zStep;
            float scx = centerlineX[lut.offset + i];
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
    public static float ComputeMinDistanceToTrail(float fX, float fZ, in TrailInstanceConfig trail, float lutStep)
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
            float scx = trail.amplitude * noise.snoise(new float2(sz * trail.frequency + trail.seed, 0f));
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
    public static bool IsInsideTrailExclusionZone(float fX, float fZ, in TrailInstanceConfig trail, float lutStep)
    {
        if (!trail.enabled)
            return false;

        float exclusionRadius = GetTrailFlatCoreRadius(trail);
        return ComputeMinDistanceToTrail(fX, fZ, trail, lutStep) < exclusionRadius;
    }
}
