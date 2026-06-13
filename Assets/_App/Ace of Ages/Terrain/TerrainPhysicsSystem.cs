using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

namespace _App.Ace_of_Ages.Terrain
{
    /// <summary>
    /// Creates physics colliders for terrain tiles with frame budgeting and camera-aware priority.
    /// MeshCollider.Create and PhysicsCollider registration are split across frames to reduce peak cost.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial class TerrainPhysicsSystem : SystemBase
    {
#if UNITY_EDITOR
        private static readonly ProfilerMarker s_RegisterMarker = new ProfilerMarker("TerrainPhysics.RegisterCollider");
        private static readonly ProfilerMarker s_EnqueueMarker = new ProfilerMarker("TerrainPhysics.Enqueue");
        private static readonly ProfilerMarker s_PrioritySortMarker = new ProfilerMarker("TerrainPhysics.PrioritySort");
        private static readonly ProfilerMarker s_ColliderCreationMarker = new ProfilerMarker("TerrainPhysics.ColliderCreation");
#endif

        private NativeQueue<Entity> _pendingColliders;
        private NativeHashSet<Entity> _queuedEntities;

        /// <summary>
        /// Allocates the collider pending queue and deduplication set, and registers the
        /// <see cref="TerrainTileConfig"/> requirement.
        /// </summary>
        protected override void OnCreate()
        {
            RequireForUpdate<TerrainTileConfig>();

            _pendingColliders = new NativeQueue<Entity>(Allocator.Persistent);
            _queuedEntities = new NativeHashSet<Entity>(64, Allocator.Persistent);
        }

        /// <summary>Disposes the native pending-collider queue and deduplication set.</summary>
        protected override void OnDestroy()
        {
            if (_pendingColliders.IsCreated)
            {
                _pendingColliders.Dispose();
            }

            if (_queuedEntities.IsCreated)
            {
                _queuedEntities.Dispose();
            }
        }

        /// <summary>
        /// Completes any outstanding collider preparation jobs, then processes up to
        /// <c>maxCollidersCreatedPerFrame</c> prepared tiles per frame — sorted by camera-aware
        /// priority — calling <c>MeshCollider.Create()</c> on the main thread for each and
        /// registering the result via <see cref="EntityCommandBuffer"/>.
        /// Skips processing if physics colliders are disabled in <see cref="TerrainTileConfig"/>.
        /// </summary>
        protected override void OnUpdate()
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.enablePhysicsColliders)
            {
                return;
            }

            CompletePreparationJobs();

            int remainingBudget = TerrainPhysicsBudget.GetCreationBudget(config);
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var collisionFilter = CreateCollisionFilter(config);

#if UNITY_EDITOR
            using (s_RegisterMarker.Auto())
#endif
            {
                remainingBudget = RegisterPendingColliders(remainingBudget, ecb);
            }

