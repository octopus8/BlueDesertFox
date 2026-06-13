using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Shared bullet pool return logic used by lifecycle and collision systems (zero-GC path).
/// </summary>
public static class BulletPoolUtilities
{
    /// <summary>
    /// Deactivates a bullet and returns it to the pool in a single zero-allocation call.
    /// Resets <see cref="BulletData"/> to inactive, zeros <see cref="PhysicsVelocity"/>,
    /// moves the bullet's <see cref="LocalTransform"/> below the map, and enqueues it back into
    /// <paramref name="pool"/> via <see cref="BulletPoolSystem.ReturnToPool"/>.
    /// </summary>
    /// <param name="bullet">The bullet entity to deactivate.</param>
    /// <param name="pool">Reference to the <see cref="BulletPoolSystem"/> that owns the pool queue.</param>
    /// <param name="bulletDataLookup">Write-access lookup for <see cref="BulletData"/> components.</param>
    /// <param name="localTransformLookup">Write-access lookup for <see cref="LocalTransform"/> components.</param>
    /// <param name="physicsVelocityLookup">Write-access lookup for <see cref="PhysicsVelocity"/> components.</param>
    public static void DeactivateAndReturn(
        Entity bullet,
        ref BulletPoolSystem pool,
        ref ComponentLookup<BulletData> bulletDataLookup,
        ref ComponentLookup<LocalTransform> localTransformLookup,
        ref ComponentLookup<PhysicsVelocity> physicsVelocityLookup)
    {
        bulletDataLookup[bullet] = new BulletData
        {
            spawnPosition = float3.zero,
            creationTime = 0,
            active = false,
            linearVelocityTerrainRelative = float3.zero
        };

        if (physicsVelocityLookup.HasComponent(bullet))
        {
            physicsVelocityLookup[bullet] = new PhysicsVelocity
            {
                Linear = float3.zero,
                Angular = float3.zero
            };
        }

        var transform = localTransformLookup[bullet];
        transform.Position = new float3(0, -10000, 0);
        localTransformLookup[bullet] = transform;

        pool.ReturnToPool(bullet);
    }
}
