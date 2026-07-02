using System.Collections.Generic;
using System.Diagnostics;
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
    /// Persistent NativeArray pool for cross-frame BVH construction batches.
    /// </summary>
    public struct TerrainPhysicsBufferPool
    {
        public NativeList<Entity> entities;
        public NativeArray<float3> vertices;
        public NativeArray<int> indices;
        public NativeArray<int> vertexCounts;
        public NativeArray<int> indexCounts;
        public NativeArray<BlobAssetReference<Unity.Physics.Collider>> results;

        int _batchCapacity;
        int _maxVertsPerTile;
        int _maxIndicesPerTile;

        public int MaxVertsPerTile => _maxVertsPerTile;
        public int MaxIndicesPerTile => _maxIndicesPerTile;

        public void EnsureCapacity(int batchSize, int vertsPerSide)
        {
            int vertsPerTile = vertsPerSide * vertsPerSide;
            int indicesPerTile = (vertsPerSide - 1) * (vertsPerSide - 1) * 6;

            bool needsRealloc = !vertices.IsCreated
                || _batchCapacity < batchSize
                || _maxVertsPerTile != vertsPerTile
                || _maxIndicesPerTile != indicesPerTile;

            if (needsRealloc)
            {
                DisposeArrays();

                _batchCapacity = math.max(_batchCapacity, batchSize);
                _maxVertsPerTile = vertsPerTile;
                _maxIndicesPerTile = indicesPerTile;

                if (!entities.IsCreated)
                    entities = new NativeList<Entity>(_batchCapacity, Allocator.Persistent);
                else if (entities.Capacity < _batchCapacity)
                    entities.Capacity = _batchCapacity;

                vertices = new NativeArray<float3>(_batchCapacity * vertsPerTile, Allocator.Persistent);
                indices = new NativeArray<int>(_batchCapacity * indicesPerTile, Allocator.Persistent);
                vertexCounts = new NativeArray<int>(_batchCapacity, Allocator.Persistent);
                indexCounts = new NativeArray<int>(_batchCapacity, Allocator.Persistent);
                results = new NativeArray<BlobAssetReference<Unity.Physics.Collider>>(
                    _batchCapacity, Allocator.Persistent);
            }
            else if (entities.Capacity < batchSize)
            {
                entities.Capacity = batchSize;
            }

            entities.Clear();
        }

        public void ReleaseBatch()
        {
            entities.Clear();
        }

        public void Dispose()
        {
            DisposeArrays();
            if (entities.IsCreated)
                entities.Dispose();
        }

        void DisposeArrays()
        {
            if (vertices.IsCreated) vertices.Dispose();
            if (indices.IsCreated) indices.Dispose();
            if (vertexCounts.IsCreated) vertexCounts.Dispose();
            if (indexCounts.IsCreated) indexCounts.Dispose();
            if (results.IsCreated) results.Dispose();
        }
    }

    /// <summary>
    /// Registers prepared physics colliders with the physics world.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial class TerrainPhysicsSystem : SystemBase
    {
#if UNITY_EDITOR
        static readonly ProfilerMarker s_RegisterMarker = new ProfilerMarker("TerrainPhysics.RegisterCollider");
#endif

        protected override void OnCreate()
        {
            RequireForUpdate<TerrainTileConfig>();
        }

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
                RegisterPendingColliders(TerrainPhysicsBudget.GetRegistrationBudget(config), ecb);
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        void RegisterPendingColliders(int budget, EntityCommandBuffer ecb)
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

                if (EntityManager.HasComponent<PhysicsColliderNeedsPreparation>(entity))
                    ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, false);
            }

            pendingEntities.Dispose();
        }
    }

    /// <summary>
    /// Schedules cross-frame BVH construction directly from terrain mesh buffers,
    /// skipping the redundant intermediate prepared-buffer stage.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    [UpdateAfter(typeof(TerrainPhysicsSystem))]
    public partial struct TerrainPhysicsScheduleSystem : ISystem
    {
#if UNITY_EDITOR
        static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.BvhSchedule");
        static readonly ProfilerMarker s_BufferCopyMarker = new ProfilerMarker("TerrainPhysics.BufferCopy");
#endif

        public TerrainPhysicsBufferPool _bufferPool;
        public JobHandle _inFlightHandle;
        public bool _hasInFlight;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TerrainTileConfig>();
            state.RequireForUpdate<CameraDataSingleton>();
            state.RequireForUpdate<ScrollOffset>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_hasInFlight)
                _inFlightHandle.Complete();

            _bufferPool.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_hasInFlight)
            {
                state.Dependency = JobHandle.CombineDependencies(state.Dependency, _inFlightHandle);
                return;
            }

            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            if (!config.enablePhysicsColliders)
                return;

#if UNITY_EDITOR
            using (s_ProfilerMarker.Auto())
