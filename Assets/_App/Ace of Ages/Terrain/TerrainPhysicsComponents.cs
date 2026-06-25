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

