using Unity.Mathematics;

/// <summary>
/// Shared terrain-scroll velocity conversions used by bullets, formation movement, and the player follow object.
/// Terrain-relative velocity is invariant under scroll changes; world velocity subtracts current scroll speed.
/// Not marked [BurstCompile] so Burst inlines these at call sites (external Burst functions cannot pass/return float3).
/// </summary>
public static class TerrainScrollVelocityMath
{
    /// <summary>
    /// Converts terrain-relative velocity to world-space velocity.
    /// <c>world = terrainRelative - scroll</c>
    /// </summary>
    public static float3 WorldVelocityFromTerrainRelative(
        in float3 terrainRelativeVelocity,
        in float3 scrollVelocity)
    {
        return terrainRelativeVelocity - scrollVelocity;
    }

    /// <summary>
    /// Converts world-space velocity to terrain-relative velocity.
    /// <c>terrainRelative = world + scroll</c>
    /// </summary>
    public static float3 TerrainRelativeFromWorld(in float3 worldVelocity, in float3 scrollVelocity)
    {
        return worldVelocity + scrollVelocity;
    }

    /// <summary>
    /// Integrates world position using terrain-relative velocity and current scroll speed.
    /// </summary>
    public static float3 IntegrateScrollRelativePosition(
        in float3 position,
        in float3 terrainRelativeVelocity,
        in float3 scrollVelocity,
        float deltaTime)
    {
        return position + WorldVelocityFromTerrainRelative(terrainRelativeVelocity, scrollVelocity) * deltaTime;
    }
}