#endif
            {
                var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
                float3 scrollOffset = SystemAPI.GetSingleton<ScrollOffset>().accumulatedOffset;
                var candidates = new NativeList<ColliderEntityWithPriority>(Allocator.Temp);

                foreach (var (tile, entity) in SystemAPI
                    .Query<RefRO<TerrainTile>>()
                    .WithAll<VertexElement, IndexElement, PhysicsColliderNeedsPreparation>()
                    .WithNone<PhysicsColliderRegistrationPending, PhysicsColliderValid>()
                    .WithEntityAccess())
                {
                    if (!SystemAPI.IsComponentEnabled<PhysicsColliderNeedsPreparation>(entity))
                        continue;

                    if (!tile.ValueRO.meshGenerated)
                        continue;

                    candidates.Add(new ColliderEntityWithPriority
                    {
                        entity = entity,
                        priority = TerrainColliderPriority.Compute(
                            tile.ValueRO.gridCoordinate, config, cameraData, scrollOffset)
                    });
                }

                if (candidates.Length == 0)
                {
                    candidates.Dispose();
                    return;
                }

                if (candidates.Length > 1)
                    candidates.Sort(new ColliderPriorityComparer());

                int budget = TerrainPhysicsBudget.GetBvhCreationBudget(config);

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (candidates.Length > budget)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[TerrainPhysics] BVH queue depth {candidates.Length} exceeds budget {budget}. " +
                        "Processing highest-priority tiles first.");
                }
#endif

                int tilesToProcess = math.min(candidates.Length, budget);
                _bufferPool.EnsureCapacity(tilesToProcess, config.verticesPerSide);

                int maxVertsPerTile = _bufferPool.MaxVertsPerTile;
                int maxIndicesPerTile = _bufferPool.MaxIndicesPerTile;

                for (int i = 0; i < tilesToProcess; i++)
                {
                    Entity entity = candidates[i].entity;
                    if (state.EntityManager.Exists(entity))
                        _bufferPool.entities.Add(entity);
                }

                candidates.Dispose();

                int scheduledCount = _bufferPool.entities.Length;
                if (scheduledCount == 0)
                    return;

                var vertexLookup = SystemAPI.GetBufferLookup<VertexElement>(true);
                var indexLookup = SystemAPI.GetBufferLookup<IndexElement>(true);
                vertexLookup.Update(ref state);
                indexLookup.Update(ref state);

                var entityArray = _bufferPool.entities.AsArray();

                var copyJob = new CopyTerrainMeshBuffersJob
                {
                    entities = entityArray,
                    vertexLookup = vertexLookup,
                    indexLookup = indexLookup,
                    outVertices = _bufferPool.vertices,
                    outIndices = _bufferPool.indices,
                    outVertexCounts = _bufferPool.vertexCounts,
                    outIndexCounts = _bufferPool.indexCounts,
                    maxVerticesPerTile = maxVertsPerTile,
                    maxIndicesPerTile = maxIndicesPerTile
                };

                uint layerMask = 1u << config.terrainPhysicsLayer;
                var filter = new CollisionFilter
                {
                    BelongsTo = layerMask,
                    CollidesWith = ~0u,
                    GroupIndex = 0
                };

                var buildJob = new BuildTerrainMeshColliderJob
                {
                    sourceVertices = _bufferPool.vertices,
                    sourceIndices = _bufferPool.indices,
                    vertexCounts = _bufferPool.vertexCounts,
                    indexCounts = _bufferPool.indexCounts,
                    maxVerticesPerTile = maxVertsPerTile,
                    maxIndicesPerTile = maxIndicesPerTile,
                    filter = filter,
                    results = _bufferPool.results
                };

#if UNITY_EDITOR
                JobHandle copyHandle;
                using (s_BufferCopyMarker.Auto())
                {
                    copyHandle = copyJob.Schedule(scheduledCount, 1, state.Dependency);
                }
#else
                var copyHandle = copyJob.Schedule(scheduledCount, 1, state.Dependency);
