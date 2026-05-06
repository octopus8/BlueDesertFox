using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that prepares collider data asynchronously using Burst-compiled jobs.
/// Applies LOD decimation to vertex/index data before main-thread MeshCollider.Create() call.
/// Calculates camera-aware priority to ensure tiles visible to camera are processed first.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainMeshGenerationSystem))]
public partial struct TerrainColliderPreparationSystem : ISystem
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.PrepareJob");
#endif

    private EntityQuery _query;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<CameraDataSingleton>(); // Require the new singleton
        _query = state.GetEntityQuery(
            ComponentType.ReadOnly<PhysicsColliderNeedsPreparation>(),
            ComponentType.ReadOnly<VertexElement>(),
            ComponentType.ReadOnly<IndexElement>(),
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<TerrainTileDistanceToPlayer>()
        );
    }

    public void OnUpdate(ref SystemState state)
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            
            // Early exit if physics colliders are disabled
            if (!config.enablePhysicsColliders)
            {
                return;
            }
            
            var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
            
            // Get the ECB singleton and create a command buffer
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Schedule job to prepare collider data
            var job = new PrepareColliderDataJob
            {
                ecb = ecb.AsParallelWriter(),
                verticesPerSide = config.verticesPerSide,
                tileSize = config.tileSize,
                cameraPosition = cameraData.position,
                cameraForward = cameraData.forward,
                viewDistance = config.viewDistance
            };
            
            // Schedule against the filtered query
            state.Dependency = job.ScheduleParallel(_query, state.Dependency);
        }
    }
}

// This new system will run on the main thread to get the managed camera data
// and write it to a Burst-compatible singleton for other systems to use.
public struct CameraDataSingleton : IComponentData
{
    public float3 position;
    public float3 forward;
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TerrainColliderPreparationSystem))]
public partial class CameraDataUpdateSystem : SystemBase
{
    protected override void OnCreate()
    {
        // Ensure the singleton exists
        if (!SystemAPI.HasSingleton<CameraDataSingleton>())
        {
            EntityManager.CreateEntity(typeof(CameraDataSingleton));
        }
    }

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
                0, // Project to XZ plane
                playerRef.playerTransform.forward.z));
        }

        // Write to the singleton
        SystemAPI.SetSingleton(new CameraDataSingleton
        {
            position = cameraPosition,
            forward = cameraForward
        });
    }
}

