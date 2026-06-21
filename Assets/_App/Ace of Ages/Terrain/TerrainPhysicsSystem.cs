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
    /// Registers prepared physics colliders with the physics world.  Attaches the already-built
    /// <see cref="BlobAssetReference{Collider}"/> (produced by <see cref="TerrainPhysicsCompleteSystem"/>)
    /// to the tile entity as a <see cref="PhysicsCollider"/> component.
    ///
    /// The expensive BVH construction has been moved off the main thread: it is scheduled
    /// cross-frame by <see cref="TerrainPhysicsScheduleSystem"/> and completed at the start of
    /// the next frame by <see cref="TerrainPhysicsCompleteSystem"/>, running on worker threads
    /// during the XRUpdate window.  This system is now a lightweight registration step only.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial class TerrainPhysicsSystem : SystemBase
    {
#if UNITY_EDITOR
        private static readonly ProfilerMarker s_RegisterMarker = new ProfilerMarker("TerrainPhysics.RegisterCollider");
#endif

        /// <summary>Registers the <see cref="TerrainTileConfig"/> singleton requirement.</summary>
        protected override void OnCreate()
        {
            RequireForUpdate<TerrainTileConfig>();
        }

        /// <summary>
        /// Processes up to <c>maxCollidersCreatedPerFrame</c> tiles that have a fully-built
        /// <see cref="PhysicsColliderRegistrationPending"/> component, attaching the blob as a
        /// live <see cref="PhysicsCollider"/> and marking the tile with <see cref="PhysicsColliderValid"/>.
        /// </summary>
        protected override void OnUpdate()
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.enablePhysicsColliders)
                return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

#if UNITY_EDITOR
            using (s_RegisterMarker.Auto())
