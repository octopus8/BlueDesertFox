using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// Schedules the Burst-compiled <see cref="PrepareColliderDataJob"/> at the end of the frame
/// (PresentationSystemGroup) so that worker threads can execute the mesh copy work during
/// <c>EarlyUpdate.XRUpdate</c> of the next frame.
///
/// Source vertex/index data is copied from ECS DynamicBuffers into Persistent NativeArrays at
/// schedule time. This is the safe pattern for cross-frame job scheduling: the job holds no live
/// ECS references, so structural changes that occur between schedule and completion cannot
/// invalidate its input data.
///
/// Pairs with <see cref="TerrainColliderCompleteSystem"/> which harvests results in
/// <c>InitializationSystemGroup</c> immediately after XRUpdate finishes.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(CameraDataUpdateSystem))]
public partial struct TerrainColliderScheduleSystem : ISystem
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.ColliderSchedule");
#endif

    // ── Cross-frame in-flight state ────────────────────────────────────────────────────────────
    // All allocations use Allocator.Persistent so they survive the frame boundary.
    // They are disposed by TerrainColliderCompleteSystem after the job is consumed,
    // or by OnDestroy if the world is torn down while a job is still in flight.
    public NativeList<Entity>  _inFlightEntities;

    // Copies of source ECS buffer data (safe for worker threads across frame boundary)
    public NativeArray<float3> _inFlightSourceVertices;   // [entityIdx * maxVertsPerTile + vertIdx]
    public NativeArray<int>    _inFlightSourceIndices;    // [entityIdx * maxIndicesPerTile + idxIdx]
    public NativeArray<int2>   _inFlightGridCoords;       // [entityIdx]

    // Output arrays written by PrepareColliderDataJob
    public NativeArray<ColliderPreparedVertexElement>   _inFlightOutVertices;   // [entityIdx * maxVertsPerTile]
    public NativeArray<ColliderPreparedTriangleElement> _inFlightOutTriangles;  // [entityIdx * maxTrianglesPerTile]
    public NativeArray<int>    _inFlightVertexCounts;     // [entityIdx] actual vertex count
    public NativeArray<int>    _inFlightTriangleCounts;   // [entityIdx] actual triangle count
    public NativeArray<int>    _inFlightPriorities;       // [entityIdx] camera-aware priority score

    public JobHandle _inFlightHandle;
    public bool      _hasInFlight;

    // Stride info needed by TerrainColliderCompleteSystem to calculate array offsets
    public int _maxVerticesPerTile;
    public int _maxTrianglesPerTile;

    private EntityQuery _query;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<CameraDataSingleton>();

        _query = state.GetEntityQuery(
            ComponentType.ReadOnly<PhysicsColliderNeedsPreparation>(),
            ComponentType.ReadOnly<VertexElement>(),
            ComponentType.ReadOnly<IndexElement>(),
            ComponentType.ReadOnly<TerrainTile>()
        );
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
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();

            if (!config.enablePhysicsColliders)
                return;

            // If the complete system hasn't consumed last frame's job yet, skip this frame.
            if (_hasInFlight)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogWarning("[TerrainCollider] Schedule skipped: previous job still in flight. " +
                    "TerrainColliderCompleteSystem may have been skipped.");
