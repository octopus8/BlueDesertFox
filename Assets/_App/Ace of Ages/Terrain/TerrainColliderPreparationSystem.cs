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

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            
            // Schedule job to prepare collider data
            var job = new PrepareColliderDataJob
            {
                verticesPerSide = config.verticesPerSide
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
/// </summary>
[BurstCompile]
[WithAll(typeof(PhysicsColliderNeedsPreparation))]
partial struct PrepareColliderDataJob : IJobEntity
{
    public int verticesPerSide;
    
    public void Execute(
        Entity entity,
        ref DynamicBuffer<VertexElement> sourceVertices,
        ref DynamicBuffer<IndexElement> sourceIndices,
        ref DynamicBuffer<ColliderPreparedVertexElement> preparedVertices,
        ref DynamicBuffer<ColliderPreparedTriangleElement> preparedTriangles,
        in PhysicsColliderNeedsPreparation needsPrep,
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
        
        // Mark as prepared with priority based on distance
        prepared.lodLevel = needsPrep.targetLOD;
        prepared.priority = (int)distanceData.distance;
        
        // Remove needs preparation flag
        needsPrepEnabled.ValueRW = false;
    }
}