#endif
            {
                RegisterPendingColliders(TerrainPhysicsBudget.GetCreationBudget(config), ecb);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Iterates tiles with <see cref="PhysicsColliderRegistrationPending"/>, attaches the
        /// collider blob as a live <see cref="PhysicsCollider"/>, and removes the pending tag.
        /// </summary>
        private void RegisterPendingColliders(int budget, EntityCommandBuffer ecb)
        {
            if (budget <= 0)
                return;

            var pendingEntities = new NativeList<Entity>(budget, Allocator.Temp);

            foreach (var (_, entity) in SystemAPI.Query<RefRO<PhysicsColliderRegistrationPending>>().WithEntityAccess())
            {
                pendingEntities.Add(entity);
                if (pendingEntities.Length >= budget)
                    break;
            }

            for (int i = 0; i < pendingEntities.Length; i++)
            {
                Entity entity = pendingEntities[i];
                if (!EntityManager.Exists(entity))
                    continue;

                var pending = EntityManager.GetComponentData<PhysicsColliderRegistrationPending>(entity);
                ecb.AddComponent(entity, new PhysicsCollider { Value = pending.collider });

                if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                    ecb.AddSharedComponent(entity, new PhysicsWorldIndex());

                ecb.AddComponent<PhysicsColliderValid>(entity);
                ecb.RemoveComponent<PhysicsColliderRegistrationPending>(entity);
            }

            pendingEntities.Dispose();
        }
    }

    /// <summary>
    /// Schedules <see cref="CreateMeshCollidersJob"/> (BVH construction) as a cross-frame
    /// background job so the expensive <c>MeshCollider.Create</c> work runs on worker threads
    /// during the XRUpdate window rather than blocking <c>SimulationSystemGroup</c>.
    ///
    /// At schedule time, prepared vertex and triangle data is <b>copied</b> from live ECS
    /// <see cref="DynamicBuffer{T}"/> into Persistent <see cref="NativeArray{T}"/>s.  This
    /// mirrors the pattern used by <see cref="TerrainColliderScheduleSystem"/> for the decimation
    /// step and is necessary because structural changes that occur between the schedule and
    /// completion frames would otherwise invalidate live ECS buffer pointers held by the job.
    ///
    /// Pairs with <see cref="TerrainPhysicsCompleteSystem"/> which harvests the BVH results in
    /// <c>InitializationSystemGroup</c> of the following frame.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    [UpdateAfter(typeof(TerrainPhysicsSystem))]
    public partial struct TerrainPhysicsScheduleSystem : ISystem
    {
#if UNITY_EDITOR
        private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.BvhSchedule");
#endif

        // ── Cross-frame in-flight state (Persistent allocations, disposed by Complete system) ───
        public NativeList<Entity>  _inFlightEntities;
        public NativeArray<float3> _inFlightVertices;    // [entityIdx * _maxVertsPerTile + v]
        public NativeArray<int3>   _inFlightTriangles;   // [entityIdx * _maxTrisPerTile  + t]
        public NativeArray<int>    _inFlightVertexCounts;
        public NativeArray<int>    _inFlightTriangleCounts;
        public NativeArray<BlobAssetReference<Unity.Physics.Collider>> _inFlightResults;
        public int      _maxVertsPerTile;
        public int      _maxTrisPerTile;
        public JobHandle _inFlightHandle;
        public bool      _hasInFlight;

        /// <summary>Registers the <see cref="TerrainTileConfig"/> singleton requirement.</summary>
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TerrainTileConfig>();
        }

        /// <summary>Completes any dangling in-flight job and releases its allocations.</summary>
        public void OnDestroy(ref SystemState state)
        {
            if (_hasInFlight)
            {
                _inFlightHandle.Complete();
                DisposeInFlight(ref this);
            }
        }

        /// <summary>
        /// Selects up to <c>maxCollidersCreatedPerFrame</c> tiles with prepared collider data
        /// (sorted by camera-aware priority), copies their vertex/triangle buffers into Persistent
        /// NativeArrays, and schedules <see cref="CreateMeshCollidersJob"/> without blocking.
        /// Skips the frame if a previous job is still in flight.
        /// </summary>
        public void OnUpdate(ref SystemState state)
        {
            if (_hasInFlight)
                return;

            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            if (!config.enablePhysicsColliders)
                return;

#if UNITY_EDITOR
            using (s_ProfilerMarker.Auto())
#endif
            {
                // ── Collect and sort candidates ───────────────────────────────────────────────
                var candidates = new NativeList<ColliderEntityWithPriority>(Allocator.Temp);

                foreach (var (prepared, entity) in SystemAPI
                    .Query<RefRO<PhysicsColliderPrepared>>()
                    .WithAll<ColliderPreparedVertexElement, ColliderPreparedTriangleElement>()
                    .WithNone<PhysicsColliderRegistrationPending>()
                    .WithEntityAccess())
                {
                    candidates.Add(new ColliderEntityWithPriority
                    {
                        entity   = entity,
                        priority = prepared.ValueRO.priority
                    });
                }

                if (candidates.Length == 0)
                {
                    candidates.Dispose();
                    return;
                }

                if (candidates.Length > 1)
                    candidates.Sort(new ColliderPriorityComparer());

                int budget         = TerrainPhysicsBudget.GetCreationBudget(config);
                int tilesToProcess = math.min(candidates.Length, budget);

                int maxVertsPerTile = config.verticesPerSide * config.verticesPerSide;
                int maxTrisPerTile  = (config.verticesPerSide - 1) * (config.verticesPerSide - 1) * 2;
                _maxVertsPerTile    = maxVertsPerTile;
                _maxTrisPerTile     = maxTrisPerTile;

                // ── Allocate Persistent cross-frame storage ───────────────────────────────────
                _inFlightEntities       = new NativeList<Entity>(tilesToProcess, Allocator.Persistent);
                _inFlightVertices       = new NativeArray<float3>(tilesToProcess * maxVertsPerTile, Allocator.Persistent);
                _inFlightTriangles      = new NativeArray<int3>(tilesToProcess * maxTrisPerTile, Allocator.Persistent);
                _inFlightVertexCounts   = new NativeArray<int>(tilesToProcess, Allocator.Persistent);
                _inFlightTriangleCounts = new NativeArray<int>(tilesToProcess, Allocator.Persistent);
                _inFlightResults        = new NativeArray<BlobAssetReference<Unity.Physics.Collider>>(
                    tilesToProcess, Allocator.Persistent);

                // ── Copy buffer data from ECS into NativeArrays (main thread) ─────────────────
                // This is the key cross-frame safety step: the job reads from these copies, not
                // from live ECS DynamicBuffer pointers that structural changes could invalidate.
                int scheduledCount = 0;
                for (int i = 0; i < tilesToProcess; i++)
                {
                    Entity entity = candidates[i].entity;
                    if (!state.EntityManager.Exists(entity))
                        continue;
                    if (!state.EntityManager.HasBuffer<ColliderPreparedVertexElement>(entity))
                        continue;
                    if (!state.EntityManager.HasBuffer<ColliderPreparedTriangleElement>(entity))
                        continue;

                    var vBuf = state.EntityManager.GetBuffer<ColliderPreparedVertexElement>(entity, isReadOnly: true);
                    var tBuf = state.EntityManager.GetBuffer<ColliderPreparedTriangleElement>(entity, isReadOnly: true);

                    int vCount = math.min(vBuf.Length, maxVertsPerTile);
                    int tCount = math.min(tBuf.Length, maxTrisPerTile);

                    if (vCount == 0 || tCount == 0)
                        continue;

                    int vOffset = scheduledCount * maxVertsPerTile;
                    int tOffset = scheduledCount * maxTrisPerTile;

                    for (int v = 0; v < vCount; v++)
                        _inFlightVertices[vOffset + v] = vBuf[v].value;
                    for (int t = 0; t < tCount; t++)
                        _inFlightTriangles[tOffset + t] = tBuf[t].value;

                    _inFlightVertexCounts[scheduledCount]   = vCount;
                    _inFlightTriangleCounts[scheduledCount] = tCount;
                    _inFlightEntities.Add(entity);
                    scheduledCount++;
                }

                candidates.Dispose();

                if (scheduledCount == 0)
                {
                    DisposeInFlight(ref this);
                    return;
                }

                uint layerMask = 1u << config.terrainPhysicsLayer;
                var filter = new CollisionFilter
                {
                    BelongsTo    = layerMask,
                    CollidesWith = ~0u,
                    GroupIndex   = 0
                };

                var job = new CreateMeshCollidersJob
                {
                    vertices            = _inFlightVertices,
                    triangles           = _inFlightTriangles,
                    vertexCounts        = _inFlightVertexCounts,
                    triangleCounts      = _inFlightTriangleCounts,
                    maxVerticesPerTile  = maxVertsPerTile,
                    maxTrianglesPerTile = maxTrisPerTile,
                    results             = _inFlightResults,
                    filter              = filter
                };

                // Intentionally NOT assigned to state.Dependency so the job runs freely on
                // worker threads across the frame boundary, completing during next frame's
                // EarlyUpdate.XRUpdate window.  state.Dependency is passed as the input
                // dependency so the job waits for any current-frame reads to finish first.
                _inFlightHandle = job.Schedule(scheduledCount, 1, state.Dependency);
                _hasInFlight    = true;
            }
        }

        /// <summary>Disposes all Persistent in-flight allocations and resets the in-flight flag.</summary>
        internal static void DisposeInFlight(ref TerrainPhysicsScheduleSystem s)
        {
            if (s._inFlightEntities.IsCreated)       s._inFlightEntities.Dispose();
            if (s._inFlightVertices.IsCreated)        s._inFlightVertices.Dispose();
            if (s._inFlightTriangles.IsCreated)       s._inFlightTriangles.Dispose();
            if (s._inFlightVertexCounts.IsCreated)    s._inFlightVertexCounts.Dispose();
            if (s._inFlightTriangleCounts.IsCreated)  s._inFlightTriangleCounts.Dispose();
            if (s._inFlightResults.IsCreated)         s._inFlightResults.Dispose();
            s._hasInFlight = false;
        }
    }

    /// <summary>
    /// Completes the <see cref="CreateMeshCollidersJob"/> scheduled by
    /// <see cref="TerrainPhysicsScheduleSystem"/> the previous frame, then writes the resulting
    /// <see cref="BlobAssetReference{Collider}"/> blobs onto their tile entities as
    /// <see cref="PhysicsColliderRegistrationPending"/> so <see cref="TerrainPhysicsSystem"/>
    /// can attach them to the physics world in the same frame.
    ///
    /// Runs in <c>InitializationSystemGroup</c> — immediately after
    /// <c>EarlyUpdate.XRUpdate</c> finishes — so <c>Complete()</c> returns almost instantly
    /// because BVH construction ran on worker threads during XRUpdate.
    ///
    /// <c>RequireForUpdate</c> is intentionally omitted so this system always ticks and never
    /// leaves a dangling job handle if the world enters a state where singletons are absent.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
    public partial struct TerrainPhysicsCompleteSystem : ISystem
    {
#if UNITY_EDITOR
        private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.BvhComplete");
#endif

        /// <summary>RequireForUpdate intentionally omitted — must always run to drain any in-flight handle.</summary>
        public void OnCreate(ref SystemState state) { }

        /// <summary>
        /// Completes the pending <see cref="CreateMeshCollidersJob"/>, writes each built collider
        /// as a <see cref="PhysicsColliderRegistrationPending"/> component, removes
        /// <see cref="PhysicsColliderPrepared"/>, and frees all Persistent in-flight allocations.
        /// Entities destroyed during the cross-frame window have their blob assets disposed to
        /// prevent leaks.
        /// </summary>
        public void OnUpdate(ref SystemState state)
        {
            var schedHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<TerrainPhysicsScheduleSystem>();
            if (schedHandle == SystemHandle.Null)
                return;

            ref var sched = ref state.WorldUnmanaged.GetUnsafeSystemRef<TerrainPhysicsScheduleSystem>(schedHandle);
            if (!sched._hasInFlight)
                return;

#if UNITY_EDITOR
            using (s_ProfilerMarker.Auto())
#endif
            {
                // Workers ran during XRUpdate — Complete() should return almost immediately.
                sched._inFlightHandle.Complete();

                for (int i = 0; i < sched._inFlightEntities.Length; i++)
                {
                    Entity entity = sched._inFlightEntities[i];
                    BlobAssetReference<Unity.Physics.Collider> result = sched._inFlightResults[i];

                    if (!state.EntityManager.Exists(entity))
                    {
                        // Entity was destroyed during the cross-frame window — dispose blob to avoid leaks.
                        if (result.IsCreated)
                            result.Dispose();
                        continue;
                    }

                    if (!result.IsCreated)
                    {
                        // Empty or degenerate buffers produced no collider — clean up preparation state.
                        Debug.LogWarning($"[TerrainPhysics] Entity {entity.Index} produced no collider (empty buffers?), skipping");
                        if (state.EntityManager.HasComponent<PhysicsColliderPrepared>(entity))
                            state.EntityManager.RemoveComponent<PhysicsColliderPrepared>(entity);
                        continue;
                    }

                    state.EntityManager.AddComponentData(entity,
                        new PhysicsColliderRegistrationPending { collider = result });

                    if (state.EntityManager.HasComponent<PhysicsColliderPrepared>(entity))
                        state.EntityManager.RemoveComponent<PhysicsColliderPrepared>(entity);
                }

                TerrainPhysicsScheduleSystem.DisposeInFlight(ref sched);
            }
        }
    }
}

