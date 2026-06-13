using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that prepares collider data asynchronously using Burst-compiled jobs.
/// Applies distance-based vertex decimation to reduce MeshCollider.Create cost on distant tiles.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainMeshGenerationSystem))]
public partial struct TerrainColliderPreparationSystem : ISystem
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.PrepareJob");
#endif

    private EntityQuery _query;

    /// <summary>
    /// Builds the tile query for entities with <c>PhysicsColliderNeedsPreparation</c> enabled,
    /// and registers required singletons (<see cref="TerrainTileConfig"/>,
    /// <see cref="EndSimulationEntityCommandBufferSystem.Singleton"/>, and <see cref="CameraDataSingleton"/>).
    /// </summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<CameraDataSingleton>();
        _query = state.GetEntityQuery(
            ComponentType.ReadOnly<PhysicsColliderNeedsPreparation>(),
            ComponentType.ReadOnly<VertexElement>(),
            ComponentType.ReadOnly<IndexElement>(),
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<TerrainTileDistanceToPlayer>()
        );
    }

    /// <summary>
    /// Schedules the Burst-compiled <c>PrepareColliderDataJob</c> in parallel for all tiles that
    /// need collider preparation, applying distance-based vertex decimation based on camera proximity
    /// and writing the prepared buffers via <see cref="EntityCommandBuffer"/>.
    /// Skips processing if physics colliders are disabled in <see cref="TerrainTileConfig"/>.
    /// </summary>
    public void OnUpdate(ref SystemState state)
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

            var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            var job = new PrepareColliderDataJob
            {
                ecb = ecb.AsParallelWriter(),
                verticesPerSide = config.verticesPerSide,
                tileSize = config.tileSize,
                cameraPosition = cameraData.position,
                cameraForward = cameraData.forward,
                viewDistance = config.viewDistance,
                physicsColliderFullResolutionDistance = config.physicsColliderFullResolutionDistance,
                physicsColliderVertexStride = math.max(1, config.physicsColliderVertexStride)
            };

            state.Dependency = job.ScheduleParallel(_query, state.Dependency);
        }
    }
}

/// <summary>
/// Singleton ECS component that holds the camera's current world-space position and forward direction.
/// Updated each frame by <see cref="CameraDataUpdateSystem"/> from the player's <see cref="Transform"/>.
/// Used by <see cref="TerrainColliderPreparationSystem"/> to compute camera-aware collider priority scores.
/// </summary>
public struct CameraDataSingleton : IComponentData
{
    /// <summary>World-space position of the camera (or player transform) this frame.</summary>
    public float3 position;
    /// <summary>Normalized world-space forward direction of the camera this frame.</summary>
    public float3 forward;
}

/// <summary>
/// Reads the player's <see cref="Transform"/> position and forward vector from <see cref="PlayerTransformReference"/>
/// each frame and writes them to the <see cref="CameraDataSingleton"/> so that Burst-compiled jobs can access
/// camera data without managed object references. Runs before <see cref="TerrainColliderPreparationSystem"/>.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TerrainColliderPreparationSystem))]
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
    /// Reads the player transform's position and XZ-projected forward direction and writes them
    /// to the <see cref="CameraDataSingleton"/>. Falls back to zero position and +Z forward when
    /// no player is tracked.
    /// </summary>
    protected override void OnUpdate()
    {
        float3 cameraPosition = float3.zero;
        float3 cameraForward = new float3(0, 0, 1);

        if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
            playerRef != null &&
            playerRef.playerTransform != null)
        {
            cameraPosition = playerRef.playerTransform.position;
            cameraForward = math.normalize(new float3(
                playerRef.playerTransform.forward.x,
                0,
                playerRef.playerTransform.forward.z));
        }

        SystemAPI.SetSingleton(new CameraDataSingleton
        {
            position = cameraPosition,
            forward = cameraForward
        });
    }
}

[BurstCompile]
[WithAll(typeof(PhysicsColliderNeedsPreparation))]
partial struct PrepareColliderDataJob : IJobEntity
{
    public EntityCommandBuffer.ParallelWriter ecb;
    public int verticesPerSide;
    public float tileSize;
    public float3 cameraPosition;
    public float3 cameraForward;
    public float viewDistance;
    public float physicsColliderFullResolutionDistance;
    public int physicsColliderVertexStride;

    /// <summary>
    /// Determines the vertex stride based on the tile's distance to the player, calls
    /// <see cref="ColliderMeshDecimation.BuildDecimatedMesh"/> to produce a simplified vertex/triangle
    /// buffer, and adds the output components to the tile entity via ECB for physics collider creation.
    /// </summary>
    public void Execute(
        [ChunkIndexInQuery] int chunkIndex,
        Entity entity,
        in DynamicBuffer<VertexElement> sourceVertices,
        in DynamicBuffer<IndexElement> sourceIndices,
        in TerrainTile tile,
        in TerrainTileDistanceToPlayer distanceData)
    {
        int stride = distanceData.distance <= physicsColliderFullResolutionDistance
            ? 1
            : physicsColliderVertexStride;

        ColliderMeshDecimation.BuildDecimatedMesh(
            sourceVertices,
            sourceIndices,
            verticesPerSide,
            stride,
            out NativeList<ColliderPreparedVertexElement> preparedVertices,
            out NativeList<ColliderPreparedTriangleElement> preparedTriangles);

        ecb.AddBuffer<ColliderPreparedVertexElement>(chunkIndex, entity).CopyFrom(preparedVertices.AsArray());
        ecb.AddBuffer<ColliderPreparedTriangleElement>(chunkIndex, entity).CopyFrom(preparedTriangles.AsArray());

        preparedVertices.Dispose();
        preparedTriangles.Dispose();

        float2 tileCenter = new float2(
            tile.gridCoordinate.x * tileSize + tileSize * 0.5f,
            tile.gridCoordinate.y * tileSize + tileSize * 0.5f
        );

        float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);
        float2 toTile = tileCenter - cameraPos2D;
        float distance = math.length(toTile);
        float normalizedDistance = math.clamp(distance / viewDistance, 0f, 1f);

