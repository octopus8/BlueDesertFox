using System.Collections.Generic;
using _App.Ace_of_Ages.Terrain;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that calculates distance from each terrain tile to the player and manages collider lifecycle.
/// Runs before TerrainPhysicsSystem to ensure distance data is up-to-date.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TerrainPhysicsSystem))]
public partial class TerrainDistanceTrackingSystem : SystemBase
{
#if UNITY_EDITOR
    static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.DistanceTracking");
#endif

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileConfig>();
        RequireForUpdate<CameraDataSingleton>();
    }

    protected override void OnUpdate()
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.enablePhysicsColliders)
                return;

            float3 playerPosition = SystemAPI.GetSingleton<CameraDataSingleton>().position;
            int prepBudget = TerrainPhysicsBudget.GetPrepMarkBudget(config);
            var world = World.Unmanaged;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var prepCandidates = new NativeList<PrepCandidate>(32, Allocator.Temp);

            foreach (var (transform, tile, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<TerrainTile>>().WithEntityAccess())
            {
                float3 tileCenter = transform.ValueRO.Position;

                float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
                float2 tileCenter2D = new float2(tileCenter.x, tileCenter.z);
                float distance = math.distance(tileCenter2D, playerPos2D);

                bool needsCollider = distance < config.maxColliderDistance;

                var distanceData = new TerrainTileDistanceToPlayer { distance = distance };

                if (SystemAPI.HasComponent<TerrainTileDistanceToPlayer>(entity))
                    ecb.SetComponent(entity, distanceData);
                else
                    ecb.AddComponent(entity, distanceData);

                if (!tile.ValueRO.meshGenerated)
                    continue;

                if (!needsCollider)
                {
                    RetireColliderState(entity, tile.ValueRO.gridCoordinate, ecb, EntityManager, world, config);
                    continue;
                }

                if (SystemAPI.HasComponent<PhysicsColliderValid>(entity))
                    continue;

                if (SystemAPI.HasComponent<PhysicsColliderRegistrationPending>(entity))
                    continue;

                if (SystemAPI.HasComponent<PhysicsColliderNeedsPreparation>(entity) &&
                    SystemAPI.IsComponentEnabled<PhysicsColliderNeedsPreparation>(entity))
                    continue;

                prepCandidates.Add(new PrepCandidate
                {
                    entity = entity,
                    gridCoordinate = tile.ValueRO.gridCoordinate,
                    distance = distance
                });
            }

            if (prepCandidates.Length > 1)
                prepCandidates.Sort(new PrepCandidateComparer());

            int prepMarksThisFrame = math.min(prepCandidates.Length, prepBudget);
            for (int i = 0; i < prepMarksThisFrame; i++)
            {
                var candidate = prepCandidates[i];
                Entity entity = candidate.entity;

                if (TerrainColliderBlobCacheSystem.TryTakeCachedCollider(
                        world, candidate.gridCoordinate, out var cachedCollider))
                {
                    ecb.AddComponent(entity, new PhysicsColliderRegistrationPending { collider = cachedCollider });
                    continue;
                }

                if (SystemAPI.HasComponent<PhysicsColliderNeedsPreparation>(entity))
                    ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
                else
                {
                    ecb.AddComponent<PhysicsColliderNeedsPreparation>(entity);
                    ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
                }
            }

            prepCandidates.Dispose();
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }

    static void RetireColliderState(
        Entity entity,
        int2 gridCoordinate,
        EntityCommandBuffer ecb,
        EntityManager entityManager,
        WorldUnmanaged world,
        in TerrainTileConfig config)
    {
        int vCount = config.verticesPerSide * config.verticesPerSide;
        int tCount = (config.verticesPerSide - 1) * (config.verticesPerSide - 1) * 2;
        int estimatedBytes = TerrainColliderBlobCacheSystem.EstimateColliderMemoryBytes(vCount, tCount);

        if (entityManager.HasComponent<Unity.Physics.PhysicsCollider>(entity))
        {
            var pc = entityManager.GetComponentData<Unity.Physics.PhysicsCollider>(entity);
            if (pc.Value.IsCreated)
                TerrainColliderBlobCacheSystem.RetireToCache(world, gridCoordinate, pc.Value, estimatedBytes);
            ecb.RemoveComponent<Unity.Physics.PhysicsCollider>(entity);
        }

        if (entityManager.HasComponent<PhysicsColliderValid>(entity))
            ecb.RemoveComponent<PhysicsColliderValid>(entity);

        if (entityManager.HasComponent<PhysicsWorldIndex>(entity))
            ecb.RemoveComponent<PhysicsWorldIndex>(entity);

        if (entityManager.HasComponent<PhysicsColliderRegistrationPending>(entity))
        {
            var pending = entityManager.GetComponentData<PhysicsColliderRegistrationPending>(entity);
            if (pending.collider.IsCreated)
                TerrainColliderBlobCacheSystem.RetireToCache(world, gridCoordinate, pending.collider, estimatedBytes);
            ecb.RemoveComponent<PhysicsColliderRegistrationPending>(entity);
        }

        if (entityManager.HasComponent<PhysicsColliderNeedsPreparation>(entity))
            ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, false);
    }

    struct PrepCandidate
    {
        public Entity entity;
        public int2 gridCoordinate;
        public float distance;
    }

    struct PrepCandidateComparer : IComparer<PrepCandidate>
    {
        public int Compare(PrepCandidate a, PrepCandidate b)
            => a.distance.CompareTo(b.distance);
    }
}
