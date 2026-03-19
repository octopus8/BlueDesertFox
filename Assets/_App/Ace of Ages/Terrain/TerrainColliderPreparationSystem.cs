using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
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

    private JobHandle _preparationDependency;
    
    /// <summary>
    /// Public dependency handle for future parallelization of mesh generation.
    /// Future optimization: TerrainMeshGenerationSystem can schedule parallel jobs with .ScheduleParallel() - this dependency will chain automatically.
    /// </summary>
    public JobHandle PreparationDependency => _preparationDependency;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
    }

    // NOTE: Removed [BurstCompile] because we need to access managed PlayerTransformReference
    public void OnUpdate(ref SystemState state)
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            
            // Get player/camera position and forward direction for priority calculation
            float3 cameraPosition = float3.zero;
            float3 cameraForward = new float3(0, 0, 1); // Default forward if no camera
            
            if (SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) &&
                playerRef != null && 
                playerRef.playerTransform != null)
            {
                cameraPosition = playerRef.playerTransform.position;
                cameraForward = math.normalize(new float3(
                    playerRef.playerTransform.forward.x, 
                    playerRef.playerTransform.forward.y, 
                    playerRef.playerTransform.forward.z));
            }
            
            // Schedule job to prepare collider data
            var job = new PrepareColliderDataJob
            {
                verticesPerSide = config.verticesPerSide,
                tileSize = config.tileSize,
                cameraPosition = cameraPosition,
                cameraForward = cameraForward,
                viewDistance = config.viewDistance
            };
            
            // Chain with existing dependency and schedule
            _preparationDependency = job.ScheduleParallel(state.Dependency);
            state.Dependency = _preparationDependency;
        }
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
    public int verticesPerSide;
    public float tileSize;
    public float3 cameraPosition;
    public float3 cameraForward;
    public float viewDistance;
    
    public void Execute(
        Entity entity,
        ref DynamicBuffer<VertexElement> sourceVertices,
        ref DynamicBuffer<IndexElement> sourceIndices,
        ref DynamicBuffer<ColliderPreparedVertexElement> preparedVertices,
        ref DynamicBuffer<ColliderPreparedTriangleElement> preparedTriangles,
        in PhysicsColliderNeedsPreparation needsPrep,
        in TerrainTile tile,
        in TerrainTileDistanceToPlayer distanceData,
        ref PhysicsColliderPrepared prepared,
        EnabledRefRW<PhysicsColliderNeedsPreparation> needsPrepEnabled)
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
                needsPrepEnabled.ValueRW = false;
                return;
        }
        
        // Clear prepared buffers
        preparedVertices.Clear();
        preparedTriangles.Clear();
        
        // Calculate decimated vertex count
        int decimatedVerticesPerSide = (verticesPerSide - 1) / stride + 1;
        int totalDecimatedVertices = decimatedVerticesPerSide * decimatedVerticesPerSide;
        
        preparedVertices.EnsureCapacity(totalDecimatedVertices);
        
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
        
        // Decimate triangles - only keep triangles where all vertices are in the decimated set
        int triangleCount = sourceIndices.Length / 3;
        
        for (int i = 0; i < triangleCount; i++)
        {
            int idx0 = sourceIndices[i * 3].value;
            int idx1 = sourceIndices[i * 3 + 1].value;
            int idx2 = sourceIndices[i * 3 + 2].value;
            
            // Check if all three vertices are in the decimated set
            if (idx0 < vertexIndexMap.Length && idx1 < vertexIndexMap.Length && idx2 < vertexIndexMap.Length &&
                vertexIndexMap[idx0] >= 0 && vertexIndexMap[idx1] >= 0 && vertexIndexMap[idx2] >= 0)
            {
                // Remap to new vertex indices
                int3 remappedTriangle = new int3(
                    vertexIndexMap[idx0],
                    vertexIndexMap[idx1],
                    vertexIndexMap[idx2]
                );
                
                preparedTriangles.Add(new ColliderPreparedTriangleElement 
                { 
                    value = remappedTriangle 
                });
            }
        }
        
        vertexIndexMap.Dispose();
        
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
        prepared.lodLevel = needsPrep.targetLOD;
        prepared.priority = (int)combinedPriority;
        
        // Remove needs preparation flag
        needsPrepEnabled.ValueRW = false;
    }
}
