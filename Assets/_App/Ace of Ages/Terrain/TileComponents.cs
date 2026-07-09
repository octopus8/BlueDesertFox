using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
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

    /// <summary>Constant terrain grade along world +Z in degrees. Positive = uphill as Z increases.</summary>
    public float slopeAngleDegrees;

    /// <summary>Per-tile grade variation in degrees subtracted from <see cref="slopeAngleDegrees"/>. 0 = uniform grade.</summary>
    public float slopeAngleVariation;

    /// <summary>Meters of blend zone centered on each tile Z-boundary where adjacent tile grades crossfade.</summary>
    public float slopeVariationBlendDistance;
    
    /// <summary>Base frequency of the Perlin noise used for height generation (e.g. 0.01). Higher values = smaller terrain features.</summary>
    public float noiseFrequency;
    /// <summary>Maximum height amplitude in world units (e.g. 20). Scales the full noise range to this height.</summary>
    public float noiseAmplitude;
    /// <summary>Number of fractal octaves summed for the noise (e.g. 4). More octaves add fine detail but cost CPU.</summary>
    public int noiseOctaves;
    /// <summary>Frequency multiplier between successive noise octaves (e.g. 2.0). Controls how quickly higher octaves add finer detail.</summary>
    public float noiseLacunarity;
    /// <summary>Amplitude reduction factor between successive noise octaves (e.g. 0.5). Values &lt; 1 make higher octaves progressively quieter.</summary>
    public float noisePersistence;

    /// <summary>
    /// Frequency of the low-frequency continental mask noise (e.g. 0.0008). Controls the scale
    /// of flat-plains versus mountain regions. Lower values produce larger continental features.
    /// </summary>
    public float continentalFrequency;
    /// <summary>
    /// Power exponent applied to the continental mask (e.g. 2.5). Values greater than 1 push
    /// more of the map toward flat plains, while values near 1 produce uniform highlands.
    /// </summary>
    public float continentalExponent;
    
    // Physics optimization parameters
    /// <summary>Maximum number of terrain meshes generated per frame (prevents stalls).</summary>
    public int maxCollidersCreatedPerFrame;

    /// <summary>Maximum number of physics colliders created per frame (main-thread MeshCollider.Create budget).</summary>
    public int maxPhysicsCollidersCreatedPerFrame;
    
    /// <summary>Distance threshold beyond which colliders are removed completely.</summary>
    public float maxColliderDistance;
    
    /// <summary>Maximum memory in megabytes for the grid-coordinate collider blob LRU cache.</summary>
    public int maxColliderCacheMemoryMB;
    
    /// <summary>Physics layer index for terrain colliders.</summary>
    public int terrainPhysicsLayer;

    /// <summary>Unity Physics material applied when creating terrain mesh colliders.</summary>
    public Unity.Physics.Material terrainColliderMaterial;
    
    /// <summary>Whether to render terrain tiles (disable for tree-only testing).</summary>
    public bool renderTerrain;
    
    /// <summary>Whether to generate physics colliders for terrain tiles (disable for debugging/performance testing).</summary>
    public bool enablePhysicsColliders;
}

/// <summary>
/// Per-trail settings. Height is shared across all trails and lives on <see cref="TrailConfig"/>.
/// The centerline of each trail is defined by centerX(Z) = amplitude * snoise(Z * frequency + seed, 0),
/// guaranteeing the path always advances in the +Z direction (cannot turn past 90°).
/// Trail cross-section is level (ski-trail style): grade uses world +Z at the nearest centerline sample.
/// </summary>
public struct TrailInstanceConfig
{
    /// <summary>Whether this trail is active.</summary>
    public bool enabled;

    /// <summary>Width of the fully-flat portion of the trail in world units.</summary>
    public float width;

    /// <summary>Width of the smooth blend zone on each side of the flat portion, in world units.</summary>
    public float blendWidth;

    /// <summary>Noise offset / random seed. Different values produce different weave patterns.</summary>
    public float seed;

    /// <summary>How rapidly the trail weaves along the Z axis. Higher values = tighter turns.</summary>
    public float frequency;

