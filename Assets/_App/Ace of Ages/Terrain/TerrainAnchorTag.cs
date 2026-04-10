using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Component that marks an entity as a terrain anchor.
/// Terrain anchors move with the terrain scroll offset while maintaining their base position.
/// </summary>
public struct TerrainAnchorTag : IComponentData
{
    /// <summary>
    /// The base position of this anchor in world space.
    /// The actual position will be: basePosition - scrollOffset.accumulatedOffset
    /// </summary>
    public float3 basePosition;
}


