using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

/// <summary>
/// Component storing cached distance from this tile to the player.
/// Updated by TerrainDistanceTrackingSystem.
/// </summary>
public struct TerrainTileDistanceToPlayer : IComponentData
{
    /// <summary>Distance in world units from this tile's centre to the player position.</summary>
    public float distance;
}

/// <summary>
/// Tag component indicating that the PhysicsCollider on this entity is valid and doesn't need regeneration.
/// Removed when mesh data changes, but NOT removed during origin shifts (colliders remain valid).
/// </summary>
public struct PhysicsColliderValid : IComponentData
{
}

/// <summary>
/// Blob asset containing pre-baked collider mesh data for a terrain tile.
/// Similar to SplineDataBlob pattern - allows efficient reuse and survives origin shifts.
/// </summary>
public struct TerrainColliderBlob
{
    /// <summary>Vertex positions for the collider mesh, stored in tile-local or world space.</summary>
    public BlobArray<float3> vertices;
    /// <summary>Triangle index triples (CCW winding) referencing entries in <see cref="vertices"/>.</summary>
    public BlobArray<int3> triangles;
    /// <summary>Total number of vertices in the collider mesh.</summary>
    public int vertexCount;
    /// <summary>Total number of triangles in the collider mesh.</summary>
    public int triangleCount;
    
    /// <summary>
    /// Creates a BlobAssetReference containing collider mesh data.
    /// Memory estimation: vertexCount * 12 bytes (float3) + triangleCount * 12 bytes (int3)
    /// </summary>
    public static BlobAssetReference<TerrainColliderBlob> Create(
        NativeArray<float3> sourceVertices,
        NativeArray<int3> sourceTriangles,
        Allocator allocator)
    {
        var builder = new BlobBuilder(Allocator.Temp);
        ref TerrainColliderBlob root = ref builder.ConstructRoot<TerrainColliderBlob>();
        
        // Build vertex array
        var vertexArray = builder.Allocate(ref root.vertices, sourceVertices.Length);
        for (int i = 0; i < sourceVertices.Length; i++)
        {
            vertexArray[i] = sourceVertices[i];
        }
        
        // Build triangle array
        var triangleArray = builder.Allocate(ref root.triangles, sourceTriangles.Length);
        for (int i = 0; i < sourceTriangles.Length; i++)
        {
            triangleArray[i] = sourceTriangles[i];
        }
        
        root.vertexCount = sourceVertices.Length;
        root.triangleCount = sourceTriangles.Length;
        
        var result = builder.CreateBlobAssetReference<TerrainColliderBlob>(allocator);
        builder.Dispose();
        
        return result;
    }
}

/// <summary>
/// Component holding a reference to pre-baked collider data as a BlobAsset.
/// </summary>
public struct TerrainPhysicsColliderComponent : IComponentData
{
    /// <summary>Reference to the cached blob asset holding the pre-baked collider mesh data.</summary>
    public BlobAssetReference<TerrainColliderBlob> colliderData;
}

/// <summary>
/// Buffer element for storing prepared collider vertices ready for MeshCollider.Create().
/// Used as intermediate storage between Burst job preparation and main-thread collider creation.
/// </summary>
public struct ColliderPreparedVertexElement : IBufferElementData
{
    /// <summary>World-space vertex position written by the Burst preparation job.</summary>
    public float3 value;
}

/// <summary>
/// Buffer element for storing prepared triangle indices.
/// </summary>
public struct ColliderPreparedTriangleElement : IBufferElementData
{
    /// <summary>Triangle index triple (x, y, z = vertex indices) written by the Burst preparation job.</summary>
    public int3 value;
}

/// <summary>
/// Component indicating this tile needs collider preparation job to run.
/// Added when mesh changes or collider needs to be created.
/// </summary>
public struct PhysicsColliderNeedsPreparation : IComponentData, IEnableableComponent
{
}

/// <summary>
/// Component indicating this tile has prepared collider data ready for MeshCollider.Create().
/// Added after preparation job completes, removed after collider is created.
/// Priority is distance-based (lower = closer = higher priority).
/// </summary>
public struct PhysicsColliderPrepared : IComponentData
{
    /// <summary>
    /// Distance-based priority score for this tile's collider creation.
    /// Lower values indicate tiles closer to the camera and are processed first.
    /// </summary>
    public int priority;
}

