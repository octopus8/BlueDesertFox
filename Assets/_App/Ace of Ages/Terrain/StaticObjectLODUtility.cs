using Unity.Burst;

/// <summary>
/// Shared Burst helpers for static object distance-based LOD selection.
/// </summary>
public static class StaticObjectLODUtility
{
    /// <summary>
    /// Determine LOD level based on distance with hysteresis to prevent flickering.
    /// </summary>
    [BurstCompile]
    public static byte DetermineLODLevel(
        float distance,
        byte currentLOD,
        float lod0Dist,
        float lod1Dist,
        float lod2Dist,
        float hysteresis)
    {
        if (distance < lod0Dist)
        {
            if (currentLOD == 0)
                return 0;

            return distance < (lod0Dist - hysteresis) ? (byte)0 : currentLOD;
        }

        if (distance < lod1Dist)
        {
            if (currentLOD == 1)
                return 1;
            else if (currentLOD == 0)
                return distance > (lod0Dist + hysteresis) ? (byte)1 : (byte)0;

            return distance < (lod1Dist - hysteresis) ? (byte)1 : (byte)2;
        }

        if (distance < lod2Dist)
        {
            if (currentLOD == 2)
                return 2;

            return distance > (lod1Dist + hysteresis) ? (byte)2 : currentLOD;
        }

        return 2;
    }
}
