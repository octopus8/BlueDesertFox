using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

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
    
    /// <summary>Physics layer index for close terrain (full resolution).</summary>
    public int closeTerrainPhysicsLayer;
    
    /// <summary>Physics layer index for low-detail terrain (half/quarter resolution).</summary>
    public int lowDetailPhysicsLayer;
    
    /// <summary>Whether to render terrain tiles (disable for tree-only testing).</summary>
    public bool renderTerrain;
    
    /// <summary>Whether to visualize physics colliders as wireframes in Scene view.</summary>
    public bool visualizeColliders;
    
    /// <summary>Whether to generate physics colliders for terrain tiles (disable for debugging/performance testing).</summary>
    public bool enablePhysicsColliders;
    
    /// <summary>Whether to enable TerrainRenderingDebugSystem logging (disable to reduce console spam).</summary>
    public bool enableRenderingDebug;
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

/// <summary>
/// Singleton component that configures tree spawning on terrain tiles.
/// </summary>
public struct TreeSpawnerConfig : IComponentData
{
    /// <summary>Minimum number of trees to spawn per tile.</summary>
    public int minTreesPerTile;
    
    /// <summary>Maximum number of trees to spawn per tile.</summary>
    public int maxTreesPerTile;
    
    /// <summary>Minimum height (Y coordinate) for tree spawning.</summary>
    public float minSpawnHeight;
    
    /// <summary>Maximum height (Y coordinate) for tree spawning.</summary>
    public float maxSpawnHeight;
    
    /// <summary>Pre-calculated slope threshold (cosine of max slope angle) for filtering steep terrain.</summary>
    public float slopeThreshold;
    
    /// <summary>Maximum number of trees to spawn per frame (performance budgeting).</summary>
    public int maxTreesSpawnedPerFrame;
    
    /// <summary>Enable debug logging for tree spawner system.</summary>
    public bool enableSpawnerDebug;
}

/// <summary>
/// Buffer element that stores a reference to a tree prefab entity for random selection.
/// </summary>
public struct TreePrefabElement : IBufferElementData
{
    /// <summary>The entity prefab to instantiate for this tree type.</summary>
    public Entity prefabEntity;
}

/// <summary>
/// Managed component that stores mesh and material references for all tree prefabs.
/// Used during tree spawning to assign GlobalTreeInstanceData without runtime lookups.
/// Must be a class (not struct) to hold managed Unity object references.
/// Singleton component stored on the same entity as TreeSpawnerConfig.
/// </summary>
public class TreePrefabMeshMaterialData : IComponentData
{
    /// <summary>Array of meshes, one per tree prefab (same index as TreePrefabElement buffer).</summary>
    public Mesh[] meshes;
    
    /// <summary>Array of materials, one per tree prefab (same index as TreePrefabElement buffer).</summary>
    public Material[] materials;
}

/// <summary>
/// Tag component indicating that trees have been spawned for this tile.
/// </summary>
public struct TreesSpawned : IComponentData
{
}

/// <summary>
/// Temporary buffer element storing calculated tree spawn data for deferred instantiation.
/// Calculated by Burst job, consumed by ECB-based instantiation job, then cleared same frame.
/// </summary>
public struct TreeSpawnPosition : IBufferElementData
{
    /// <summary>Position relative to tile origin (tile-local space).</summary>
    public float3 localPosition;
    
    /// <summary>World-space position for the tree.</summary>
    public float3 worldPosition;
    
    /// <summary>Random Y-axis rotation for visual variety.</summary>
    public quaternion rotation;
    
    /// <summary>Tree type index (0 to N-1 where N is number of tree types).</summary>
    public int treeTypeIndex;
    
    /// <summary>Initial LOD level based on distance to camera (0=LOD0, 1=LOD1, 2=LOD2).</summary>
    public byte initialLODLevel;
    
    /// <summary>Initial distance to player/camera for LOD calculation.</summary>
    public float initialDistance;
    
    /// <summary>Initial mesh index based on tree type and LOD level.</summary>
    public int initialMeshIndex;
}

/// <summary>
/// Buffer element that stores references to trees spawned on this tile for cleanup.
/// </summary>
public struct SpawnedTreeReference : IBufferElementData
{
    /// <summary>The entity of a tree spawned on this tile.</summary>
    public Entity treeEntity;
}

/// <summary>
/// Component that tracks which terrain tile a tree belongs to and its local offset.
/// Used to update tree positions when tiles move, without using parent-child hierarchy.
/// </summary>
public struct TreeTileOwnership : IComponentData
{
    /// <summary>The terrain tile entity this tree belongs to.</summary>
    public Entity tileEntity;
    
