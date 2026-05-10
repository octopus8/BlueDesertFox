using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Component storing cached distance from this tile to the player.
/// Updated by TerrainDistanceTrackingSystem.
/// </summary>
public struct TerrainTileDistanceToPlayer : IComponentData
{
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
    public BlobArray<float3> vertices;
    public BlobArray<int3> triangles;
    public int vertexCount;
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
    public BlobAssetReference<TerrainColliderBlob> colliderData;
}

/// <summary>
/// Buffer element for storing prepared collider vertices ready for MeshCollider.Create().
/// Used as intermediate storage between Burst job preparation and main-thread collider creation.
/// </summary>
public struct ColliderPreparedVertexElement : IBufferElementData
{
    public float3 value;
}

/// <summary>
/// Buffer element for storing prepared triangle indices.
/// </summary>
public struct ColliderPreparedTriangleElement : IBufferElementData
{
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
    public int priority; // Distance-based priority (lower = closer = higher priority)
}

/// <summary>
/// Key for caching collider BlobAssets based on generation parameters.
/// Allows tiles with identical parameters to share the same cached collider.
/// All tiles now use full-resolution geometry, so LOD is no longer part of the key.
/// </summary>
public struct ColliderCacheKey : IEquatable<ColliderCacheKey>
{
    public int verticesPerSide;
    public uint noiseParamsHash; // Hash of noise parameters
    
    public bool Equals(ColliderCacheKey other)
    {
        return verticesPerSide == other.verticesPerSide &&
               noiseParamsHash == other.noiseParamsHash;
    }
    
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
    public BlobAssetReference<TerrainColliderBlob> blobAsset;
    public long lastAccessFrame;
    public int estimatedMemoryBytes;
}


