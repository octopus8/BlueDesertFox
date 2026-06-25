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
#endif

        public NativeList<Entity> _inFlightEntities;
        public NativeArray<float3> _inFlightSourceVertices;
        public NativeArray<int> _inFlightSourceIndices;
        public NativeArray<int> _inFlightVertexCounts;
        public NativeArray<int> _inFlightIndexCounts;
        public NativeArray<BlobAssetReference<Unity.Physics.Collider>> _inFlightResults;
        public int _maxVertsPerTile;
        public int _maxIndicesPerTile;
        public JobHandle _inFlightHandle;
        public bool _hasInFlight;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TerrainTileConfig>();
            state.RequireForUpdate<CameraDataSingleton>();
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_hasInFlight)
            {
                _inFlightHandle.Complete();
                DisposeInFlight(ref this);
            }
        }

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
                var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
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
                            tile.ValueRO.gridCoordinate, config, cameraData)
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

                int maxVertsPerTile = config.verticesPerSide * config.verticesPerSide;
                int maxIndicesPerTile = (config.verticesPerSide - 1) * (config.verticesPerSide - 1) * 6;
                _maxVertsPerTile = maxVertsPerTile;
                _maxIndicesPerTile = maxIndicesPerTile;

                _inFlightEntities = new NativeList<Entity>(tilesToProcess, Allocator.Persistent);
                _inFlightSourceVertices = new NativeArray<float3>(
                    tilesToProcess * maxVertsPerTile, Allocator.Persistent);
                _inFlightSourceIndices = new NativeArray<int>(
                    tilesToProcess * maxIndicesPerTile, Allocator.Persistent);
                _inFlightVertexCounts = new NativeArray<int>(tilesToProcess, Allocator.Persistent);
                _inFlightIndexCounts = new NativeArray<int>(tilesToProcess, Allocator.Persistent);
                _inFlightResults = new NativeArray<BlobAssetReference<Unity.Physics.Collider>>(
                    tilesToProcess, Allocator.Persistent);

                int scheduledCount = 0;
                for (int i = 0; i < tilesToProcess; i++)
                {
                    Entity entity = candidates[i].entity;
                    if (!state.EntityManager.Exists(entity))
                        continue;

                    var vBuf = state.EntityManager.GetBuffer<VertexElement>(entity, isReadOnly: true);
                    var iBuf = state.EntityManager.GetBuffer<IndexElement>(entity, isReadOnly: true);

                    int vCount = math.min(vBuf.Length, maxVertsPerTile);
                    int iCount = math.min(iBuf.Length, maxIndicesPerTile);

                    if (vCount == 0 || iCount < 3)
                        continue;

                    int vOffset = scheduledCount * maxVertsPerTile;
                    int iOffset = scheduledCount * maxIndicesPerTile;

                    for (int v = 0; v < vCount; v++)
                        _inFlightSourceVertices[vOffset + v] = vBuf[v].value;
                    for (int j = 0; j < iCount; j++)
                        _inFlightSourceIndices[iOffset + j] = iBuf[j].value;

                    _inFlightVertexCounts[scheduledCount] = vCount;
                    _inFlightIndexCounts[scheduledCount] = iCount;
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
                    BelongsTo = layerMask,
                    CollidesWith = ~0u,
                    GroupIndex = 0
                };

                var job = new BuildTerrainMeshColliderJob
                {
                    sourceVertices = _inFlightSourceVertices,
                    sourceIndices = _inFlightSourceIndices,
                    vertexCounts = _inFlightVertexCounts,
                    indexCounts = _inFlightIndexCounts,
                    maxVerticesPerTile = maxVertsPerTile,
                    maxIndicesPerTile = maxIndicesPerTile,
                    filter = filter,
                    results = _inFlightResults
                };

                _inFlightHandle = job.Schedule(scheduledCount, 1, state.Dependency);
                _hasInFlight = true;
            }
        }

        internal static void DisposeInFlight(ref TerrainPhysicsScheduleSystem s)
        {
            if (s._inFlightEntities.IsCreated) s._inFlightEntities.Dispose();
            if (s._inFlightSourceVertices.IsCreated) s._inFlightSourceVertices.Dispose();
            if (s._inFlightSourceIndices.IsCreated) s._inFlightSourceIndices.Dispose();
            if (s._inFlightVertexCounts.IsCreated) s._inFlightVertexCounts.Dispose();
            if (s._inFlightIndexCounts.IsCreated) s._inFlightIndexCounts.Dispose();
            if (s._inFlightResults.IsCreated) s._inFlightResults.Dispose();
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
                        $"{sched._inFlightEntities.Length} collider(s). Consider lowering maxPhysicsCollidersCreatedPerFrame.");
                }
#endif

                for (int i = 0; i < sched._inFlightEntities.Length; i++)
                {
                    Entity entity = sched._inFlightEntities[i];
                    BlobAssetReference<Unity.Physics.Collider> result = sched._inFlightResults[i];

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

                TerrainPhysicsScheduleSystem.DisposeInFlight(ref sched);
            }
        }
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
