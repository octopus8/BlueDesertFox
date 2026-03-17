using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that generates procedural terrain meshes using noise functions.
/// Uses parallel Burst-compiled jobs for performance and frame budgeting to prevent stalls.
/// Implements camera-aware prioritization to ensure visible tiles are generated first.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct TerrainMeshGenerationSystem : ISystem
{
    private NativeQueue<Entity> _pendingTiles;
    
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainMesh.Generation");
    private static readonly ProfilerMarker s_JobScheduleMarker = new ProfilerMarker("TerrainMesh.JobSchedule");
    private static readonly ProfilerMarker s_BufferCopyMarker = new ProfilerMarker("TerrainMesh.BufferCopy");
    private static readonly ProfilerMarker s_PrioritySortMarker = new ProfilerMarker("TerrainMesh.PrioritySort");
#endif

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<WorldOriginOffset>();
        
        _pendingTiles = new NativeQueue<Entity>(Allocator.Persistent);
    }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_pendingTiles.IsCreated)
            _pendingTiles.Dispose();
    }

    public void OnUpdate(ref SystemState state)
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            var worldOffset = SystemAPI.GetSingleton<WorldOriginOffset>();
            
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
            
            // Add new tiles that need generation to the queue (ZERO GC ALLOCATIONS)
            foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
                .WithAll<VertexElement>()
                .WithAll<NormalElement>()
                .WithAll<UVElement>()
                .WithAll<IndexElement>()
                .WithEntityAccess())
            {
                if (!tile.ValueRO.meshGenerated || tile.ValueRO.needsRegeneration)
                {
                    _pendingTiles.Enqueue(entity);
                }
            }

            // Process up to maxMeshesPerFrame tiles this frame
            int maxMeshesPerFrame = math.max(1, config.maxCollidersCreatedPerFrame); // Reuse same budget config
            
            // Collect all pending tiles with priority calculation
            var tilesWithPriority = new NativeList<MeshTileWithPriority>(math.min(_pendingTiles.Count, maxMeshesPerFrame * 2), Allocator.Temp);
            var processedEntities = new NativeHashSet<Entity>(_pendingTiles.Count, Allocator.Temp);
            
            while (_pendingTiles.Count > 0)
            {
                var entity = _pendingTiles.Dequeue();
                
                // Skip duplicates
                if (processedEntities.Contains(entity))
                    continue;
                
                // Verify entity still exists and needs processing
                if (state.EntityManager.Exists(entity))
                {
                    var tile = SystemAPI.GetComponent<TerrainTile>(entity);
                    if (!tile.meshGenerated || tile.needsRegeneration)
                    {
                        // Calculate camera-aware priority for this tile
                        float priority = CalculateTilePriority(tile, config, cameraPosition, cameraForward);
                        
                        tilesWithPriority.Add(new MeshTileWithPriority
                        {
                            entity = entity,
                            priority = priority
                        });
                        processedEntities.Add(entity);
                    }
                }
            }
            
            processedEntities.Dispose();
            
            if (tilesWithPriority.Length == 0)
            {
                tilesWithPriority.Dispose();
                return;
            }
            
            // Sort by priority if we have more tiles than budget
            // (Only sort when queue is large to minimize overhead)
#if UNITY_EDITOR
            using (s_PrioritySortMarker.Auto())
#endif
            {
                if (tilesWithPriority.Length > maxMeshesPerFrame)
                {
                    tilesWithPriority.Sort(new TilePriorityComparer());
                }
            }
            
            // Select top priority tiles up to budget
            int tilesToProcessCount = math.min(tilesWithPriority.Length, maxMeshesPerFrame);
            var tilesToProcess = new NativeList<Entity>(tilesToProcessCount, Allocator.Temp);
            
            for (int i = 0; i < tilesToProcessCount; i++)
            {
                tilesToProcess.Add(tilesWithPriority[i].entity);
            }
            
            // Put remaining tiles back in queue for next frame
            for (int i = tilesToProcessCount; i < tilesWithPriority.Length; i++)
            {
                _pendingTiles.Enqueue(tilesWithPriority[i].entity);
            }
            
            tilesWithPriority.Dispose();
            
            // Schedule parallel jobs for mesh generation
