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
/// Component indicating this tile needs collider BVH construction.
/// Added when mesh is ready and tile is within maxColliderDistance.
/// </summary>
public struct PhysicsColliderNeedsPreparation : IComponentData, IEnableableComponent
{
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
/// Singleton tracking aggregate terrain collider blob cache memory for LRU eviction.
/// </summary>
public struct TerrainColliderCacheStats : IComponentData
{
    public int entryCount;
    public int totalMemoryBytes;
}

/// <summary>
/// Resolves per-stage physics collider creation budgets from terrain config.
/// </summary>
public static class TerrainPhysicsBudget
{
    /// <summary>
    /// Returns the mesh-generation / prep-marking budget (<see cref="TerrainTileConfig.maxCollidersCreatedPerFrame"/>).
    /// </summary>
    public static int GetPrepMarkBudget(in TerrainTileConfig config)
    {
        if (config.maxCollidersCreatedPerFrame > 0)
            return config.maxCollidersCreatedPerFrame;
        return 4;
    }

    /// <summary>
    /// Returns the BVH / MeshCollider.Create budget (<see cref="TerrainTileConfig.maxPhysicsCollidersCreatedPerFrame"/>).
    /// Clamped to 1–2 on mobile platforms to bound worst-case Complete() spike time on Quest.
    /// </summary>
    public static int GetBvhCreationBudget(in TerrainTileConfig config)
    {
        int budget = config.maxPhysicsCollidersCreatedPerFrame > 0
            ? config.maxPhysicsCollidersCreatedPerFrame
            : 4;

#if !UNITY_EDITOR
        if (UnityEngine.Application.isMobilePlatform)
            budget = math.min(budget, 2);
#endif

        return math.max(1, budget);
    }

    /// <summary>
    /// Returns the registration budget for attaching pending collider blobs (matches BVH throughput).
    /// </summary>
    public static int GetRegistrationBudget(in TerrainTileConfig config)
        => GetBvhCreationBudget(config);

    /// <summary>
    /// Legacy combined budget — prefer stage-specific helpers above.
    /// </summary>
    public static int GetCreationBudget(in TerrainTileConfig config)
        => math.min(GetPrepMarkBudget(config), GetBvhCreationBudget(config));
}

/// <summary>
/// Camera-aware collider priority scoring shared by distance tracking and BVH scheduling.
/// Lower score = higher priority.
/// </summary>
public static class TerrainColliderPriority
{
    /// <summary>
    /// Computes a priority score from tile grid coordinate and camera pose.
    /// Lower values are processed first.
    /// </summary>
    public static int Compute(in int2 gridCoord, in TerrainTileConfig config, in CameraDataSingleton camera)
    {
        float2 tileCenter = new float2(
            gridCoord.x * config.tileSize + config.tileSize * 0.5f,
            gridCoord.y * config.tileSize + config.tileSize * 0.5f);
        float2 cameraPos2D = new float2(camera.position.x, camera.position.z);
        float2 toTile = tileCenter - cameraPos2D;
        float dist2D = math.length(toTile);
        float normalizedDist = math.clamp(dist2D / config.viewDistance, 0f, 1f);
        float2 fwd2D = math.normalize(new float2(camera.forward.x, camera.forward.z));
        float2 toTileNorm = math.lengthsq(toTile) < 0.001f ? fwd2D : math.normalize(toTile);
        float dot = math.dot(fwd2D, toTileNorm);
        float viewScore = (dot + 1f) * 0.5f;
        return (int)((1f - viewScore) * 1000f + normalizedDist * 500f);
    }
}
