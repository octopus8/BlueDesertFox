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
    
    // Physics optimization parameters
    /// <summary>Maximum number of physics colliders created per frame (prevents stalls).</summary>
    public int maxCollidersCreatedPerFrame;
    
    /// <summary>Distance threshold for full-resolution colliders (all vertices).</summary>
    public float lodFullResolutionDistance;
    
    /// <summary>Distance threshold for half-resolution colliders (every 2nd vertex).</summary>
    public float lodHalfResolutionDistance;
    
    /// <summary>Distance threshold for quarter-resolution colliders (every 4th vertex).</summary>
    public float lodQuarterResolutionDistance;
    
    /// <summary>Maximum memory in megabytes for collider cache (LRU eviction when exceeded).</summary>
    public int maxColliderCacheMemoryMB;
    
    /// <summary>Whether to assign distant tiles to low-detail physics layer.</summary>
    public bool usePhysicsLODLayers;
    
    /// <summary>Physics layer index for low-detail terrain (half/quarter resolution).</summary>
    public int lowDetailPhysicsLayer;
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

/// <summary>
/// Singleton managed component that holds a reference to the player GameObject's Transform.
/// This allows the terrain system to track a GameObject that exists outside the ECS subscene.
/// </summary>
public class PlayerTransformReference : IComponentData
{
    /// <summary>
    /// The Transform of the player GameObject to track for terrain centering.
    /// </summary>
    public UnityEngine.Transform playerTransform;
}

/// <summary>
/// Component that stores search parameters for finding the player GameObject at runtime.
/// This is baked into the entity so it can find the target after subscenes load.
/// </summary>
public struct PlayerTrackingSearch : IComponentData
{
    public enum Mode : byte
    {
        FindByName = 0,
        FindByTag = 1,
        FindAutoHandPlayer = 2,
        FindMainCamera = 3
    }
    
    /// <summary>
    /// How to search for the player GameObject.
    /// </summary>
    public Mode mode;
    
    /// <summary>
    /// Search string (name or tag) - only used for FindByName and FindByTag modes.
    /// </summary>
    public Unity.Collections.FixedString128Bytes searchString;
    
    /// <summary>
    /// True if the PlayerTransformReference has been set up successfully.
    /// </summary>
    public bool initialized;
}

/// <summary>
/// Singleton component that tracks accumulated terrain scroll distance.
/// Used for auto-scrolling terrain in the direction the player is facing (XZ plane).
/// </summary>
public struct ScrollOffset : IComponentData
{
    /// <summary>
    /// Total distance the terrain has scrolled as a directional vector (locked to XZ plane, Y=0).
    /// This offset is subtracted from tile positions to create the scrolling effect.
    /// Direction is determined by the player's forward direction projected onto the XZ plane.
    /// </summary>
    public float3 accumulatedOffset;
}

/// <summary>
/// Singleton component that provides scroll direction and speed for terrain auto-scrolling.
/// Direction is expected to be pre-normalized. Speed is in units per second.
/// </summary>
public struct TerrainScrollVelocity : IComponentData
{
    /// <summary>
    /// Normalized direction vector for scrolling (expected to be pre-normalized by provider system).
    /// </summary>
    public float3 direction;
    
    /// <summary>
    /// Speed of scrolling in units per second. Set to 0 to disable scrolling.
    /// </summary>
    public float speed;
}

/// <summary>
/// Configuration component for player-based scroll velocity with world origin tracking rotation.
/// Scrolls terrain in the direction the player is facing, with rotation based on world origin orientation.
/// Supports vertical movement based on player pitch angle.
/// </summary>
public struct PlayerTerrainScrollVelocityConfig : IComponentData
{
    /// <summary>
    /// Speed of scrolling in units per second (scrolls in player's forward direction).
    /// </summary>
    public float speed;
    
    /// <summary>
    /// Rotation speed multiplier for world origin tracking (degrees per second per degree of difference).
    /// Higher values make the scroll direction rotate faster toward the world origin direction.
    /// </summary>
    public float rotationSpeed;
    
    /// <summary>
    /// Vertical movement speed in units per second at maximum pitch (90 degrees up/down).
    /// The actual vertical speed scales proportionally with the player's pitch angle.
    /// Positive values: looking up moves world origin upward, looking down moves it downward.
    /// </summary>
    public float verticalSpeed;
    
    /// <summary>
    /// Minimum Y position for the world origin (prevents moving too far down).
    /// </summary>
    public float minVerticalPosition;
    
    /// <summary>
    /// Maximum Y position for the world origin (prevents moving too far up).
    /// </summary>
    public float maxVerticalPosition;
}

/// <summary>
/// Singleton managed component that holds a reference to the world origin GameObject's Transform.
/// This allows the terrain system to track the VR world origin for rotation of scroll direction.
/// </summary>
public class WorldOriginTransformReference : IComponentData
{
    /// <summary>
    /// The Transform of the world origin/camera GameObject for tracking rotation.
    /// </summary>
    public UnityEngine.Transform worldOriginTransform;
}

/// <summary>
/// Component that stores search parameters for finding the world origin GameObject at runtime.
/// This is baked into the entity so it can find the world origin after subscenes load.
/// </summary>
public struct WorldOriginTrackingSearch : IComponentData
{
    public enum Mode : byte
    {
        FindByName = 0,
        FindByTag = 1,
        FindMainCamera = 2
    }
    
    /// <summary>
    /// How to search for the world origin GameObject.
    /// </summary>
    public Mode mode;
    
    /// <summary>
    /// Search string (name or tag) - only used for FindByName and FindByTag modes.
    /// </summary>
    public Unity.Collections.FixedString128Bytes searchString;
    
    /// <summary>
    /// True if the WorldOriginTransformReference has been set up successfully.
    /// </summary>
    public bool initialized;
}

/// <summary>
/// Singleton managed component that holds a reference to the terrain material.
/// This allows the authoring component to pass the material to the rendering system.
/// </summary>
public class TerrainMaterialReference : IComponentData
{
    /// <summary>
    /// The material to use for rendering terrain tiles.
    /// If null, the rendering system will fall back to loading from Resources.
    /// </summary>
    public UnityEngine.Material material;
}