#if UNITY_EDITOR
            using (s_JobScheduleMarker.Auto())
#endif
            {
                int verticesPerSide = config.verticesPerSide;
                int totalVertices = verticesPerSide * verticesPerSide;
                int totalTriangles = (verticesPerSide - 1) * (verticesPerSide - 1) * 2;
                int totalIndices = totalTriangles * 3;
                
                // Allocate flat arrays for all tiles (avoid nested containers)
                int totalTileVertices = totalVertices * tilesToProcess.Length;
                int totalTileIndices = totalIndices * tilesToProcess.Length;
                
                var allVertices = new NativeArray<float3>(totalTileVertices, Allocator.TempJob);
                var allNormals = new NativeArray<float3>(totalTileVertices, Allocator.TempJob);
                var allUVs = new NativeArray<float2>(totalTileVertices, Allocator.TempJob);
                var allIndices = new NativeArray<int>(totalTileIndices, Allocator.TempJob);
                var tileDataArray = new NativeArray<TileMeshJobData>(tilesToProcess.Length, Allocator.TempJob);
                
                // Prepare job data
                for (int i = 0; i < tilesToProcess.Length; i++)
                {
                    var entity = tilesToProcess[i];
                    var tile = SystemAPI.GetComponent<TerrainTile>(entity);
                    
                    // Calculate world position for noise sampling
                    double3 tileWorldPos = new double3(
                        tile.gridCoordinate.x * config.tileSize,
                        0,
                        tile.gridCoordinate.y * config.tileSize
                    ) + worldOffset.accumulatedOffset;
                    
                    tileDataArray[i] = new TileMeshJobData
                    {
                        tileWorldPos = tileWorldPos,
                        verticesPerSide = verticesPerSide,
                        tileSize = config.tileSize,
                        noiseFrequency = config.noiseFrequency,
                        noiseAmplitude = config.noiseAmplitude,
                        noiseOctaves = config.noiseOctaves,
                        noiseLacunarity = config.noiseLacunarity,
                        noisePersistence = config.noisePersistence,
                        vertexOffset = i * totalVertices,
                        indexOffset = i * totalIndices
                    };
                }
                
                // Schedule parallel jobs for mesh generation
                var meshGenJob = new GenerateTileMeshJob
                {
                    tileData = tileDataArray,
                    allVertices = allVertices,
                    allNormals = allNormals,
                    allUVs = allUVs,
                    allIndices = allIndices
                };
                
                var jobHandle = meshGenJob.Schedule(tilesToProcess.Length, 1, state.Dependency);
                jobHandle.Complete();
                
                // Copy results back to buffers (must be done on main thread)
#if UNITY_EDITOR
                using (s_BufferCopyMarker.Auto())
#endif
                {
                    for (int i = 0; i < tilesToProcess.Length; i++)
                    {
                        var entity = tilesToProcess[i];
                        ref var tile = ref SystemAPI.GetComponentRW<TerrainTile>(entity).ValueRW;
                        
                        var vertexBuffer = SystemAPI.GetBuffer<VertexElement>(entity);
                        var normalBuffer = SystemAPI.GetBuffer<NormalElement>(entity);
                        var uvBuffer = SystemAPI.GetBuffer<UVElement>(entity);
                        var indexBuffer = SystemAPI.GetBuffer<IndexElement>(entity);
                        
                        // Clear existing data
                        vertexBuffer.Clear();
                        normalBuffer.Clear();
                        uvBuffer.Clear();
                        indexBuffer.Clear();
                        
                        // Reserve capacity for better performance
                        vertexBuffer.EnsureCapacity(totalVertices);
                        normalBuffer.EnsureCapacity(totalVertices);
                        uvBuffer.EnsureCapacity(totalVertices);
                        indexBuffer.EnsureCapacity(totalIndices);
                        
                        // Calculate offset for this tile's data
                        int vertexOffset = i * totalVertices;
                        int indexOffset = i * totalIndices;
                        
                        // Copy from flat NativeArrays to DynamicBuffers
                        for (int v = 0; v < totalVertices; v++)
                        {
                            vertexBuffer.Add(new VertexElement { value = allVertices[vertexOffset + v] });
                            normalBuffer.Add(new NormalElement { value = allNormals[vertexOffset + v] });
                            uvBuffer.Add(new UVElement { value = allUVs[vertexOffset + v] });
                        }
                        
                        for (int idx = 0; idx < totalIndices; idx++)
                        {
                            indexBuffer.Add(new IndexElement { value = allIndices[indexOffset + idx] });
                        }
                        
                        tile.meshGenerated = true;
                        tile.needsRegeneration = false;
                    }
                }
                
                // Cleanup
                allVertices.Dispose();
                allNormals.Dispose();
                allUVs.Dispose();
                allIndices.Dispose();
                tileDataArray.Dispose();
            }
            
            tilesToProcess.Dispose();
        }
    }
    
    /// <summary>
    /// Calculates camera-aware priority for a tile.
    /// Lower values = higher priority (processed first).
    /// </summary>
    private float CalculateTilePriority(TerrainTile tile, TerrainTileConfig config, float3 cameraPosition, float3 cameraForward)
    {
        // Calculate tile center position
        float2 tileCenter = new float2(
            tile.gridCoordinate.x * config.tileSize + config.tileSize * 0.5f,
            tile.gridCoordinate.y * config.tileSize + config.tileSize * 0.5f
        );
        
        // Vector from camera to tile (2D, XZ plane)
        float2 cameraPos2D = new float2(cameraPosition.x, cameraPosition.z);
        float2 toTile = tileCenter - cameraPos2D;
        float distance = math.length(toTile);
        
        // Normalize distance to 0-1 range based on view distance
        float normalizedDistance = math.clamp(distance / config.viewDistance, 0f, 1f);
        
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
        return (1f - viewScore) * 1000f + normalizedDistance * 500f;
    }
}

