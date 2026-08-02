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
///
/// Before the first shot of each burst, a physics ray from the muzzle toward the intercept point
/// checks for terrain colliders along the bullet path; the result is cached for the rest of the burst.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct TurretShooterSystem : ISystem
{
    private const float RayOriginOffset = 0.1f;

    private ComponentLookup<TurretDome> _domeLookup;

    private struct PendingShot
    {
        public float3     spawnPos;
        public quaternion spawnRot;
        public float3     bulletVelocity;
    }

    /// <summary>Registers required singletons and caches the <see cref="TurretDome"/> component lookup.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TurretShooterState>();
        state.RequireForUpdate<BulletPoolConfig>();
        state.RequireForUpdate<PrefabEntitiesReferences>();
        _domeLookup = state.GetComponentLookup<TurretDome>(isReadOnly: true);
    }

    /// <summary>
    /// Processes each turret's burst-fire cooldown, performs a line-of-sight raycast against
    /// terrain physics colliders (when available), and fires bullets from the turret launch offset
    /// toward the pre-computed ballistic intercept point. Spawns bullets from the pool.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var gamePaused) && gamePaused.Value)
            return;

        _domeLookup.Update(ref state);

        bool canCheckTerrain = SystemAPI.TryGetSingleton<TerrainTileConfig>(out var terrainConfig)
            && terrainConfig.enablePhysicsColliders
            && SystemAPI.HasSingleton<PhysicsWorldSingleton>();

        bool physicsWorldReady = false;
        CollisionWorld collisionWorld = default;
        CollisionFilter terrainRayFilter = default;

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
            terrainVelocity = sv.WorldVelocity;
        }

        double currentTime = SystemAPI.Time.ElapsedTime;
        if (SystemAPI.TryGetSingleton(out gamePaused))
            currentTime = GamePausedUtility.GetGameplayElapsedTime(currentTime, gamePaused);

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
                shooter.burstLineOfSightEvaluated = false;
                shooter.burstTerrainBlocked = false;
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
            if (interceptDist > shooter.maxFireDistance)
            {
                continue;
            }

            float3 desiredDir = toIntercept / interceptDist;
            float3 muzzleForward = math.rotate(spawnRot, math.forward());
            float cosAngle = math.dot(math.normalizesafe(muzzleForward), desiredDir);
            if (math.acos(math.clamp(cosAngle, -1f, 1f)) > shooter.maxFireAngleRadians)
                continue;

            if (canCheckTerrain)
            {
                if (!shooter.burstLineOfSightEvaluated)
                {
                    EnsurePhysicsWorldReady(
                        ref state,
                        terrainConfig,
                        ref physicsWorldReady,
                        ref collisionWorld,
                        ref terrainRayFilter);
                    shooter.burstTerrainBlocked = TerrainBlocksShot(
                        collisionWorld, terrainRayFilter, spawnPos, desiredDir, interceptDist);
                    shooter.burstLineOfSightEvaluated = true;
                }

                if (shooter.burstTerrainBlocked)
                {
                    shooter.inCooldown = true;
                    shooter.cooldownEndsAt = currentTime + shooter.cooldownDuration;
                    shooter.burstLineOfSightEvaluated = false;
                    shooter.burstTerrainBlocked = false;
                    continue;
                }
            }

            float3 bulletVelocity = desiredDir * dome.bulletSpeed - terrainVelocity;

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

    /// <summary>
    /// Lazily initialises the <see cref="CollisionWorld"/> reference and terrain-layer
    /// <see cref="CollisionFilter"/> on first use within a frame by completing the dependency
    /// and reading from <see cref="PhysicsWorldSingleton"/>. Does nothing if already ready.
    /// </summary>
    private void EnsurePhysicsWorldReady(
        ref SystemState state,
        in TerrainTileConfig terrainConfig,
        ref bool physicsWorldReady,
        ref CollisionWorld collisionWorld,
        ref CollisionFilter terrainRayFilter)
    {
        if (physicsWorldReady)
            return;

        state.Dependency.Complete();
        collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld.CollisionWorld;
        uint terrainLayerMask = 1u << terrainConfig.terrainPhysicsLayer;
        terrainRayFilter = new CollisionFilter
        {
            BelongsTo = ~0u,
            CollidesWith = terrainLayerMask,
            GroupIndex = 0
        };
        physicsWorldReady = true;
    }

    /// <summary>
    /// Returns true when a terrain tile collider lies on the segment from muzzle to intercept.
    /// </summary>
    private static bool TerrainBlocksShot(
        CollisionWorld collisionWorld,
        CollisionFilter terrainFilter,
        float3 spawnPos,
        float3 direction,
        float interceptDistance)
    {
        float rayLength = math.max(interceptDistance - RayOriginOffset, 0.01f);
        float3 rayStart = spawnPos + direction * RayOriginOffset;
        float3 rayEnd = spawnPos + direction * (RayOriginOffset + rayLength);

        var rayInput = new RaycastInput
        {
            Start = rayStart,
            End = rayEnd,
            Filter = terrainFilter
        };

        return collisionWorld.CastRay(rayInput);
    }
}