/// <summary>
/// Burst-compiled parallel job that builds a <see cref="Unity.Physics.MeshCollider"/> BVH for
/// each terrain tile in the budget.  Reads pre-copied vertex and triangle data from flat
/// <see cref="NativeArray{T}"/>s rather than live ECS <see cref="DynamicBuffer{T}"/>s so the
/// job is safe to run across frame boundaries without ECS structural-change invalidation.
///
/// Results are written to <see cref="results"/>; a default (not-created) blob indicates empty
/// or degenerate input that the caller should skip and clean up.
///
/// Scheduled by <see cref="_App.Ace_of_Ages.Terrain.TerrainPhysicsScheduleSystem"/> and
/// completed by <see cref="_App.Ace_of_Ages.Terrain.TerrainPhysicsCompleteSystem"/>.
/// </summary>
[BurstCompile]
struct CreateMeshCollidersJob : IJobParallelFor
{
    // Flat arrays: tile i's data lives at [i * maxXxxPerTile ... i * maxXxxPerTile + count - 1]
    [ReadOnly] public NativeArray<float3> vertices;
    [ReadOnly] public NativeArray<int3>   triangles;
    [ReadOnly] public NativeArray<int>    vertexCounts;
    [ReadOnly] public NativeArray<int>    triangleCounts;
    public int maxVerticesPerTile;
    public int maxTrianglesPerTile;
    public CollisionFilter filter;