#endif
                return;
            }

            int entityCount = _query.CalculateEntityCount();
            if (entityCount == 0)
                return;

            int verticesPerSide  = config.verticesPerSide;
            int maxVertsPerTile  = verticesPerSide * verticesPerSide;
            int maxIdxPerTile    = (verticesPerSide - 1) * (verticesPerSide - 1) * 6; // triangles*3
            int maxTrisPerTile   = (verticesPerSide - 1) * (verticesPerSide - 1) * 2;

            _maxVerticesPerTile  = maxVertsPerTile;
            _maxTrianglesPerTile = maxTrisPerTile;

            var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();

            // ── Allocate persistent storage ───────────────────────────────────────────────────
            _inFlightEntities        = new NativeList<Entity>(entityCount, Allocator.Persistent);
            _inFlightGridCoords      = new NativeArray<int2>(entityCount, Allocator.Persistent);
            _inFlightSourceVertices  = new NativeArray<float3>(entityCount * maxVertsPerTile, Allocator.Persistent);
            _inFlightSourceIndices   = new NativeArray<int>(entityCount * maxIdxPerTile, Allocator.Persistent);
            _inFlightOutVertices     = new NativeArray<ColliderPreparedVertexElement>(entityCount * maxVertsPerTile, Allocator.Persistent);
            _inFlightOutTriangles    = new NativeArray<ColliderPreparedTriangleElement>(entityCount * maxTrisPerTile, Allocator.Persistent);
            _inFlightVertexCounts    = new NativeArray<int>(entityCount, Allocator.Persistent);
            _inFlightTriangleCounts  = new NativeArray<int>(entityCount, Allocator.Persistent);
            _inFlightPriorities      = new NativeArray<int>(entityCount, Allocator.Persistent);

            // ── Copy source data from ECS buffers into NativeArrays (main thread) ─────────────
            // This is the key safety step for cross-frame scheduling: the job reads from these
            // copies, not from live ECS buffer pointers that could be invalidated by structural
            // changes in subsequent frames.
            int idx = 0;
            foreach (var (vBuf, iBuf, tile, entity) in
                SystemAPI.Query<
                    DynamicBuffer<VertexElement>,
                    DynamicBuffer<IndexElement>,
                    RefRO<TerrainTile>>()
                .WithAll<PhysicsColliderNeedsPreparation>()
                .WithEntityAccess())
            {
                _inFlightEntities.Add(entity);
                _inFlightGridCoords[idx] = tile.ValueRO.gridCoordinate;

                int vOffset = idx * maxVertsPerTile;
                int iOffset = idx * maxIdxPerTile;

                int copyVCount = math.min(vBuf.Length, maxVertsPerTile);
                for (int v = 0; v < copyVCount; v++)
                    _inFlightSourceVertices[vOffset + v] = vBuf[v].value;

                int copyICount = math.min(iBuf.Length, maxIdxPerTile);
                for (int i = 0; i < copyICount; i++)
                    _inFlightSourceIndices[iOffset + i] = iBuf[i].value;

                idx++;
            }

            // Guard against any iteration/count discrepancy
            int actualCount = _inFlightEntities.Length;
            if (actualCount == 0)
            {
                DisposeInFlight(ref this);
                return;
            }

            var job = new PrepareColliderDataJob
            {
                sourceVertices      = _inFlightSourceVertices,
                sourceIndices       = _inFlightSourceIndices,
                gridCoords          = _inFlightGridCoords,
                verticesPerSide     = verticesPerSide,
                maxVerticesPerTile  = maxVertsPerTile,
                maxTrianglesPerTile = maxTrisPerTile,
                maxIndicesPerTile   = maxIdxPerTile,
                tileSize            = config.tileSize,
                cameraPosition      = cameraData.position,
                cameraForward       = cameraData.forward,
                viewDistance        = config.viewDistance,
                outVertices         = _inFlightOutVertices,
                outTriangles        = _inFlightOutTriangles,
                outVertexCounts     = _inFlightVertexCounts,
                outTriangleCounts   = _inFlightTriangleCounts,
                outPriorities       = _inFlightPriorities
            };

            // Intentionally NOT assigned to state.Dependency so the job runs freely on worker
            // threads across the frame boundary, completing during next frame's EarlyUpdate.XRUpdate.
            _inFlightHandle = job.Schedule(actualCount, 1, state.Dependency);
            _hasInFlight = true;
        }
    }

    internal static void DisposeInFlight(ref TerrainColliderScheduleSystem s)
    {
        if (s._inFlightEntities.IsCreated)       s._inFlightEntities.Dispose();
        if (s._inFlightSourceVertices.IsCreated)  s._inFlightSourceVertices.Dispose();
        if (s._inFlightSourceIndices.IsCreated)   s._inFlightSourceIndices.Dispose();
        if (s._inFlightGridCoords.IsCreated)      s._inFlightGridCoords.Dispose();
        if (s._inFlightOutVertices.IsCreated)     s._inFlightOutVertices.Dispose();
        if (s._inFlightOutTriangles.IsCreated)    s._inFlightOutTriangles.Dispose();
        if (s._inFlightVertexCounts.IsCreated)    s._inFlightVertexCounts.Dispose();
        if (s._inFlightTriangleCounts.IsCreated)  s._inFlightTriangleCounts.Dispose();
        if (s._inFlightPriorities.IsCreated)      s._inFlightPriorities.Dispose();
        s._hasInFlight = false;
    }
}

