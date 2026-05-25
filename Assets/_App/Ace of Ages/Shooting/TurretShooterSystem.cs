using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Drives burst-fire behaviour for all turret barrel entities that have a TurretShooterState component.
///
/// Execution order:
///   StaticObjectPositionUpdateSystem (sets tile-relative positions)
///     TurretAimingSystem   [TransformSystemGroup] — writes TurretDome.interceptPoint
///       TurretBarrelSystem [TransformSystemGroup] — sets barrel world position/rotation
///         TurretShooterSystem [SimulationSystemGroup, UpdateAfter(TransformSystemGroup)]
///
/// Phase 1 (inside foreach): evaluate burst/cooldown state, collect pending shots, update
///   TurretShooterState via RefRW — no structural changes.
/// Phase 2 (after foreach): call BulletPoolSystem.GetFromPool and configure each bullet.
///   Also writes LocalToWorld directly so the renderer sees the correct position on the
///   same frame the bullet is spawned (LocalToWorldSystem has already run this frame).
///
/// Bullet velocity = (direction from muzzle to intercept * bulletSpeed) - terrainScrollVelocity.
/// Intercept is solved from the muzzle in TurretAimingSystem when TurretLaunchOffset is baked on the dome.
/// Tiles move at -terrainScrollVelocity in world space, so subtracting gives the correct
/// terrain-frame velocity matching BulletShooterSystem.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct TurretShooterSystem : ISystem
{
    private ComponentLookup<TurretDome> _domeLookup;

    private struct PendingShot
    {
        public float3     spawnPos;
        public quaternion spawnRot;
        public float3     bulletVelocity;
    }

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TurretShooterState>();
        state.RequireForUpdate<BulletPoolConfig>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        _domeLookup = state.GetComponentLookup<TurretDome>(isReadOnly: true);
    }

    public void OnUpdate(ref SystemState state)
    {
        _domeLookup.Update(ref state);

        var poolSystemHandle = state.World.GetExistingSystem<BulletPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
        {
            Debug.LogWarning("[TurretShooterSystem] BulletPoolSystem not found");
            return;
        }
        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<BulletPoolSystem>(poolSystemHandle);

        var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
        float prefabScale = 1f;
        if (SystemAPI.HasComponent<LocalTransform>(prefabs.bulletSimplePrefab))
            prefabScale = SystemAPI.GetComponent<LocalTransform>(prefabs.bulletSimplePrefab).Scale;

        float3 terrainVelocity = float3.zero;
        if (SystemAPI.HasSingleton<TerrainScrollVelocity>())
        {
            var sv = SystemAPI.GetSingleton<TerrainScrollVelocity>();
            terrainVelocity = sv.direction * sv.speed;
        }

        double currentTime = SystemAPI.Time.ElapsedTime;

        // Phase 1: evaluate burst/cooldown state and collect shots to fire.
        // All writes go through RefRW — no structural changes, safe inside the iterator.
        var pendingShots = new NativeList<PendingShot>(16, Allocator.Temp);

        foreach (var (shooterRef, barrelTag, barrelTransform) in
            SystemAPI.Query<RefRW<TurretShooterState>, RefRO<TurretBarrelTag>, RefRO<LocalTransform>>())
        {
            ref var shooter = ref shooterRef.ValueRW;

            // Cooldown expiry
            if (shooter.inCooldown)
            {
                if (currentTime >= shooter.cooldownEndsAt)
                {
                    shooter.inCooldown = false;
                    shooter.bulletsRemainingInBurst = shooter.bulletsPerBurst;
                }
                else
                {
                    continue;
                }
            }

            // Burst exhausted → enter cooldown
            if (shooter.bulletsRemainingInBurst <= 0)
            {
                shooter.inCooldown = true;
                shooter.cooldownEndsAt = currentTime + shooter.cooldownDuration;
                continue;
            }

            // Intra-burst fire rate gate
            if (currentTime < shooter.lastShotTime + shooter.burstIntraDelay)
                continue;

            // Dome must be available for intercept data
            if (!_domeLookup.HasComponent(barrelTag.ValueRO.domeEntity))
                continue;

            var dome = _domeLookup[barrelTag.ValueRO.domeEntity];

            // Compute muzzle world position and rotation from barrel transform + baked local offset
            var barrelTf = barrelTransform.ValueRO;
            float3 spawnPos = barrelTf.Position + math.rotate(barrelTf.Rotation, shooter.spawnLocalOffset);
            quaternion spawnRot = math.mul(barrelTf.Rotation, shooter.spawnLocalRotation);

            // Aim at the pre-solved intercept point
            float3 toIntercept = dome.interceptPoint - spawnPos;
            float interceptDist = math.length(toIntercept);
            if (interceptDist < 0.01f)
                continue;

            float3 bulletVelocity = (toIntercept / interceptDist) * dome.bulletSpeed - terrainVelocity;

            pendingShots.Add(new PendingShot
            {
                spawnPos       = spawnPos,
                spawnRot       = spawnRot,
                bulletVelocity = bulletVelocity
            });

            shooter.bulletsRemainingInBurst--;
            shooter.lastShotTime = currentTime;
        }

        // Phase 2: spawn bullets outside the query iterator.
        // GetFromPool may call Instantiate/AddComponentData (structural changes) — safe here.
        for (int i = 0; i < pendingShots.Length; i++)
        {
            var shot = pendingShots[i];

            Entity bulletEntity = poolSystem.GetFromPool(ref state);
            if (bulletEntity == Entity.Null)
                continue;

            var lt = new LocalTransform
            {
                Position = shot.spawnPos,
                Rotation = shot.spawnRot,
                Scale    = prefabScale
            };

            state.EntityManager.SetComponentData(bulletEntity, lt);

            // Write LocalToWorld directly so the renderer shows the bullet at the correct
            // position on this frame. LocalToWorldSystem already ran inside TransformSystemGroup
            // (before this system), so without this the bullet would render at (0,-10000,0)
            // until the next frame.
            bool hasL2W = state.EntityManager.HasComponent<LocalToWorld>(bulletEntity);
            if (hasL2W)
            {
                state.EntityManager.SetComponentData(bulletEntity, new LocalToWorld
                {
                    Value = float4x4.TRS(shot.spawnPos, shot.spawnRot, new float3(prefabScale))
                });
            }

            state.EntityManager.SetComponentData(bulletEntity, new PhysicsVelocity
            {
                Linear  = shot.bulletVelocity,
                Angular = float3.zero
            });

            state.EntityManager.SetComponentData(bulletEntity, new BulletData
            {
                spawnPosition = shot.spawnPos,
                creationTime  = currentTime,
                active        = true,
                linearVelocityTerrainRelative = shot.bulletVelocity + terrainVelocity
            });

        }

        pendingShots.Dispose();
    }
}