            if (remainingBudget > 0)
            {
                EnqueuePreparedTiles();

                if (_pendingColliders.Count > 0)
                {
                    remainingBudget = CreateCollidersFromQueue(remainingBudget, collisionFilter, ecb);
                }
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Queries tiles with prepared collider data (<see cref="PhysicsColliderPrepared"/>) that are
        /// not yet registered, marks up to <paramref name="budget"/> of them as pending registration
        /// via ECB, and returns the count actually enqueued.
        /// </summary>
        private int RegisterPendingColliders(int budget, EntityCommandBuffer ecb)
        {
            if (budget <= 0)
            {
                return 0;
            }

            var pendingEntities = new NativeList<Entity>(budget, Allocator.Temp);

            foreach (var (pending, entity) in SystemAPI.Query<RefRO<PhysicsColliderRegistrationPending>>().WithEntityAccess())
            {
                pendingEntities.Add(entity);
                if (pendingEntities.Length >= budget)
                {
                    break;
                }
            }

            for (int i = 0; i < pendingEntities.Length; i++)
            {
                Entity entity = pendingEntities[i];
                if (!EntityManager.Exists(entity))
                {
                    continue;
                }

                var pending = EntityManager.GetComponentData<PhysicsColliderRegistrationPending>(entity);
                ecb.AddComponent(entity, new PhysicsCollider { Value = pending.collider });

                if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                {
                    ecb.AddSharedComponent(entity, new PhysicsWorldIndex());
                }

                ecb.AddComponent<PhysicsColliderValid>(entity);
                ecb.RemoveComponent<PhysicsColliderRegistrationPending>(entity);
                budget--;
            }

            pendingEntities.Dispose();
            return budget;
        }

        /// <summary>Moves all entities with <see cref="PhysicsColliderRegistrationPending"/> into the <c>_pendingColliders</c> queue for processing this frame.</summary>
        private void EnqueuePreparedTiles()
        {
#if UNITY_EDITOR
            using (s_EnqueueMarker.Auto())
#endif
            {
                foreach (var (_, entity) in SystemAPI.Query<RefRO<PhysicsColliderPrepared>>()
                    .WithAll<ColliderPreparedVertexElement, ColliderPreparedTriangleElement>()
                    .WithNone<PhysicsColliderRegistrationPending>()
                    .WithEntityAccess())
                {
                    if (_queuedEntities.Add(entity))
                    {
                        _pendingColliders.Enqueue(entity);
                    }
                }
            }
        }

        /// <summary>
        /// Sorts queued tiles by camera-aware priority, then creates up to <paramref name="budget"/>
        /// <see cref="Unity.Physics.MeshCollider"/> instances from prepared vertex/triangle buffers,
        /// attaches them to tile entities via ECB, and updates the LRU collider cache.
        /// Returns the number of colliders created.
        /// </summary>
        private int CreateCollidersFromQueue(int budget, CollisionFilter collisionFilter, EntityCommandBuffer ecb)
        {
            var pendingTiles = new NativeList<ColliderEntityWithPriority>(_pendingColliders.Count, Allocator.Temp);

            while (_pendingColliders.Count > 0)
            {
                Entity entity = _pendingColliders.Dequeue();
                _queuedEntities.Remove(entity);

                if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<PhysicsColliderPrepared>(entity))
                {
                    continue;
                }

                var prepared = EntityManager.GetComponentData<PhysicsColliderPrepared>(entity);
                pendingTiles.Add(new ColliderEntityWithPriority
                {
                    entity = entity,
                    priority = prepared.priority
                });
            }

            if (pendingTiles.Length == 0)
            {
                pendingTiles.Dispose();
                return budget;
            }

#if UNITY_EDITOR
            using (s_PrioritySortMarker.Auto())
#endif
            {
                if (pendingTiles.Length > 1)
                {
                    pendingTiles.Sort(new ColliderPriorityComparer());
                }
            }

            int tilesToProcess = math.min(pendingTiles.Length, budget);

#if UNITY_EDITOR
            using (s_ColliderCreationMarker.Auto())
#endif
            {
                for (int i = 0; i < tilesToProcess; i++)
                {
                    var entity = pendingTiles[i].entity;

                    if (!EntityManager.Exists(entity))
                    {
                        continue;
                    }

                    var vertexBuffer = EntityManager.GetBuffer<ColliderPreparedVertexElement>(entity);
                    var triangleBuffer = EntityManager.GetBuffer<ColliderPreparedTriangleElement>(entity);

                    if (vertexBuffer.Length == 0 || triangleBuffer.Length == 0)
                    {
                        Debug.LogWarning($"[TerrainPhysics] Entity {entity.Index} has empty prepared buffers, skipping");
                        ecb.RemoveComponent<PhysicsColliderPrepared>(entity);
                        continue;
                    }

                    var verticesNative = vertexBuffer.Reinterpret<float3>().AsNativeArray();
                    var trianglesNative = triangleBuffer.Reinterpret<int3>().AsNativeArray();

                    try
                    {
#if UNITY_EDITOR
                        UnityEngine.Profiling.Profiler.BeginSample("TerrainPhysics.MeshColliderCreate");
#endif
                        var collider = Unity.Physics.MeshCollider.Create(
                            verticesNative,
                            trianglesNative,
                            collisionFilter,
                            Unity.Physics.Material.Default
                        );
#if UNITY_EDITOR
                        UnityEngine.Profiling.Profiler.EndSample();
#endif

                        ecb.AddComponent(entity, new PhysicsColliderRegistrationPending { collider = collider });
                        ecb.RemoveComponent<PhysicsColliderPrepared>(entity);
                        // Keep ColliderPreparedVertexElement/ColliderPreparedTriangleElement so the
                        // TerrainColliderVisualizer can draw the actual baked collider geometry.
                        // These are removed by TerrainDistanceTrackingSystem.RemoveColliderState when
                        // the collider itself is removed, keeping the overlay in lockstep with physics.
                        budget--;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[TerrainPhysics] Failed to create collider for entity {entity.Index}: {e.Message}");
                    }
                }
            }

            for (int i = tilesToProcess; i < pendingTiles.Length; i++)
            {
                Entity entity = pendingTiles[i].entity;
                if (EntityManager.Exists(entity) && EntityManager.HasComponent<PhysicsColliderPrepared>(entity))
                {
                    if (_queuedEntities.Add(entity))
                    {
                        _pendingColliders.Enqueue(entity);
                    }
                }
            }

            pendingTiles.Dispose();
            return budget;
        }

        /// <summary>Completes the current <see cref="TerrainColliderPreparationSystem"/> dependency to ensure prepared vertex/triangle data is ready before collider creation this frame.</summary>
        private void CompletePreparationJobs()
        {
            var prepSystem = World.Unmanaged.GetExistingUnmanagedSystem<TerrainColliderPreparationSystem>();
            if (prepSystem != SystemHandle.Null)
            {
                ref SystemState prepState = ref World.Unmanaged.ResolveSystemStateRef(prepSystem);
                prepState.Dependency.Complete();
            }
        }

        /// <summary>Creates a <see cref="CollisionFilter"/> that belongs to all layers and collides with the terrain physics layer specified in <paramref name="config"/>.</summary>
        private static CollisionFilter CreateCollisionFilter(TerrainTileConfig config)
        {
            uint layerMask = 1u << config.terrainPhysicsLayer;

            return new CollisionFilter
            {
                BelongsTo = layerMask,
                CollidesWith = ~0u,
                GroupIndex = 0
            };
        }
    }

    struct ColliderEntityWithPriority
    {
        public Entity entity;
        public int priority;
    }

    /// <summary>Sorts <see cref="ColliderEntityWithPriority"/> values in ascending order so higher-priority (closer/in-view) tiles are processed first.</summary>
    struct ColliderPriorityComparer : IComparer<ColliderEntityWithPriority>
    {
        /// <inheritdoc/>
        public int Compare(ColliderEntityWithPriority a, ColliderEntityWithPriority b)
        {
            return a.priority.CompareTo(b.priority);
        }
    }
}