    /// <summary>Maximum left/right deviation of the trail centerline in world units.</summary>
    public float amplitude;
}

/// <summary>
/// Singleton configuration for up to three procedural winding trails carved flat into the terrain.
/// All trails share a single Y height value; each trail has its own shape parameters via
/// <see cref="TrailInstanceConfig"/>. Where trails overlap the maximum carve influence wins.
/// Cross-section stays level perpendicular to the winding centerline; downhill grade follows
/// <see cref="TerrainTileConfig.slopeAngleDegrees"/> at the nearest centerline world Z.
/// </summary>
public struct TrailConfig : IComponentData
{
    /// <summary>Y height of all flat trail surfaces in world units (shared by trail1/2/3).</summary>
    public float height;

    /// <summary>
    /// Spacing in meters between centerline LUT samples used for mesh generation and spawn exclusion.
    /// Lower values sharpen blend edges; higher values reduce LUT build cost.
    /// </summary>
    public float lutStepMeters;

    public TrailInstanceConfig trail1;
    public TrailInstanceConfig trail2;
    public TrailInstanceConfig trail3;
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
    /// <summary>World-space (or tile-local) position of this vertex.</summary>
    public float3 value;
}

/// <summary>
/// Buffer element for storing vertex normals.
/// </summary>
public struct NormalElement : IBufferElementData
{
    /// <summary>Normalized surface normal for this vertex, used for lighting calculations.</summary>
    public float3 value;
}

/// <summary>
/// Buffer element for storing UV coordinates.
/// </summary>
public struct UVElement : IBufferElementData
{
    /// <summary>Texture UV coordinate (0–1 range) for this vertex.</summary>
    public float2 value;
}

/// <summary>
/// Buffer element for storing triangle indices.
/// </summary>
public struct IndexElement : IBufferElementData
{
    /// <summary>Index into the vertex buffer identifying one corner of a triangle.</summary>
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
/// Smoothed world-space horizontal velocity of the player for ballistic intercept (turrets etc.).
/// Updated by <see cref="PlayerTargetVelocityEstimateSystem"/> from finite differences on
/// <see cref="PlayerTransformReference"/>.
/// </summary>
public struct PlayerTargetVelocity : IComponentData
{
    /// <summary>Smoothed velocity on XZ (world units/sec); Y kept at 0.</summary>
    public float3 horizontal;
    /// <summary>Player world position sampled on the previous frame, used to compute finite-difference velocity.</summary>
    public float3 lastWorldPosition;
    /// <summary>Whether <see cref="lastWorldPosition"/> has been populated from at least one prior frame.</summary>
    public bool hasPrevious;
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
        FindMainCamera = 2
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
/// Used for auto-scrolling terrain in the direction the player is facing (XZ plane)
/// and for pitch-driven vertical scroll (Y axis).
/// </summary>
public struct ScrollOffset : IComponentData
{
    /// <summary>
    /// Total distance the terrain has scrolled as a directional vector.
    /// XZ components come from horizontal scroll; Y from pitch-driven vertical scroll.
    /// This offset is subtracted from tile positions to create the scrolling effect.
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
    /// Normalized direction vector for horizontal scrolling (expected to be pre-normalized by provider system).
    /// </summary>
    public float3 direction;
    
    /// <summary>
    /// Horizontal scroll speed in units per second. Set to 0 to disable horizontal scrolling.
    /// </summary>
    public float speed;

    /// <summary>
    /// Vertical scroll speed in units per second (positive = terrain moves down in world space).
    /// Driven by player pitch when using <see cref="PlayerScrollVelocitySystem"/>.
    /// </summary>
    public float verticalSpeed;

    /// <summary>Combined world-space terrain velocity (horizontal + vertical).</summary>
    public readonly float3 WorldVelocity => direction * speed + new float3(0f, verticalSpeed, 0f);
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
/// Singleton component that configures static object spawning on terrain tiles.
/// </summary>
public struct StaticObjectSpawnerConfig : IComponentData
{
    /// <summary>Random seed offset added to tile grid coordinates for deterministic placement.</summary>
    public int randomSeed;