/// <summary>
/// Burst-compiled parallel job that copies terrain tile vertex/index buffers into physics-ready
/// collider meshes at full resolution. Runs on worker threads during <c>EarlyUpdate.XRUpdate</c>
/// of the next frame.
///
/// Reads source vertex/index data from pre-copied NativeArrays (not live ECS buffers) and writes
/// output to pre-allocated arrays. All inputs and outputs use flat array indexing:
/// tile <c>i</c>'s data lives at <c>i * maxSlots</c> in the respective array.
/// </summary>
[BurstCompile]
struct PrepareColliderDataJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> sourceVertices;
    [ReadOnly] public NativeArray<int>    sourceIndices;
    [ReadOnly] public NativeArray<int2>   gridCoords;

    public int   verticesPerSide;
    public int   maxVerticesPerTile;
    public int   maxTrianglesPerTile;
    public int   maxIndicesPerTile;
    public float tileSize;
    public float3 cameraPosition;
    public float3 cameraForward;
    public float viewDistance;

    [NativeDisableParallelForRestriction]
    public NativeArray<ColliderPreparedVertexElement>   outVertices;
    [NativeDisableParallelForRestriction]
    public NativeArray<ColliderPreparedTriangleElement> outTriangles;
    public NativeArray<int> outVertexCounts;
    public NativeArray<int> outTriangleCounts;
    public NativeArray<int> outPriorities;

    public void Execute(int index)
    {
        int2 gridCoord = gridCoords[index];

        int srcVOffset = index * maxVerticesPerTile;
        int srcIOffset = index * maxIndicesPerTile;
        int outVOffset = index * maxVerticesPerTile;
        int outTOffset = index * maxTrianglesPerTile;

        int totalVerts   = verticesPerSide * verticesPerSide;
        int totalIndices = (verticesPerSide - 1) * (verticesPerSide - 1) * 6;

        for (int v = 0; v < totalVerts; v++)
            outVertices[outVOffset + v] = new ColliderPreparedVertexElement
                { value = sourceVertices[srcVOffset + v] };

        int tCount = 0;
        for (int i = 0; i + 2 < totalIndices; i += 3)
        {
            outTriangles[outTOffset + tCount] = new ColliderPreparedTriangleElement
            {
                value = new int3(
                    sourceIndices[srcIOffset + i],
                    sourceIndices[srcIOffset + i + 1],
                    sourceIndices[srcIOffset + i + 2])
            };
            tCount++;
        }

        outVertexCounts[index]   = totalVerts;
        outTriangleCounts[index] = tCount;

        // Camera-aware priority score (lower = higher priority for TerrainPhysicsSystem sorting).
        float2 tileCenter = new float2(
            gridCoord.x * tileSize + tileSize * 0.5f,
            gridCoord.y * tileSize + tileSize * 0.5f);
        float2 cameraPos2D     = new float2(cameraPosition.x, cameraPosition.z);
        float2 toTile          = tileCenter - cameraPos2D;
        float  dist2D          = math.length(toTile);
        float  normalizedDist  = math.clamp(dist2D / viewDistance, 0f, 1f);
        float2 fwd2D           = math.normalize(new float2(cameraForward.x, cameraForward.z));
        float2 toTileNorm      = math.lengthsq(toTile) < 0.001f ? fwd2D : math.normalize(toTile);
        float  dot             = math.dot(fwd2D, toTileNorm);
        float  viewScore       = (dot + 1f) * 0.5f;
        outPriorities[index]   = (int)((1f - viewScore) * 1000f + normalizedDist * 500f);
    }
}