#endif

                _inFlightHandle = buildJob.Schedule(scheduledCount, 1, copyHandle);
                _hasInFlight = true;
                state.Dependency = JobHandle.CombineDependencies(state.Dependency, _inFlightHandle);
            }
        }

        internal static void ReleaseBatch(ref TerrainPhysicsScheduleSystem s)
        {
            s._bufferPool.ReleaseBatch();
            s._hasInFlight = false;
        }
    }

    /// <summary>
    /// Completes the cross-frame BVH job and writes results as PhysicsColliderRegistrationPending.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
    public partial struct TerrainPhysicsCompleteSystem : ISystem
    {
#if UNITY_EDITOR
        static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.BvhComplete");
#endif

        public void OnCreate(ref SystemState state) { }

        public void OnUpdate(ref SystemState state)
        {
            var schedHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<TerrainPhysicsScheduleSystem>();
            if (schedHandle == SystemHandle.Null)
                return;

            ref var sched = ref state.WorldUnmanaged.GetUnsafeSystemRef<TerrainPhysicsScheduleSystem>(schedHandle);
            if (!sched._hasInFlight)
                return;

            if (!sched._inFlightHandle.IsCompleted)
                return;

#if UNITY_EDITOR
            using (s_ProfilerMarker.Auto())
#endif
            {
                var sw = Stopwatch.StartNew();
                sched._inFlightHandle.Complete();
                sw.Stop();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
                double completeMs = sw.Elapsed.TotalMilliseconds;
                if (completeMs > 8.0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[TerrainPhysics] BvhComplete waited {completeMs:F1}ms for " +
                        $"{sched._bufferPool.entities.Length} collider(s). Consider lowering maxPhysicsCollidersCreatedPerFrame.");
                }
#endif

                for (int i = 0; i < sched._bufferPool.entities.Length; i++)
                {
                    Entity entity = sched._bufferPool.entities[i];
                    BlobAssetReference<Unity.Physics.Collider> result = sched._bufferPool.results[i];

                    if (!state.EntityManager.Exists(entity))
                    {
                        if (result.IsCreated)
                            result.Dispose();
                        continue;
                    }

                    if (!result.IsCreated)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[TerrainPhysics] Entity {entity.Index} produced no collider (empty mesh?), skipping");
                        if (state.EntityManager.HasComponent<PhysicsColliderNeedsPreparation>(entity))
                            state.EntityManager.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, false);
                        continue;
                    }

                    state.EntityManager.AddComponentData(entity,
                        new PhysicsColliderRegistrationPending { collider = result });
                }

                TerrainPhysicsScheduleSystem.ReleaseBatch(ref sched);
            }
        }
    }
}

/// <summary>
/// Burst job that copies terrain mesh buffers into flat native arrays for BVH construction.
/// </summary>
[BurstCompile]
struct CopyTerrainMeshBuffersJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<Entity> entities;
    [ReadOnly] public BufferLookup<VertexElement> vertexLookup;
    [ReadOnly] public BufferLookup<IndexElement> indexLookup;

    [NativeDisableParallelForRestriction]
    public NativeArray<float3> outVertices;

    [NativeDisableParallelForRestriction]
    public NativeArray<int> outIndices;

    public NativeArray<int> outVertexCounts;
    public NativeArray<int> outIndexCounts;
    public int maxVerticesPerTile;
    public int maxIndicesPerTile;

    public void Execute(int index)
    {
        Entity entity = entities[index];

        if (!vertexLookup.HasBuffer(entity) || !indexLookup.HasBuffer(entity))
        {
            outVertexCounts[index] = 0;
            outIndexCounts[index] = 0;
            return;
        }

        var vBuf = vertexLookup[entity];
        var iBuf = indexLookup[entity];

        int vCount = math.min(vBuf.Length, maxVerticesPerTile);
        int iCount = math.min(iBuf.Length, maxIndicesPerTile);

        outVertexCounts[index] = vCount;
        outIndexCounts[index] = iCount;

        int vOffset = index * maxVerticesPerTile;
        int iOffset = index * maxIndicesPerTile;

        for (int v = 0; v < vCount; v++)
            outVertices[vOffset + v] = vBuf[v].value;

        for (int j = 0; j < iCount; j++)
            outIndices[iOffset + j] = iBuf[j].value;
    }
}

/// <summary>
/// Burst job that builds int3 triangles from flat mesh indices and constructs a MeshCollider BVH.
/// </summary>
[BurstCompile]
struct BuildTerrainMeshColliderJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> sourceVertices;
    [ReadOnly] public NativeArray<int> sourceIndices;
    [ReadOnly] public NativeArray<int> vertexCounts;
    [ReadOnly] public NativeArray<int> indexCounts;
    public int maxVerticesPerTile;
    public int maxIndicesPerTile;
    public CollisionFilter filter;

    [WriteOnly]
    public NativeArray<BlobAssetReference<Unity.Physics.Collider>> results;

    public void Execute(int index)
    {
        int vCount = vertexCounts[index];
        int iCount = indexCounts[index];

        if (vCount == 0 || iCount < 3)
        {
            results[index] = default;
            return;
        }

        int tCount = iCount / 3;
        var verts = sourceVertices.GetSubArray(index * maxVerticesPerTile, vCount);
        int iOffset = index * maxIndicesPerTile;

        var tris = new NativeArray<int3>(tCount, Allocator.Temp);
        for (int t = 0; t < tCount; t++)
        {
            int io = iOffset + t * 3;
            tris[t] = new int3(sourceIndices[io], sourceIndices[io + 1], sourceIndices[io + 2]);
        }

        results[index] = Unity.Physics.MeshCollider.Create(
            verts, tris, filter, Unity.Physics.Material.Default);
        tris.Dispose();
    }
}

struct ColliderEntityWithPriority
{
    public Entity entity;
    public int priority;
}

struct ColliderPriorityComparer : IComparer<ColliderEntityWithPriority>
{
    public int Compare(ColliderEntityWithPriority a, ColliderEntityWithPriority b)
        => a.priority.CompareTo(b.priority);
}