    /// <summary>Minimum number of Objects to spawn per tile.</summary>
    public int minObjectsPerTile;
    
    /// <summary>Maximum number of Objects to spawn per tile.</summary>
    public int maxObjectsPerTile;

    /// <summary>Spawn acceptance multiplier at trail center (0 = none, 1 = same as open terrain). Blend zone interpolates to 1.0.</summary>
    public float trailSpawnDensityMultiplier;
    
    /// <summary>Pre-calculated slope threshold (cosine of max slope angle) for filtering steep terrain.</summary>
    public float slopeThreshold;
    
    /// <summary>Maximum number of Objects to spawn per frame (performance budgeting).</summary>
    public int maxObjectsSpawnedPerFrame;

    /// <summary>Maximum objects to spawn per frame for tiles within LOD0 distance (near-field burst budget).</summary>
    public int maxNearObjectsSpawnedPerFrame;

    /// <summary>Maximum spawn-position rejection attempts per frame (spread across active tiles).</summary>
    public int maxPositionCalcAttemptsPerFrame;
}

/// <summary>
/// Buffer element that stores a reference to a object prefab entity for random selection.
/// </summary>
public struct StaticObjectPrefabElement : IBufferElementData
{
    /// <summary>The entity prefab to instantiate for this object type.</summary>
    public Entity prefabEntity;
}


/// <summary>
/// Tag component indicating that static objects have been spawned for this tile.
/// </summary>
public struct StaticObjectsSpawned : IComponentData
{
}

/// <summary>
/// Tracks partial static object instantiation progress on a tile.
/// Present while <see cref="StaticObjectSpawnPosition"/> entries remain to be instantiated.
/// </summary>
public struct StaticObjectSpawnProgress : IComponentData
{
    /// <summary>Next index in <see cref="StaticObjectSpawnPosition"/> buffer to instantiate.</summary>
    public int nextSpawnIndex;
}

/// <summary>
/// Tracks incremental spawn-position calculation on a tile across frames.
/// Removed when all positions are calculated and instantiation begins or completes.
/// </summary>
public struct StaticObjectPositionCalcProgress : IComponentData
{
    /// <summary>Deterministic target object count for this tile.</summary>
    public int targetCount;

    /// <summary>Accepted spawn positions written so far.</summary>
    public int acceptedCount;

    /// <summary>Total rejection-sampling attempts consumed.</summary>
    public int attempts;

    /// <summary>Persisted RNG state for deterministic cross-frame continuation (0 = uninitialized).</summary>
    public uint randomState;
}

/// <summary>
/// Marks a terrain tile scheduled for budgeted despawn. Static objects are destroyed over multiple
/// frames before the tile entity itself is destroyed.
/// </summary>
public struct PendingTileDespawn : IComponentData
{
}

/// <summary>
/// Temporary buffer element storing calculated static object spawn data for deferred instantiation.
/// Calculated by Burst job, consumed incrementally by ECB-based instantiation, then cleared when complete.
/// </summary>
public struct StaticObjectSpawnPosition : IBufferElementData
{
    /// <summary>Position relative to tile origin (tile-local space).</summary>
    public float3 localPosition;
    
    /// <summary>World-space position for the object.</summary>
    public float3 worldPosition;
    
    /// <summary>Random Y-axis rotation for visual variety.</summary>
    public quaternion rotation;
    
    /// <summary>Object type index (0 to N-1 where N is number of object types).</summary>
    public int objectTypeIndex;
    
    /// <summary>Initial LOD level based on distance to camera (0=LOD0, 1=LOD1, 2=LOD2).</summary>
    public byte initialLODLevel;
    
    /// <summary>Initial distance to player/camera for LOD calculation.</summary>
    public float initialDistance;
    
    /// <summary>Initial mesh index based on object type and LOD level.</summary>
    public int initialMeshIndex;
    