/// <summary>
/// Data passed to each job for mesh generation.
/// </summary>
[BurstCompile]
public struct TileMeshJobData
{
    public double3 tileWorldPos;
    public int verticesPerSide;
    public float tileSize;
    public float noiseFrequency;
    public float noiseAmplitude;
    public int noiseOctaves;
    public float noiseLacunarity;
    public float noisePersistence;
    public int vertexOffset;  // Offset in flat vertex arrays
    public int indexOffset;   // Offset in flat index array
}

/// <summary>
/// Burst-compiled parallel job that generates mesh data for terrain tiles.
/// Each job processes one tile independently using flat arrays with offsets.
/// </summary>
[BurstCompile]
public struct GenerateTileMeshJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<TileMeshJobData> tileData;
    [NativeDisableParallelForRestriction] public NativeArray<float3> allVertices;
    [NativeDisableParallelForRestriction] public NativeArray<float3> allNormals;
    [NativeDisableParallelForRestriction] public NativeArray<float2> allUVs;
    [NativeDisableParallelForRestriction] public NativeArray<int> allIndices;
    
    public void Execute(int index)
    {
        var data = tileData[index];
        int vertexOffset = data.vertexOffset;
        int indexOffset = data.indexOffset;
        
        float stepSize = data.tileSize / (data.verticesPerSide - 1);
        
        // Generate vertices and UVs
        for (int z = 0; z < data.verticesPerSide; z++)
        {
            for (int x = 0; x < data.verticesPerSide; x++)
            {
                int vertexIndex = z * data.verticesPerSide + x;
                int flatIndex = vertexOffset + vertexIndex;
                
                // Local position within tile
                float localX = x * stepSize;
                float localZ = z * stepSize;
                
                // World position for noise sampling (using double precision)
                double worldX = data.tileWorldPos.x + localX;
                double worldZ = data.tileWorldPos.z + localZ;
                
                // Sample noise at this position
                float height = SampleNoise(worldX, worldZ, data);
                
                // Store vertex position (relative to tile origin)
                allVertices[flatIndex] = new float3(localX, height, localZ);
                
                // Store UV coordinates
                allUVs[flatIndex] = new float2(
                    (float)x / (data.verticesPerSide - 1),
                    (float)z / (data.verticesPerSide - 1)
                );
            }
        }
        
        // Calculate normals
        for (int z = 0; z < data.verticesPerSide; z++)
        {
            for (int x = 0; x < data.verticesPerSide; x++)
            {
                int vertexIndex = z * data.verticesPerSide + x;
                int flatIndex = vertexOffset + vertexIndex;
                
                // Calculate world position for this vertex
                float localX = x * stepSize;
                float localZ = z * stepSize;
                double worldX = data.tileWorldPos.x + localX;
                double worldZ = data.tileWorldPos.z + localZ;
                
                // Calculate normal by sampling neighboring heights directly from noise
                allNormals[flatIndex] = CalculateNormalFromHeightfield(worldX, worldZ, stepSize, data);
            }
        }
        
        // Generate indices (triangles)
        int currentIndexOffset = 0;
        for (int z = 0; z < data.verticesPerSide - 1; z++)
        {
            for (int x = 0; x < data.verticesPerSide - 1; x++)
            {
                int baseIndex = z * data.verticesPerSide + x;
                
                // First triangle
                allIndices[indexOffset + currentIndexOffset++] = baseIndex;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + data.verticesPerSide;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + 1;
                
                // Second triangle
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + 1;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + data.verticesPerSide;
                allIndices[indexOffset + currentIndexOffset++] = baseIndex + data.verticesPerSide + 1;
            }
        }
    }
    
    /// <summary>
    /// Samples multi-octave noise at the given world position.
    /// </summary>
    private static float SampleNoise(double worldX, double worldZ, in TileMeshJobData data)
    {
        float total = 0f;
        float frequency = data.noiseFrequency;
        float amplitude = data.noiseAmplitude;
        float maxValue = 0f;
        
        for (int i = 0; i < data.noiseOctaves; i++)
        {
            // Sample noise using float (converted from double)
            float2 samplePos = new float2((float)worldX, (float)worldZ) * frequency;
            float noiseValue = noise.snoise(samplePos);
            
            total += noiseValue * amplitude;
            maxValue += amplitude;
            
            amplitude *= data.noisePersistence;
            frequency *= data.noiseLacunarity;
        }
        
        return total / maxValue * data.noiseAmplitude;
    }
    
    /// <summary>
    /// Calculates the normal vector by sampling heights from the noise function at neighboring positions.
    /// </summary>
    private static float3 CalculateNormalFromHeightfield(double worldX, double worldZ, float stepSize, in TileMeshJobData data)
    {
        // Sample heights at 4 neighboring positions (cross pattern)
        float heightLeft = SampleNoise(worldX - stepSize, worldZ, data);
        float heightRight = SampleNoise(worldX + stepSize, worldZ, data);
        float heightDown = SampleNoise(worldX, worldZ - stepSize, data);
        float heightUp = SampleNoise(worldX, worldZ + stepSize, data);
        
        // Calculate tangent vectors
        float3 tangentX = new float3(2.0f * stepSize, heightRight - heightLeft, 0);
        float3 tangentZ = new float3(0, heightUp - heightDown, 2.0f * stepSize);
        
        // Normal is cross product of tangents
        float3 normal = math.normalize(math.cross(tangentZ, tangentX));
        
        return normal;
    }
}

/// <summary>
/// Helper struct for storing entity with its calculated priority for mesh generation.
/// </summary>
struct MeshTileWithPriority
{
    public Entity entity;
    public float priority;
}

/// <summary>
/// Comparer for sorting tiles by priority (ascending - lower = higher priority).
/// </summary>
struct TilePriorityComparer : IComparer<MeshTileWithPriority>
{
    public int Compare(MeshTileWithPriority a, MeshTileWithPriority b)
    {
        return a.priority.CompareTo(b.priority);
    }
}