/// <summary>
/// Completes the collider preparation job scheduled by <see cref="TerrainColliderScheduleSystem"/>
/// the previous frame, then writes the vertex/triangle buffers and priority score into ECS
/// components so <see cref="_App.Ace_of_Ages.Terrain.TerrainPhysicsSystem"/> can create
/// <c>MeshCollider</c>s in the same frame.
///
/// Runs in <c>InitializationSystemGroup</c> — immediately after <c>EarlyUpdate.XRUpdate</c>
/// finishes — so <c>Complete()</c> returns almost instantly because workers ran during XRUpdate.
///
/// <c>RequireForUpdate</c> is intentionally omitted so this system always ticks and never leaves
/// a dangling job handle if the world enters a state where singletons are absent.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(BeginInitializationEntityCommandBufferSystem))]
public partial struct TerrainColliderCompleteSystem : ISystem
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.ColliderComplete");
#endif

    public void OnCreate(ref SystemState state)
    {
        // RequireForUpdate intentionally omitted — must always run to drain any in-flight handle.
    }

    public void OnUpdate(ref SystemState state)
    {
        var schedHandle = state.WorldUnmanaged.GetExistingUnmanagedSystem<TerrainColliderScheduleSystem>();
        if (schedHandle == SystemHandle.Null)
            return;

        ref var sched = ref state.WorldUnmanaged.GetUnsafeSystemRef<TerrainColliderScheduleSystem>(schedHandle);

        if (!sched._hasInFlight)
            return;

#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            // Workers ran during XRUpdate — this should return immediately.
            sched._inFlightHandle.Complete();

            int maxVPT = sched._maxVerticesPerTile;
            int maxTPT = sched._maxTrianglesPerTile;

            for (int i = 0; i < sched._inFlightEntities.Length; i++)
            {
                var entity = sched._inFlightEntities[i];
                if (!state.EntityManager.Exists(entity))
                    continue;

                int vCount  = sched._inFlightVertexCounts[i];
                int tCount  = sched._inFlightTriangleCounts[i];
                int vOffset = i * maxVPT;
                int tOffset = i * maxTPT;

                // ── Vertex buffer ──────────────────────────────────────────────────────────────
                DynamicBuffer<ColliderPreparedVertexElement> vBuf;
                if (state.EntityManager.HasBuffer<ColliderPreparedVertexElement>(entity))
                    vBuf = state.EntityManager.GetBuffer<ColliderPreparedVertexElement>(entity);
                else
                    vBuf = state.EntityManager.AddBuffer<ColliderPreparedVertexElement>(entity);

                vBuf.ResizeUninitialized(vCount);
                NativeArray<ColliderPreparedVertexElement>.Copy(
                    sched._inFlightOutVertices, vOffset, vBuf.AsNativeArray(), 0, vCount);

                // ── Triangle buffer ────────────────────────────────────────────────────────────
                DynamicBuffer<ColliderPreparedTriangleElement> tBuf;
                if (state.EntityManager.HasBuffer<ColliderPreparedTriangleElement>(entity))
                    tBuf = state.EntityManager.GetBuffer<ColliderPreparedTriangleElement>(entity);
                else
                    tBuf = state.EntityManager.AddBuffer<ColliderPreparedTriangleElement>(entity);

                tBuf.ResizeUninitialized(tCount);
                NativeArray<ColliderPreparedTriangleElement>.Copy(
                    sched._inFlightOutTriangles, tOffset, tBuf.AsNativeArray(), 0, tCount);

                // ── Priority component ─────────────────────────────────────────────────────────
                var prepared = new PhysicsColliderPrepared { priority = sched._inFlightPriorities[i] };
                if (state.EntityManager.HasComponent<PhysicsColliderPrepared>(entity))
                    state.EntityManager.SetComponentData(entity, prepared);
                else
                    state.EntityManager.AddComponentData(entity, prepared);

                // ── Disable the preparation-request tag ────────────────────────────────────────
                if (state.EntityManager.HasComponent<PhysicsColliderNeedsPreparation>(entity))
                    state.EntityManager.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, false);
            }

            // Dispose all in-flight allocations — ownership transfers back here after Complete().
            TerrainColliderScheduleSystem.DisposeInFlight(ref sched);
        }
    }
}