        float2 cameraForward2D = math.normalize(new float2(cameraForward.x, cameraForward.z));
        float2 toTileNormalized = math.lengthsq(toTile) < 0.001f
            ? cameraForward2D
            : math.normalize(toTile);
        float dotProduct = math.dot(cameraForward2D, toTileNormalized);
        float viewScore = (dotProduct + 1f) * 0.5f;
        float combinedPriority = (1f - viewScore) * 1000f + normalizedDistance * 500f;

        ecb.AddComponent(chunkIndex, entity, new PhysicsColliderPrepared
        {
            priority = (int)combinedPriority
        });

        ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(chunkIndex, entity, false);
    }
}

/// <summary>
/// Burst-compiled utility for building a decimated collider mesh from a terrain tile's vertex buffer.
/// Produces a lower-resolution mesh by sampling every <c>stride</c>-th vertex in the grid,
/// reducing the polygon count for distant tiles to lower <c>MeshCollider.Create</c> cost.
/// </summary>
[BurstCompile]
static class ColliderMeshDecimation
{
    /// <summary>
    /// Decimates the source vertex and index buffers using the given vertex <paramref name="stride"/>,
    /// writing the resulting vertices and triangles into newly allocated <see cref="NativeList{T}"/> outputs.
    /// The caller is responsible for disposing both output lists.
    /// </summary>
    /// <param name="sourceVertices">Full-resolution vertex buffer from the terrain tile.</param>
    /// <param name="sourceIndices">Full-resolution index buffer from the terrain tile.</param>
    /// <param name="verticesPerSide">Number of vertices per edge of the square tile grid.</param>
    /// <param name="stride">Vertex sampling stride (1 = full resolution, 2 = half, etc.).</param>
    /// <param name="preparedVertices">Output list of decimated vertex positions (Temp allocated).</param>
    /// <param name="preparedTriangles">Output list of decimated triangle indices (Temp allocated).</param>
    [BurstCompile]
    public static void BuildDecimatedMesh(
        in DynamicBuffer<VertexElement> sourceVertices,
        in DynamicBuffer<IndexElement> sourceIndices,
        int verticesPerSide,
        int stride,
        out NativeList<ColliderPreparedVertexElement> preparedVertices,
        out NativeList<ColliderPreparedTriangleElement> preparedTriangles)
    {
        stride = math.max(1, stride);

        if (stride == 1)
        {
            preparedVertices = new NativeList<ColliderPreparedVertexElement>(sourceVertices.Length, Allocator.Temp);
            preparedTriangles = new NativeList<ColliderPreparedTriangleElement>(sourceIndices.Length / 3, Allocator.Temp);

            for (int i = 0; i < sourceVertices.Length; i++)
            {
                preparedVertices.Add(new ColliderPreparedVertexElement { value = sourceVertices[i].value });
            }

            for (int i = 0; i + 2 < sourceIndices.Length; i += 3)
            {
                preparedTriangles.Add(new ColliderPreparedTriangleElement
                {
                    value = new int3(
                        sourceIndices[i].value,
                        sourceIndices[i + 1].value,
                        sourceIndices[i + 2].value)
                });
            }

            return;
        }

        int decimatedPerSide = (verticesPerSide - 1) / stride + 1;
        int decimatedVertexCount = decimatedPerSide * decimatedPerSide;
        int decimatedTriangleCount = (decimatedPerSide - 1) * (decimatedPerSide - 1) * 2;

        preparedVertices = new NativeList<ColliderPreparedVertexElement>(decimatedVertexCount, Allocator.Temp);
        preparedTriangles = new NativeList<ColliderPreparedTriangleElement>(decimatedTriangleCount, Allocator.Temp);

        for (int z = 0; z < decimatedPerSide; z++)
        {
            int srcZ = math.min(z * stride, verticesPerSide - 1);
            for (int x = 0; x < decimatedPerSide; x++)
            {
                int srcX = math.min(x * stride, verticesPerSide - 1);
                int srcIndex = srcZ * verticesPerSide + srcX;
                preparedVertices.Add(new ColliderPreparedVertexElement { value = sourceVertices[srcIndex].value });
            }
        }

        for (int z = 0; z < decimatedPerSide - 1; z++)
        {
            for (int x = 0; x < decimatedPerSide - 1; x++)
            {
                int i = z * decimatedPerSide + x;
                preparedTriangles.Add(new ColliderPreparedTriangleElement
                {
                    value = new int3(i, i + decimatedPerSide, i + 1)
                });
                preparedTriangles.Add(new ColliderPreparedTriangleElement
                {
                    value = new int3(i + 1, i + decimatedPerSide, i + decimatedPerSide + 1)
                });
            }
        }
    }
}