/// <summary>
/// Burst-compiled job that prepares collider vertex and triangle data with LOD decimation.
/// Runs in parallel for maximum performance.
/// Calculates camera-aware priority: tiles in front of camera get higher priority than tiles behind.
/// </summary>
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
    
    public void Execute(
        [ChunkIndexInQuery] int chunkIndex,
        Entity entity,
        in DynamicBuffer<VertexElement> sourceVertices,
        in DynamicBuffer<IndexElement> sourceIndices,
        in PhysicsColliderNeedsPreparation needsPrep,
        in TerrainTile tile,
        in TerrainTileDistanceToPlayer distanceData)
    {
        // Determine vertex stride based on LOD level
        int stride = 1;
        switch (needsPrep.targetLOD)
        {
            case TerrainPhysicsLODLevel.FullResolution:
                stride = 1;
                break;
            case TerrainPhysicsLODLevel.HalfResolution:
                stride = 2;
                break;
            case TerrainPhysicsLODLevel.QuarterResolution:
                stride = 4;
                break;
            case TerrainPhysicsLODLevel.NoCollider:
                // Should not happen, but handle gracefully
                ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(chunkIndex, entity, false);
                return;
        }
        
        // Use temporary lists to build decimated data
        var preparedVertices = new NativeList<ColliderPreparedVertexElement>(Allocator.Temp);
        var preparedTriangles = new NativeList<ColliderPreparedTriangleElement>(Allocator.Temp);
        
        // Calculate decimated vertex count
        int decimatedVerticesPerSide = (verticesPerSide - 1) / stride + 1;
        
        preparedVertices.Capacity = decimatedVerticesPerSide * decimatedVerticesPerSide;
        
        // Create vertex index mapping for triangle remapping
        var vertexIndexMap = new NativeArray<int>(verticesPerSide * verticesPerSide, Allocator.Temp);
        for (int i = 0; i < vertexIndexMap.Length; i++)
        {
            vertexIndexMap[i] = -1; // Mark as unmapped
        }
        
        // Decimate vertices
        int newVertexIndex = 0;
        for (int z = 0; z < verticesPerSide; z += stride)
        {
            for (int x = 0; x < verticesPerSide; x += stride)
            {
                int sourceIndex = z * verticesPerSide + x;
                
                if (sourceIndex < sourceVertices.Length)
                {
                    preparedVertices.Add(new ColliderPreparedVertexElement 
                    { 
                        value = sourceVertices[sourceIndex].value 
                    });
                    
                    vertexIndexMap[sourceIndex] = newVertexIndex;
                    newVertexIndex++;
                }
            }
        }
        
        // Rebuild triangles based on the decimated grid, instead of trying to preserve old ones.
        // This guarantees a valid mesh and prevents empty triangle buffers.
        decimatedVerticesPerSide = (verticesPerSide - 1) / stride + 1;
        for (int z = 0; z < decimatedVerticesPerSide - 1; z++)
        {
            for (int x = 0; x < decimatedVerticesPerSide - 1; x++)
            {
                int i0 = z * decimatedVerticesPerSide + x;
                int i1 = z * decimatedVerticesPerSide + x + 1;
                int i2 = (z + 1) * decimatedVerticesPerSide + x;
                int i3 = (z + 1) * decimatedVerticesPerSide + x + 1;

                // First triangle
                preparedTriangles.Add(new ColliderPreparedTriangleElement { value = new int3(i2, i1, i0) });
                // Second triangle
                preparedTriangles.Add(new ColliderPreparedTriangleElement { value = new int3(i2, i3, i1) });
            }
        }
        
        vertexIndexMap.Dispose();
        
        // Add the prepared buffers to the entity
        ecb.AddBuffer<ColliderPreparedVertexElement>(chunkIndex, entity).CopyFrom(preparedVertices.AsArray());
        ecb.AddBuffer<ColliderPreparedTriangleElement>(chunkIndex, entity).CopyFrom(preparedTriangles.AsArray());
        
        preparedVertices.Dispose();
        preparedTriangles.Dispose();
        
        // Calculate camera-aware priority
        // Lower priority value = higher actual priority (processed first)
        
        // Calculate tile center position
        float2 tileCenter = new float2(
            tile.gridCoordinate.x * tileSize + tileSize * 0.5f,
            tile.gridCoordinate.y * tileSize + tileSize * 0.5f
        );
        
        // Vector from camera to tile (2D, XZ plane)
        float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);
        float2 toTile = tileCenter - cameraPos2D;
        float distance = math.length(toTile);
        
        // Normalize distance to 0-1 range based on view distance
        float normalizedDistance = math.clamp(distance / viewDistance, 0f, 1f);
        
        // Calculate dot product with camera forward (2D projection)
        float2 cameraForward2D = math.normalize(new float2(cameraForward.x, cameraForward.z));
        float2 toTileNormalized = math.normalize(toTile);
        if (math.lengthsq(toTile) < 0.001f) toTileNormalized = cameraForward2D; // Avoid NaN if tile is at camera center
        float dotProduct = math.dot(cameraForward2D, toTileNormalized);
        
        // Convert dot product from [-1, 1] to [0, 1] where:
        // 1.0 = directly in front
        // 0.5 = perpendicular
        // 0.0 = behind camera
        float viewScore = (dotProduct + 1f) * 0.5f;
        
        // Combined priority: weight view direction more heavily than distance
        // Formula: priority = (1 - viewScore) * 1000 + normalizedDistance * 500
        // This means:
        // - Tiles in front of camera (viewScore=1.0) get priority 0-500 (based on distance)
        // - Tiles behind camera (viewScore=0.0) get priority 1000-1500 (based on distance)
        // - Closer tiles within same viewing direction get higher priority
        float combinedPriority = (1f - viewScore) * 1000f + normalizedDistance * 500f;
        
        // Mark as prepared with camera-aware priority
        ecb.AddComponent(chunkIndex, entity, new PhysicsColliderPrepared
        {
            lodLevel = needsPrep.targetLOD,
            priority = (int)combinedPriority
        });
        
        // Remove needs preparation flag
        ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(chunkIndex, entity, false);
    }
}
