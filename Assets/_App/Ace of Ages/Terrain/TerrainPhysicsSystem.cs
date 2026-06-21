using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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

        private BufferLookup<ColliderPreparedVertexElement> _vertexLookup;
        private BufferLookup<ColliderPreparedTriangleElement> _triangleLookup;

        /// <summary>
        /// Allocates the collider pending queue and deduplication set, caches buffer lookups for
        /// the parallel collider-creation job, and registers the <see cref="TerrainTileConfig"/> requirement.
        /// </summary>
        protected override void OnCreate()
        {
            RequireForUpdate<TerrainTileConfig>();

            _pendingColliders = new NativeQueue<Entity>(Allocator.Persistent);
            _queuedEntities = new NativeHashSet<Entity>(64, Allocator.Persistent);

            _vertexLookup = GetBufferLookup<ColliderPreparedVertexElement>(isReadOnly: true);
            _triangleLookup = GetBufferLookup<ColliderPreparedTriangleElement>(isReadOnly: true);
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
        /// priority — scheduling <c>MeshCollider.Create()</c> as a parallel Burst job and
        /// registering the results via <see cref="EntityCommandBuffer"/>.
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

            _vertexLookup.Update(this);
            _triangleLookup.Update(this);

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
        /// <see cref="Unity.Physics.MeshCollider"/> instances from prepared vertex/triangle buffers
        /// using a parallel Burst job, attaches blobs to tile entities via ECB, and returns
        /// the remaining budget.
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

            var entityArr = new NativeArray<Entity>(tilesToProcess, Allocator.TempJob);
            var resultArr = new NativeArray<BlobAssetReference<Unity.Physics.Collider>>(tilesToProcess, Allocator.TempJob);

            for (int i = 0; i < tilesToProcess; i++)
            {
                entityArr[i] = pendingTiles[i].entity;
            }

#if UNITY_EDITOR
            using (s_ColliderCreationMarker.Auto())
#endif
            {
                var job = new CreateMeshCollidersJob
                {
                    entities = entityArr,
                    vertexBuffers = _vertexLookup,
                    triangleBuffers = _triangleLookup,
                    results = resultArr,
                    filter = collisionFilter
                };
                job.Schedule(tilesToProcess, 1).Complete();
            }

            for (int i = 0; i < tilesToProcess; i++)
            {
                var entity = entityArr[i];

                if (!EntityManager.Exists(entity))
                {
                    if (resultArr[i].IsCreated) resultArr[i].Dispose();
                    continue;
                }

                if (!resultArr[i].IsCreated)
                {
                    Debug.LogWarning($"[TerrainPhysics] Entity {entity.Index} produced no collider (empty buffers?), skipping");
                    ecb.RemoveComponent<PhysicsColliderPrepared>(entity);
                    continue;
                }

                ecb.AddComponent(entity, new PhysicsColliderRegistrationPending { collider = resultArr[i] });
                ecb.RemoveComponent<PhysicsColliderPrepared>(entity);
                // Keep ColliderPreparedVertexElement/ColliderPreparedTriangleElement so the
                // TerrainColliderVisualizer can draw the actual baked collider geometry.
                // These are removed by TerrainDistanceTrackingSystem.RemoveColliderState when
                // the collider itself is removed, keeping the overlay in lockstep with physics.
                budget--;
            }

            entityArr.Dispose();
            resultArr.Dispose();

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

    /// <summary>
    /// Burst-compiled parallel job that calls <see cref="MeshCollider.Create"/> for each terrain tile
    /// in the budget. Runs BVH construction on worker threads instead of the main thread, eliminating
    /// the per-tile 2–8 ms stall from sequential main-thread collider creation.
    /// Results are written to <see cref="results"/>; a default (not-created) blob indicates an empty
    /// or degenerate buffer that the caller should skip and clean up.
    /// </summary>
    [BurstCompile]
    struct CreateMeshCollidersJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> entities;
        [ReadOnly] public BufferLookup<ColliderPreparedVertexElement> vertexBuffers;
        [ReadOnly] public BufferLookup<ColliderPreparedTriangleElement> triangleBuffers;
        [WriteOnly] public NativeArray<BlobAssetReference<Unity.Physics.Collider>> results;
        public CollisionFilter filter;

        /// <summary>
        /// Creates a <see cref="MeshCollider"/> blob for the entity at <paramref name="index"/>.
        /// Writes a default blob when the entity has no prepared buffers (caller detects via <c>IsCreated</c>).
        /// </summary>
        public void Execute(int index)
        {
            Entity entity = entities[index];

            if (!vertexBuffers.HasBuffer(entity) || !triangleBuffers.HasBuffer(entity))
            {
                results[index] = default;
                return;
            }

            var verts = vertexBuffers[entity].Reinterpret<float3>().AsNativeArray();
            var tris  = triangleBuffers[entity].Reinterpret<int3>().AsNativeArray();

            if (verts.Length == 0 || tris.Length == 0)
            {
                results[index] = default;
                return;
            }

            results[index] = Unity.Physics.MeshCollider.Create(verts, tris, filter, Unity.Physics.Material.Default);
        }
    }
}
