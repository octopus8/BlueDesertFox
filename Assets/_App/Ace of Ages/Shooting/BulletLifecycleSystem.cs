using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// System that manages bullet lifecycle - returns bullets to pool when they exceed max lifetime.
/// Uses time-based TTL (4 seconds) rather than world-space distance so that bullets fired with
/// terrain scroll velocity baked in expire consistently regardless of scroll speed.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(BulletShooterSystem))]
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct BulletLifecycleSystem : ISystem
{
    private const double BULLET_MAX_LIFETIME = 4.0;

    private ComponentLookup<BulletData> _bulletDataLookup;
    private ComponentLookup<LocalTransform> _localTransformLookup;
    private ComponentLookup<PhysicsVelocity> _physicsVelocityLookup;

    /// <summary>Registers the <see cref="Bullet"/> requirement and caches component lookup handles.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Bullet>();

        _bulletDataLookup = state.GetComponentLookup<BulletData>(isReadOnly: false);
        _localTransformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: false);
        _physicsVelocityLookup = state.GetComponentLookup<PhysicsVelocity>(isReadOnly: false);
    }

    /// <summary>
    /// Iterates all active bullets and returns any whose elapsed lifetime exceeds <c>BULLET_MAX_LIFETIME</c>
    /// (4 seconds) to the <see cref="BulletPoolSystem"/> via <see cref="BulletPoolUtilities.DeactivateAndReturn"/>.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        var poolSystemHandle = state.World.GetExistingSystem<BulletPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
            return;

        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<BulletPoolSystem>(poolSystemHandle);

        _bulletDataLookup.Update(ref state);
        _localTransformLookup.Update(ref state);
        _physicsVelocityLookup.Update(ref state);

        double currentTime = SystemAPI.Time.ElapsedTime;

        foreach (var (bulletData, entity) in
            SystemAPI.Query<RefRO<BulletData>>()
                .WithAll<Bullet>()
                .WithEntityAccess())
        {
            if (!bulletData.ValueRO.active)
                continue;

            if (currentTime - bulletData.ValueRO.creationTime > BULLET_MAX_LIFETIME)
            {
                BulletPoolUtilities.DeactivateAndReturn(
                    entity,
                    ref poolSystem,
                    ref _bulletDataLookup,
                    ref _localTransformLookup,
                    ref _physicsVelocityLookup);
            }
        }
    }
}