    /// <summary>Uniform scale for this instance (prefab base scale plus random delta).</summary>
    public float scale;
}

/// <summary>
/// Buffer element that stores references to static objects spawned on this tile for cleanup.
/// </summary>
public struct SpawnedStaticObjectReference : IBufferElementData
{
    /// <summary>The entity of a static object spawned on this tile.</summary>
    public Entity objectEntity;
}

/// <summary>
/// Component that tracks which terrain tile a static object belongs to and its local offset.
/// Used to update object positions when tiles move, without using parent-child hierarchy.
/// </summary>
public struct StaticObjectTileOwnership : IComponentData
{
    /// <summary>The terrain tile entity this object belongs to.</summary>
    public Entity tileEntity;
    
    /// <summary>Local position offset from tile origin (relative to tile's position).</summary>
    public float3 localOffset;

    /// <summary>World-space Y rotation baked at spawn (reapplied each frame when tiles scroll).</summary>
    public quaternion localRotation;
}

/// <summary>
/// Tag component marking a static object entity as part of the static object system.
/// Root entities with this tag are rendered via Entities.Graphics (BRG) using their MaterialMeshInfo component.
/// </summary>
public struct GlobalStaticObjectInstance : IComponentData
{
}

/// <summary>
/// One-frame marker after ECB instantiate: stripped render components must be applied to all
/// entities in LinkedEntityGroup once instantiation has played back (parallel ECB cannot enumerate children beforehand).
/// </summary>
public struct PendingStaticObjectRendererStrip : IComponentData
{
}

/// <summary>
/// Unmanaged component storing LOD state for static object instance rendering.
/// The actual mesh/material is stored in the entity's MaterialMeshInfo component (Entities.Graphics).
/// </summary>
public struct GlobalStaticObjectInstanceData : IComponentData
{
    /// <summary>Index of the object prefab in the StaticObjectPrefabElement buffer (for debugging).</summary>
    public int prefabIndex;
    
    /// <summary>Object type index (0 to N-1 where N is number of object types). Used to calculate LOD mesh indices.</summary>
    public int objectTypeIndex;
    
    /// <summary>Current LOD level (0=highest detail, 2=lowest detail).</summary>
    public byte currentLODLevel;
    
    /// <summary>Last calculated distance to player (used for LOD hysteresis).</summary>
    public float lastDistanceToPlayer;
    
    /// <summary>When true, LOD2 is a camera-facing billboard that should rotate to face the camera each frame.</summary>
    public bool isBillboardType;
    
    /// <summary>Spawn scale relative to LOD0 prefab (base scale plus random delta).</summary>
    public float spawnScale;
}

/// <summary>
/// Singleton configuration for static object mesh LOD system.
/// Controls distance-based LOD switching with hysteresis to prevent flickering.
/// </summary>
public struct StaticObjectLODConfig : IComponentData
{
    /// <summary>Distance threshold for LOD0->LOD1 transition (meters).</summary>
    public float lod0Distance;
    
    /// <summary>Distance threshold for LOD1->LOD2 transition (meters).</summary>
    public float lod1Distance;
    
    /// <summary>Distance beyond which objects use LOD2 (meters).</summary>
    public float lod2Distance;
    
    /// <summary>Hysteresis buffer to prevent LOD flickering (meters). Adds/subtracts from thresholds.</summary>
    public float hysteresisDelta;
    
    /// <summary>Number of LOD levels per object type (hardcoded to 3).</summary>
    public int lodsPerObjectType;
    
    /// <summary>Maximum number of spatial chunks to update per frame for LOD calculations.</summary>
    public int maxChunksUpdatedPerFrame;
    
    // QUEST 3 VR OPTIMIZATIONS
    
    /// <summary>Frame skip interval when player velocity exceeds threshold during terrain scrolling. Default: 4 (update every 4th frame). Quest 3 recommended: 3-4.</summary>
    public int vrFrameSkipScrolling;
    
