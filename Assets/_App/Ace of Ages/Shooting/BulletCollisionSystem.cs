using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

/// <summary>
/// System that detects bullet collisions and returns them to the pool.
/// Checks for collisions with terrain and enemies.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(PhysicsSystemGroup))]
public partial struct BulletCollisionSystem : ISystem
{
    private ComponentLookup<Bullet> _bulletLookup;
    private ComponentLookup<BulletData> _bulletDataLookup;
    private ComponentLookup<TerrainTile> _terrainTileLookup;
    private ComponentLookup<LocalTransform> _localTransformLookup;
    private ComponentLookup<PhysicsVelocity> _physicsVelocityLookup;

    /// <summary>
    /// Registers required singletons and pre-fetches all <see cref="ComponentLookup{T}"/> handles
    /// used during collision processing to avoid per-frame allocation.
    /// </summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Bullet>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<PrefabEntitiesReferences>();

        _bulletLookup = state.GetComponentLookup<Bullet>(isReadOnly: true);
        _bulletDataLookup = state.GetComponentLookup<BulletData>(isReadOnly: false);
        _terrainTileLookup = state.GetComponentLookup<TerrainTile>(isReadOnly: true);
        _localTransformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: false);
        _physicsVelocityLookup = state.GetComponentLookup<PhysicsVelocity>(isReadOnly: false);
    }

    /// <summary>
    /// Updates component lookups, iterates all physics collision events for the current fixed step,
    /// returns colliding active bullets to the pool via <see cref="BulletPoolUtilities.DeactivateAndReturn"/>,
    /// and triggers dirt explosion VFX at terrain impact positions.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        var poolSystemHandle = state.World.GetExistingSystem<BulletPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
            return;

        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<BulletPoolSystem>(poolSystemHandle);

        var explosionPoolSystemHandle = state.World.GetExistingSystem<DirtExplosionPoolSystem>();
        bool hasExplosionPool = explosionPoolSystemHandle != SystemHandle.Null;

        _bulletLookup.Update(ref state);
        _bulletDataLookup.Update(ref state);
        _terrainTileLookup.Update(ref state);
        _localTransformLookup.Update(ref state);
        _physicsVelocityLookup.Update(ref state);

        var simulationSingleton = SystemAPI.GetSingleton<SimulationSingleton>();

        state.Dependency.Complete();

        var bulletsToReturn = new NativeHashSet<Entity>(32, Allocator.Temp);
        var terrainCollisionPositions = new NativeList<float3>(32, Allocator.Temp);

        var collisionEvents = simulationSingleton.AsSimulation().CollisionEvents;
        foreach (var collisionEvent in collisionEvents)
        {
            Entity entityA = collisionEvent.EntityA;
            Entity entityB = collisionEvent.EntityB;

            bool aIsBullet = _bulletLookup.HasComponent(entityA);
            bool bIsBullet = _bulletLookup.HasComponent(entityB);
            bool aIsTerrain = _terrainTileLookup.HasComponent(entityA);
            bool bIsTerrain = _terrainTileLookup.HasComponent(entityB);

            if (aIsBullet)
                TryCollectBullet(entityA, bIsTerrain, bulletsToReturn, terrainCollisionPositions);

            if (bIsBullet)
                TryCollectBullet(entityB, aIsTerrain, bulletsToReturn, terrainCollisionPositions);
        }

        foreach (Entity bullet in bulletsToReturn)
        {
            BulletPoolUtilities.DeactivateAndReturn(
                bullet,
                ref poolSystem,
                ref _bulletDataLookup,
                ref _localTransformLookup,
                ref _physicsVelocityLookup);
        }

        if (hasExplosionPool && terrainCollisionPositions.Length > 0)
        {
            ref var explosionPoolSystem =
                ref state.WorldUnmanaged.GetUnsafeSystemRef<DirtExplosionPoolSystem>(explosionPoolSystemHandle);

            var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
            float prefabScale = 1f;
            if (SystemAPI.HasComponent<LocalTransform>(prefabs.dirtExplosionSmallPrefab))
                prefabScale = SystemAPI.GetComponent<LocalTransform>(prefabs.dirtExplosionSmallPrefab).Scale;

            float3 scroll = SystemAPI.TryGetSingleton<ScrollOffset>(out var scrollOffsetSingleton)
                ? scrollOffsetSingleton.accumulatedOffset
                : float3.zero;

            double spawnTime = SystemAPI.Time.ElapsedTime;

            for (int i = 0; i < terrainCollisionPositions.Length; i++)
            {
                Entity explosion = explosionPoolSystem.GetFromPool(ref state);
                if (explosion == Entity.Null)
                    continue;

                float3 impactPos = terrainCollisionPositions[i];

                SystemAPI.SetComponent(explosion, new LocalTransform
                {
                    Position = impactPos,
                    Rotation = quaternion.identity,
                    Scale = prefabScale
                });

                SystemAPI.SetComponent(explosion, new DirtExplosionData
                {
                    spawnTime = spawnTime,
                    active = true,
                    triggered = false
                });

                SystemAPI.SetComponent(explosion, new TerrainAnchorTag
                {
                    basePosition = impactPos + scroll
                });
            }
        }

        bulletsToReturn.Dispose();
        terrainCollisionPositions.Dispose();
    }

    /// <summary>
    /// Checks whether <paramref name="bullet"/> is an active bullet not already queued for return,
    /// then adds it to <paramref name="bulletsToReturn"/> and, when the collision was with terrain,
    /// records the impact position in <paramref name="terrainCollisionPositions"/> for VFX spawning.
    /// </summary>
    private void TryCollectBullet(
        Entity bullet,
        bool hitTerrain,
        NativeHashSet<Entity> bulletsToReturn,
        NativeList<float3> terrainCollisionPositions)
    {
        if (!_bulletDataLookup.HasComponent(bullet))
            return;

        var bulletData = _bulletDataLookup[bullet];
        if (!bulletData.active || bulletsToReturn.Contains(bullet))
            return;

        bulletsToReturn.Add(bullet);

        if (hitTerrain && _localTransformLookup.HasComponent(bullet))
            terrainCollisionPositions.Add(_localTransformLookup[bullet].Position);
    }
}
