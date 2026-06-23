using Unity.Burst;
using Unity.Mathematics;

/// <summary>
/// Shared spatial chunk helpers for static object LOD and spawn systems.
/// </summary>
public static class StaticObjectSpatialChunkUtility
{
    /// <summary>Chunk size in meters (must match spatial chunking and LOD systems).</summary>
    public const float ChunkSize = 100f;

    /// <summary>Calculates the 100m grid chunk coordinate for a world XZ position.</summary>
    [BurstCompile]
    public static int2 GetChunkCoord(in float3 worldPos)
    {
        return new int2(
            (int)math.floor(worldPos.x / ChunkSize),
            (int)math.floor(worldPos.z / ChunkSize)
        );
    }

    /// <summary>
    /// Chunk radius around the player required to cover all objects that may need LOD transitions.
    /// </summary>
    [BurstCompile]
    public static int GetChunkRadiusForDistance(float distanceMeters)
    {
        return math.max(1, (int)math.ceil(distanceMeters / ChunkSize));
    }
}
