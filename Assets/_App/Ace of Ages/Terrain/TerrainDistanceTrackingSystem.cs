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
/// System that calculates distance from each terrain tile to the player and determines appropriate LOD level.
/// Runs before TerrainPhysicsSystem to ensure distance data is up-to-date.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TerrainPhysicsSystem))]
public partial class TerrainDistanceTrackingSystem : SystemBase
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.DistanceTracking");
#endif

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileConfig>();
    }

    protected override void OnUpdate()
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.enablePhysicsColliders)
            {
                return;
            }

            if (!SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) ||
                playerRef == null ||
                playerRef.playerTransform == null)
            {
                return;
            }

            float3 playerPosition = playerRef.playerTransform.position;
            int prepBudget = TerrainPhysicsBudget.GetCreationBudget(config);

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var prepCandidates = new NativeList<PrepCandidate>(32, Allocator.Temp);

            foreach (var (transform, tile, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<TerrainTile>>().WithEntityAccess())
            {
                float3 tileCenter = transform.ValueRO.Position;

                float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
                float2 tileCenter2D = new float2(tileCenter.x, tileCenter.z);
                float distance = math.distance(tileCenter2D, playerPos2D);

                bool needsCollider = distance < config.maxColliderDistance;

                var distanceData = new TerrainTileDistanceToPlayer
                {
                    distance = distance
                };

                if (SystemAPI.HasComponent<TerrainTileDistanceToPlayer>(entity))
                {
                    ecb.SetComponent(entity, distanceData);
                }
                else
                {
                    ecb.AddComponent(entity, distanceData);
                }

                if (!tile.ValueRO.meshGenerated)
                {
                    continue;
                }

                if (!needsCollider)
                {
                    RemoveColliderState(entity, ecb, EntityManager);
                    continue;
                }

                if (SystemAPI.HasComponent<PhysicsColliderValid>(entity))
                {
                    continue;
                }

                if (SystemAPI.HasComponent<PhysicsColliderRegistrationPending>(entity))
                {
                    continue;
                }

                if (SystemAPI.HasComponent<PhysicsColliderPrepared>(entity))
                {
                    continue;
                }

                if (SystemAPI.HasComponent<PhysicsColliderNeedsPreparation>(entity) &&
                    SystemAPI.IsComponentEnabled<PhysicsColliderNeedsPreparation>(entity))
                {
                    continue;
                }

                prepCandidates.Add(new PrepCandidate
                {
                    entity = entity,
                    distance = distance
                });
            }

            if (prepCandidates.Length > 1)
            {
                prepCandidates.Sort(new PrepCandidateComparer());
            }

            int prepMarksThisFrame = math.min(prepCandidates.Length, prepBudget);
            for (int i = 0; i < prepMarksThisFrame; i++)
            {
                Entity entity = prepCandidates[i].entity;

                if (SystemAPI.HasComponent<PhysicsColliderNeedsPreparation>(entity))
                {
                    ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
                }
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

    private static void RemoveColliderState(Entity entity, EntityCommandBuffer ecb, EntityManager entityManager)
    {
        if (entityManager.HasComponent<Unity.Physics.PhysicsCollider>(entity))
        {
            ecb.RemoveComponent<Unity.Physics.PhysicsCollider>(entity);
        }

        if (entityManager.HasComponent<PhysicsColliderValid>(entity))
        {
            ecb.RemoveComponent<PhysicsColliderValid>(entity);
        }

        if (entityManager.HasComponent<PhysicsWorldIndex>(entity))
        {
            ecb.RemoveComponent<PhysicsWorldIndex>(entity);
        }

        if (entityManager.HasComponent<PhysicsColliderPrepared>(entity))
        {
            ecb.RemoveComponent<PhysicsColliderPrepared>(entity);
        }

        if (entityManager.HasComponent<PhysicsColliderRegistrationPending>(entity))
        {
            var pending = entityManager.GetComponentData<PhysicsColliderRegistrationPending>(entity);
            if (pending.collider.IsCreated)
            {
                pending.collider.Dispose();
            }

            ecb.RemoveComponent<PhysicsColliderRegistrationPending>(entity);
        }

        if (entityManager.HasComponent<ColliderPreparedVertexElement>(entity))
        {
            ecb.RemoveComponent<ColliderPreparedVertexElement>(entity);
        }

        if (entityManager.HasComponent<ColliderPreparedTriangleElement>(entity))
        {
            ecb.RemoveComponent<ColliderPreparedTriangleElement>(entity);
        }

        if (entityManager.HasComponent<PhysicsColliderNeedsPreparation>(entity))
        {
            ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, false);
        }
    }

    struct PrepCandidate
    {
        public Entity entity;
        public float distance;
    }

    struct PrepCandidateComparer : IComparer<PrepCandidate>
    {
        public int Compare(PrepCandidate a, PrepCandidate b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