    /// <summary>Local position offset from tile origin (relative to tile's position).</summary>
    public float3 localOffset;
}

/// <summary>
/// Tag component marking a tree entity for global instance rendering.
/// Trees with this tag are rendered via Graphics.DrawMeshInstanced instead of individual ECS rendering.
/// This dramatically reduces draw calls by batching trees with the same mesh/material.
/// </summary>
public struct GlobalTreeInstance : IComponentData
{
}

/// <summary>
/// Unmanaged component storing indices for global tree instance rendering.
/// Uses indices instead of direct references for Burst compatibility and better performance.
/// References the GlobalTreeRenderingData singleton to resolve actual mesh/material.
/// </summary>
public struct GlobalTreeInstanceData : IComponentData
{
    /// <summary>Index into the GlobalTreeRenderingData.meshes array.</summary>
    public int meshIndex;
    
    /// <summary>Index into the GlobalTreeRenderingData.materials array.</summary>
    public int materialIndex;
    
    /// <summary>Index of the tree prefab in the TreePrefabElement buffer (for debugging).</summary>
    public int prefabIndex;
    
    /// <summary>Tree type index (0 to N-1 where N is number of tree types). Used to calculate LOD mesh indices.</summary>
    public int treeTypeIndex;
    
    /// <summary>Current LOD level (0=highest detail, 2=lowest detail).</summary>
    public byte currentLODLevel;
    
    /// <summary>Last calculated distance to player (used for LOD hysteresis).</summary>
    public float lastDistanceToPlayer;
}

/// <summary>
/// Singleton configuration for tree mesh LOD system.
/// Controls distance-based LOD switching with hysteresis to prevent flickering.
/// </summary>
public struct TreeLODConfig : IComponentData
{
    /// <summary>Distance threshold for LOD0->LOD1 transition (meters).</summary>
    public float lod0Distance;
    
    /// <summary>Distance threshold for LOD1->LOD2 transition (meters).</summary>
    public float lod1Distance;
    
    /// <summary>Distance beyond which trees use LOD2 (meters).</summary>
    public float lod2Distance;
    
    /// <summary>Hysteresis buffer to prevent LOD flickering (meters). Adds/subtracts from thresholds.</summary>
    public float hysteresisDelta;
    
    /// <summary>Number of LOD levels per tree type (hardcoded to 3).</summary>
    public int lodsPerTreeType;
    
    /// <summary>Maximum number of spatial chunks to update per frame for LOD calculations.</summary>
    public int maxChunksUpdatedPerFrame;
    
    /// <summary>Whether to enable tree LOD and spawning debug logging (disable to reduce console spam).</summary>
    public bool enableTreeLODDebug;
    
    /// <summary>Enable distance-based culling for tree rendering (trees beyond maxTreeRenderDistance won't render).</summary>
    public bool enableDistanceCulling;
    
    /// <summary>Maximum distance to render trees in meters. Trees beyond this distance are culled (not rendered). Quest 3 recommended: 300-500m.</summary>
    public float maxTreeRenderDistance;
    
    // QUEST 3 VR OPTIMIZATIONS
    
    /// <summary>Maximum number of unique mesh/material batch combinations. Default: 32. Increase if seeing capacity warnings.</summary>
    public int maxUniqueBatches;
    
    /// <summary>Frame skip interval when player velocity exceeds threshold during terrain scrolling. Default: 4 (update every 4th frame). Quest 3 recommended: 3-4.</summary>
    public int vrFrameSkipScrolling;
    
    /// <summary>Player velocity threshold (m/s) above which vrFrameSkipScrolling is used instead of base VRFrameSkip. Default: 0.5 m/s.</summary>
    public float playerVelocityThreshold;
}

/// <summary>
/// Component tracking which spatial chunk a tree belongs to for efficient LOD updates.
/// Chunks are 100m x 100m grid cells used to batch LOD update calculations.
/// </summary>
public struct TreeChunkMembership : IComponentData
{
    /// <summary>2D chunk coordinate (X, Z grid position).</summary>
    public int2 chunkCoord;
}

/// <summary>
/// Singleton managed component that stores mesh and material arrays for all tree types.
/// This allows thousands of tree entities to reference these arrays via indices,
/// dramatically reducing managed component lookups and enabling Burst compilation.
/// Stored on the same entity as TreeSpawnerConfig.
/// </summary>
public class GlobalTreeRenderingData : IComponentData
{
    /// <summary>Array of unique meshes used by tree prefabs.</summary>
    public Mesh[] meshes;
    
    /// <summary>Array of unique materials used by tree prefabs.</summary>
    public Material[] materials;
}