    [WriteOnly]
    public NativeArray<BlobAssetReference<Unity.Physics.Collider>> results;

    /// <summary>
    /// Builds a <see cref="Unity.Physics.MeshCollider"/> BVH for the tile at
    /// <paramref name="index"/>.  Writes a default blob when input counts are zero.
    /// </summary>
    public void Execute(int index)
    {
        int vCount = vertexCounts[index];
        int tCount = triangleCounts[index];

        if (vCount == 0 || tCount == 0)
        {
            results[index] = default;
            return;
        }

        var verts = vertices.GetSubArray(index * maxVerticesPerTile, vCount);
        var tris  = triangles.GetSubArray(index * maxTrianglesPerTile, tCount);

        results[index] = Unity.Physics.MeshCollider.Create(verts, tris, filter, Unity.Physics.Material.Default);
    }
}

struct ColliderEntityWithPriority
{
    public Entity entity;
    public int    priority;
}

/// <summary>Sorts <see cref="ColliderEntityWithPriority"/> values so higher-priority (lower value) tiles are processed first.</summary>
struct ColliderPriorityComparer : IComparer<ColliderEntityWithPriority>
{
    /// <inheritdoc/>
    public int Compare(ColliderEntityWithPriority a, ColliderEntityWithPriority b)
        => a.priority.CompareTo(b.priority);
}