/// <summary>
/// Singleton ECS component that caches the player's world-space pose each frame.
/// Written once per frame by <see cref="CameraDataUpdateSystem"/> at the start of
/// <see cref="PresentationSystemGroup"/> so that all systems in the following frame's
/// <see cref="InitializationSystemGroup"/> and <see cref="SimulationSystemGroup"/> can
/// read blittable ECS data instead of touching the managed <see cref="UnityEngine.Transform"/>.
/// One frame of latency is intentional and imperceptible for all current consumers.
/// </summary>
public struct CameraDataSingleton : IComponentData
{
    /// <summary>World-space position of the player transform.</summary>
    public float3 position;
    /// <summary>Normalized world-space forward direction projected onto the XZ plane (Y = 0).</summary>
    public float3 forward;
    /// <summary>Full 3D normalized forward direction including Y (sin of pitch angle).</summary>
    public float3 fullForward;
    /// <summary>Player Z Euler angle in degrees (bank/roll), range -180 to 180, for bank-to-turn steering.</summary>
    public float bankAngle;
}

/// <summary>
/// Reads the player's <see cref="UnityEngine.Transform"/> once at the start of
/// <see cref="PresentationSystemGroup"/> and writes all required pose data into
/// <see cref="CameraDataSingleton"/>. Running here (end of frame, OrderFirst) means the
/// singleton holds the current frame's pose when <see cref="TerrainMeshScheduleSystem"/>
/// and <see cref="TerrainColliderScheduleSystem"/> schedule their Burst jobs, and holds last
/// frame's pose for all systems in the following frame's Initialization and Simulation groups —
/// decoupling them from the managed Transform and the <c>EarlyUpdate.XRUpdate</c> dependency.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
public partial class CameraDataUpdateSystem : SystemBase
{
    /// <summary>Creates the <see cref="CameraDataSingleton"/> entity if it does not already exist.</summary>
    protected override void OnCreate()
    {
        if (!SystemAPI.HasSingleton<CameraDataSingleton>())
        {
            EntityManager.CreateEntity(typeof(CameraDataSingleton));
        }
    }

    /// <summary>
    /// Samples the player transform's position, XZ-projected forward, full 3D forward, and
    /// Z Euler bank angle, then writes them all to <see cref="CameraDataSingleton"/>.
    /// Falls back to zero position, +Z forward, and zero bank when no player is tracked.
    /// </summary>
    protected override void OnUpdate()
    {
        float3 position    = float3.zero;
        float3 forward     = new float3(0, 0, 1);
        float3 fullForward = new float3(0, 0, 1);
        float  bankAngle   = 0f;

        if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
            playerRef != null &&
            playerRef.playerTransform != null)
        {
            var tf = playerRef.playerTransform;
            position = tf.position;

            var fwd    = tf.forward;
            fullForward = math.normalize(new float3(fwd.x, fwd.y, fwd.z));
            forward     = math.normalizesafe(new float3(fwd.x, 0f, fwd.z), new float3(0, 0, 1));

            float rawZ = tf.eulerAngles.z;
            bankAngle  = rawZ > 180f ? rawZ - 360f : rawZ;
        }

        SystemAPI.SetSingleton(new CameraDataSingleton
        {
            position    = position,
            forward     = forward,
            fullForward = fullForward,
            bankAngle   = bankAngle
        });
    }
}
