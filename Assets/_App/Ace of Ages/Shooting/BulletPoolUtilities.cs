using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Shared bullet pool return logic used by lifecycle and collision systems (zero-GC path).
/// </summary>
public static class BulletPoolUtilities
{
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
