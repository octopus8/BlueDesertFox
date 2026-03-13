using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Singleton configuration for terrain tile system.
/// </summary>
public struct TerrainTileConfig : IComponentData
{
    /// <summary>Size of each tile in world units (e.g., 100 meters).</summary>
    public float tileSize;
    
    /// <summary>Distance from player that tiles remain active (e.g., 500 meters).</summary>
    public float viewDistance;
    
    /// <summary>Number of vertices per side of each tile (e.g., 32 = 32x32 grid).</summary>
    public int verticesPerSide;
    
    // Noise parameters for procedural generation
    public float noiseFrequency;
    public float noiseAmplitude;
    public int noiseOctaves;
    public float noiseLacunarity;
    public float noisePersistence;
}

/// <summary>
/// Component that identifies a terrain tile and its position in the grid.
/// </summary>
public struct TerrainTile : IComponentData
{
    /// <summary>Grid coordinates of this tile (e.g., (0,0), (1,0), (-1,1)).</summary>
    public int2 gridCoordinate;
    
    /// <summary>True if the mesh has been generated for this tile.</summary>
    public bool meshGenerated;
    
    /// <summary>True if the mesh needs to be regenerated (e.g., after origin shift).</summary>
    public bool needsRegeneration;
}

/// <summary>
/// Buffer element for storing vertex positions.
/// </summary>
public struct VertexElement : IBufferElementData
{
    public float3 value;
}

/// <summary>
/// Buffer element for storing vertex normals.
/// </summary>
public struct NormalElement : IBufferElementData
{
    public float3 value;
}

/// <summary>
/// Buffer element for storing UV coordinates.
/// </summary>
public struct UVElement : IBufferElementData
{
    public float2 value;
}

/// <summary>
/// Buffer element for storing triangle indices.
/// </summary>
public struct IndexElement : IBufferElementData
{
    public int value;
}

/// <summary>
/// Component that stores a reference to the Unity Mesh object.
/// This is a managed component because it holds a reference to a Unity Object.
/// </summary>
public class MeshReference : IComponentData
{
    public UnityEngine.Mesh mesh;
}