    /// <summary>Player velocity threshold (m/s) above which vrFrameSkipScrolling is used instead of base VRFrameSkip. Default: 0.5 m/s.</summary>
    public float playerVelocityThreshold;
}

/// <summary>
/// Component tracking which spatial chunk a static object belongs to for efficient LOD updates.
/// Chunks are 100m x 100m grid cells used to batch LOD update calculations.
/// </summary>
public struct StaticObjectChunkMembership : IComponentData
{
    /// <summary>2D chunk coordinate (X, Z grid position).</summary>
    public int2 chunkCoord;
}

/// <summary>
/// Buffer element storing the pre-registered MaterialMeshInfo for each LOD slot.
/// Index = objectTypeIndex * lodsPerObjectType + lodLevel.
/// Populated at world startup by StaticObjectLODMeshInfoInitSystem from baked prefab entities.
/// Used by the LOD update and spawning systems to switch mesh/material via Entities.Graphics.
/// </summary>
public struct StaticObjectLODMaterialMeshInfoElement : IBufferElementData
{
    public Unity.Rendering.MaterialMeshInfo materialMeshInfo;
}

/// <summary>
/// Per-LOD-prefab render bounds lookup (same index order as StaticObjectLODMaterialMeshInfoElement).
/// </summary>
public struct StaticObjectLODRenderBoundsElement : IBufferElementData
{
    public Unity.Mathematics.AABB bounds;
}

/// <summary>
/// Conservative max render bounds per object type (union of all LOD prefab bounds).
/// Used at spawn time so frustum culling is safe before the first LOD pass runs.
/// </summary>
public struct StaticObjectTypeMaxRenderBoundsElement : IBufferElementData
{
    public Unity.Mathematics.AABB bounds;
}

/// <summary>
/// Tag component placed on the config entity once StaticObjectLODMeshInfoInitSystem has finished
/// populating the StaticObjectLODMaterialMeshInfoElement buffer.
/// Systems that need the LOD MaterialMeshInfo lookup table gate on this tag.
/// </summary>
public struct StaticObjectLODMeshInfoReady : IComponentData
{
}

/// <summary>
/// Buffer element that stores normalized spawn weight for each object type.
/// Determines the probability distribution for selecting which object type to spawn.
/// Weights are normalized to sum to 1.0 during baking.
/// </summary>
public struct StaticObjectTypeSpawnWeight : IBufferElementData
{
    /// <summary>Object type index (0 to N-1 where N is number of object types).</summary>
    public int objectTypeIndex;
    
    /// <summary>Normalized spawn probability for this object type. Range [0.0, 1.0].</summary>
    public float weight;
}

/// <summary>
/// Buffer element storing per-object-type billboard flag, indexed by objectTypeIndex.
/// Populated at bake time by <see cref="StaticObjectSpawnerConfigAuthoring.Baker"/>.
/// When true, LOD2 for that object type is a camera-facing billboard.
/// </summary>
public struct StaticObjectBillboardTypeElement : IBufferElementData
{
    /// <summary>When true, LOD2 for this object type rotates to face the camera each frame.</summary>
    public bool isBillboard;
}

/// <summary>
/// Per-object-type scale configuration baked from LOD0 prefab transform and entry maxScaleDelta.
/// Indexed by objectTypeIndex.
/// </summary>
public struct StaticObjectTypeScaleElement : IBufferElementData
{
    /// <summary>Base uniform scale from LOD0 prefab (max axis of lossyScale).</summary>
    public float baseScale;
    
    /// <summary>Maximum random scale offset applied per instance (+/- this value).</summary>
    public float maxScaleDelta;
    
    /// <summary>LOD1 display scale multiplier relative to LOD0 prefab scale.</summary>
    public float lod1ScaleMultiplier;
    
    /// <summary>LOD2 display scale multiplier relative to LOD0 prefab scale.</summary>
    public float lod2ScaleMultiplier;
    
    /// <summary>Returns display scale multiplier for the given LOD level relative to LOD0 spawn scale.</summary>
    public float GetLodScaleMultiplier(byte lodLevel)
    {
        if (lodLevel == 1)
            return lod1ScaleMultiplier;
        if (lodLevel == 2)
            return lod2ScaleMultiplier;
        return 1f;
    }
}


