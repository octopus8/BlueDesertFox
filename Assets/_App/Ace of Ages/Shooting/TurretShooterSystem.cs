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
/// Follows the zero-GC two-phase pattern required by Unity ECS:
///   Phase 1 (inside foreach): evaluate timing/state, collect pending shots, update shooter
///            state via RefRW — no structural changes.
///   Phase 2 (after foreach): call BulletPoolSystem.GetFromPool and configure each bullet —
///            structural changes (pool growth via Instantiate/AddComponentData) are safe here.
///
/// Bullet velocity = (direction to intercept * bulletSpeed) + terrain scroll velocity,
/// matching BulletShooterSystem so bullets move in the terrain reference frame.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct TurretShooterSystem : ISystem
{
    private ComponentLookup<TurretDome> _domeLookup;

    // Blittable struct carrying everything needed to spawn one bullet, collected in Phase 1.
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

    // Not [BurstCompile] — accesses BulletPoolSystem (managed system ref) and Debug.Log.
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

        // Diagnostic: count matching barrel entities once per ~120 frames.
        bool logThisFrame = ((int)(currentTime * 10) % 120) == 0;

        // Phase 1: evaluate burst/cooldown state and collect shots to fire.
        // All writes here go through RefRW — no structural changes, safe inside the iterator.
        var pendingShots = new NativeList<PendingShot>(16, Allocator.Temp);
        int barrelCount = 0;

        foreach (var (shooterRef, barrelTag, barrelTransform) in
            SystemAPI.Query<RefRW<TurretShooterState>, RefRO<TurretBarrelTag>, RefRO<LocalTransform>>())
        {
            barrelCount++;
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
            {
                if (logThisFrame)
                    Debug.LogWarning($"[TurretShooterSystem] Barrel entity dome lookup failed (domeEntity={barrelTag.ValueRO.domeEntity})");
                continue;
            }

            var dome = _domeLookup[barrelTag.ValueRO.domeEntity];

            // Compute muzzle world position and rotation
            var barrelTf = barrelTransform.ValueRO;
            float3 spawnPos = barrelTf.Position + math.rotate(barrelTf.Rotation, shooter.spawnLocalOffset);
            quaternion spawnRot = math.mul(barrelTf.Rotation, shooter.spawnLocalRotation);

            // Aim at the pre-solved intercept point
            float3 toIntercept = dome.interceptPoint - spawnPos;
            float interceptDist = math.length(toIntercept);
            if (interceptDist < 0.01f)
                continue;

            float3 bulletVelocity = (toIntercept / interceptDist) * dome.bulletSpeed + terrainVelocity;

            pendingShots.Add(new PendingShot
            {
                spawnPos      = spawnPos,
                spawnRot      = spawnRot,
                bulletVelocity = bulletVelocity
            });

            // Advance burst state (RefRW write — no structural change)
            shooter.bulletsRemainingInBurst--;
            shooter.lastShotTime = currentTime;
        }

        if (logThisFrame)
            Debug.Log($"[TurretShooterSystem] barrels={barrelCount} pendingShots={pendingShots.Length} t={currentTime:F1}");

        // Phase 2: spawn bullets outside the query iterator.
        // GetFromPool may call Instantiate/AddComponentData (structural changes) — safe here.
        for (int i = 0; i < pendingShots.Length; i++)
        {
            var shot = pendingShots[i];

            Entity bulletEntity = poolSystem.GetFromPool(ref state);
            if (bulletEntity == Entity.Null)
            {
                Debug.LogWarning("[TurretShooterSystem] Bullet pool exhausted");
                continue;
            }

            state.EntityManager.SetComponentData(bulletEntity, new LocalTransform
            {
                Position = shot.spawnPos,
                Rotation = shot.spawnRot,
                Scale    = prefabScale
            });

            state.EntityManager.SetComponentData(bulletEntity, new PhysicsVelocity
            {
                Linear  = shot.bulletVelocity,
                Angular = float3.zero
            });

            state.EntityManager.SetComponentData(bulletEntity, new BulletData
            {
                spawnPosition = shot.spawnPos,
                creationTime  = currentTime,
                active        = true
            });
        }

        pendingShots.Dispose();
    }
}