/// <summary>
/// Holds a created MeshCollider blob awaiting registration with the physics world next frame.
/// Separates expensive MeshCollider.Create from PhysicsCollider component addition.
/// </summary>
public struct PhysicsColliderRegistrationPending : IComponentData
{
    /// <summary>The fully-created Unity Physics collider blob awaiting registration with the physics world.</summary>
    public BlobAssetReference<Collider> collider;
}

/// <summary>
/// Key for caching collider BlobAssets based on generation parameters.
/// Allows tiles with identical parameters to share the same cached collider.
/// All tiles now use full-resolution geometry, so LOD is no longer part of the key.
/// </summary>
public struct ColliderCacheKey : IEquatable<ColliderCacheKey>
{
    /// <summary>Number of vertices per side of the tile grid, identifying the mesh resolution.</summary>
    public int verticesPerSide;
    /// <summary>Combined hash of all noise parameters (<c>frequency</c>, <c>amplitude</c>, <c>octaves</c>, etc.)
    /// used to distinguish tiles generated with different terrain configurations.</summary>
    public uint noiseParamsHash;
    
    /// <summary>Returns <c>true</c> if both keys have identical resolution and noise parameter hashes.</summary>
    public bool Equals(ColliderCacheKey other)
    {
        return verticesPerSide == other.verticesPerSide &&
               noiseParamsHash == other.noiseParamsHash;
    }
    
    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return verticesPerSide.GetHashCode() ^
               (int)noiseParamsHash;
    }
    
    /// <summary>
    /// Creates a cache key from terrain configuration.
    /// </summary>
    public static ColliderCacheKey FromConfig(TerrainTileConfig config)
    {
        // Combine all noise parameters into a single hash
        uint hash = (uint)config.noiseFrequency.GetHashCode();
        hash ^= (uint)config.noiseAmplitude.GetHashCode() << 8;
        hash ^= (uint)config.noiseOctaves << 16;
        hash ^= (uint)config.noiseLacunarity.GetHashCode() << 4;
        hash ^= (uint)config.noisePersistence.GetHashCode() << 12;
        
        return new ColliderCacheKey
        {
            verticesPerSide = config.verticesPerSide,
            noiseParamsHash = hash
        };
    }
}

/// <summary>
/// Entry in the LRU cache for tracking BlobAsset usage and memory.
/// </summary>
public struct ColliderCacheEntry
{
    /// <summary>The cached blob asset containing pre-baked collider geometry for a specific key.</summary>
    public BlobAssetReference<TerrainColliderBlob> blobAsset;
    /// <summary>Frame number when this entry was last accessed, used by the LRU eviction policy.</summary>
    public long lastAccessFrame;
    /// <summary>Estimated memory footprint of <see cref="blobAsset"/> in bytes, used to enforce the memory budget.</summary>
    public int estimatedMemoryBytes;
}

/// <summary>
/// Resolves the effective per-frame physics collider creation budget from terrain config.
/// Uses the minimum of both budget fields so either inspector slider caps physics work.
/// </summary>
public static class TerrainPhysicsBudget
{
    /// <summary>
    /// Returns the effective maximum number of physics colliders that may be created in a single frame.
    /// Takes the minimum of <see cref="TerrainTileConfig.maxPhysicsCollidersCreatedPerFrame"/> and
    /// <see cref="TerrainTileConfig.maxCollidersCreatedPerFrame"/> (when either is positive), ensuring
    /// either inspector slider can cap physics work. Returns at least 1.
    /// </summary>
    /// <param name="config">The terrain tile configuration containing the budget settings.</param>
    /// <returns>Maximum colliders to create this frame (always &gt;= 1).</returns>
    public static int GetCreationBudget(in TerrainTileConfig config)
    {
        int budget = int.MaxValue;

        if (config.maxPhysicsCollidersCreatedPerFrame > 0)
        {
            budget = math.min(budget, config.maxPhysicsCollidersCreatedPerFrame);
        }

        if (config.maxCollidersCreatedPerFrame > 0)
        {
            budget = math.min(budget, config.maxCollidersCreatedPerFrame);
        }

        return math.max(1, budget == int.MaxValue ? 4 : budget);
    }
}


