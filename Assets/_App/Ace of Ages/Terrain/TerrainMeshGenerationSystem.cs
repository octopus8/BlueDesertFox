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

    /// <summary>
    /// Allocates the pending-tile priority queue and registers the <see cref="TerrainTileConfig"/>
    /// singleton requirement.
    /// </summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        
        _pendingTiles = new NativeQueue<Entity>(Allocator.Persistent);
    }
    
    /// <summary>Disposes the pending-tile native queue and frees all associated memory.</summary>
    [BurstCompile]
    public void OnDestroy(ref SystemState state)
    {
        if (_pendingTiles.IsCreated)
            _pendingTiles.Dispose();
    }

    /// <summary>
    /// Queues tiles that need mesh generation, sorts them by camera-aware priority (closer and
    /// more forward-facing tiles first), and processes up to <c>maxCollidersCreatedPerFrame</c>
    /// tiles per frame using parallel Burst-compiled <see cref="GenerateTileMeshJob"/> jobs.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            
            // Early exit if rendering is disabled - no need to generate meshes
            if (!config.renderTerrain)
            {
                return;
            }
            
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
                
                // Read trail config once (same for all tiles this frame)
                bool trailEnabled = false;
                float trailWidth = 0f, trailBlendWidth = 0f, trailHeight = 0f;
                float trailSeed = 0f, trailFrequency = 0f, trailAmplitude = 0f;
                if (SystemAPI.HasSingleton<TrailConfig>())
                {
                    var trail = SystemAPI.GetSingleton<TrailConfig>();
                    trailEnabled = trail.enabled;
                    trailWidth = trail.width;
                    trailBlendWidth = trail.blendWidth;
                    trailHeight = trail.height;
                    trailSeed = trail.seed;
                    trailFrequency = trail.frequency;
                    trailAmplitude = trail.amplitude;
                }

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
                    );
                    
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
                        continentalFrequency = config.continentalFrequency,
                        continentalExponent = config.continentalExponent,
                        vertexOffset = i * totalVertices,
                        indexOffset = i * totalIndices,
                        trailEnabled = trailEnabled,
                        trailWidth = trailWidth,
                        trailBlendWidth = trailBlendWidth,
                        trailHeight = trailHeight,
                        trailSeed = trailSeed,
                        trailFrequency = trailFrequency,
                        trailAmplitude = trailAmplitude
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
                        
                        // CRITICAL: Remove StaticObjectsSpawned tag so objects can respawn on regenerated mesh
                        // This also cleans up old object references
                        if (state.EntityManager.HasComponent<StaticObjectsSpawned>(entity))
                        {
                            state.EntityManager.RemoveComponent<StaticObjectsSpawned>(entity);
#if UNITY_EDITOR
                            UnityEngine.Debug.Log($"[TerrainMesh] Removed StaticObjectsSpawned tag from regenerated tile {tile.gridCoordinate}");
#endif
                        }
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
    public float continentalFrequency;
    public float continentalExponent;
    public int vertexOffset;  // Offset in flat vertex arrays
    public int indexOffset;   // Offset in flat index array

    // Trail parameters
    public bool trailEnabled;
    public float trailWidth;
    public float trailBlendWidth;
    public float trailHeight;
    public float trailSeed;
    public float trailFrequency;
    public float trailAmplitude;
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
    
    /// <summary>
    /// Generates vertices, normals, UVs, and triangle indices for one terrain tile at <paramref name="index"/>
    /// using multi-octave Perlin noise, writing output into pre-allocated shared native arrays at the
    /// tile's pre-computed vertex and index offsets.
    /// </summary>
    public void Execute(int index)
    {
        var data = tileData[index];
        int vertexOffset = data.vertexOffset;
        int indexOffset = data.indexOffset;
        
        float stepSize = data.tileSize / (data.verticesPerSide - 1);
        float halfTileSize = data.tileSize * 0.5f;
        
        // Generate vertices and UVs
        for (int z = 0; z < data.verticesPerSide; z++)
        {
            for (int x = 0; x < data.verticesPerSide; x++)
            {
                int vertexIndex = z * data.verticesPerSide + x;
                int flatIndex = vertexOffset + vertexIndex;
                
                // Local position within tile (0 to tileSize)
                float localX = x * stepSize;
                float localZ = z * stepSize;
                
                // World position for noise sampling (using double precision)
                double worldX = data.tileWorldPos.x + localX;
                double worldZ = data.tileWorldPos.z + localZ;
                
                // Sample noise at this position
                float height = SampleNoise(worldX, worldZ, data);
                
                // Store vertex position (relative to tile center, not corner)
                // Offset by -halfTileSize so vertices are centered around tile transform
                allVertices[flatIndex] = new float3(localX - halfTileSize, height, localZ - halfTileSize);
                
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
                allNormals[flatIndex] = CalculateNormalFromHeightfield(worldX, worldZ, stepSize, data);            }
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
    /// A continental mask (very low-frequency noise raised to a power) scales the amplitude
    /// so that flat plains and tall mountains coexist naturally.
    /// </summary>
    private static float SampleNoise(double worldX, double worldZ, in TileMeshJobData data)
    {
        // Continental mask: single low-frequency sample remapped to [0,1], then curved.
        // Values near 0 → flat plains; values near 1 → full mountain amplitude.
        float continentalMask = 1f;
        if (data.continentalFrequency > 0f && data.continentalExponent > 0f)
        {
            float2 continentalPos = new float2((float)worldX, (float)worldZ) * data.continentalFrequency;
            float rawContinent = noise.snoise(continentalPos) * 0.5f + 0.5f; // [0, 1]
            continentalMask = math.pow(rawContinent, data.continentalExponent);
        }

        float total = 0f;
        float frequency = data.noiseFrequency;
        float amplitude = data.noiseAmplitude;
        float maxValue = 0f;
        
        for (int i = 0; i < data.noiseOctaves; i++)
        {
            float2 samplePos = new float2((float)worldX, (float)worldZ) * frequency;
            float noiseValue = noise.snoise(samplePos);
            
            total += noiseValue * amplitude;
            maxValue += amplitude;
            
            amplitude *= data.noisePersistence;
            frequency *= data.noiseLacunarity;
        }
        
        float terrainHeight = total / maxValue * data.noiseAmplitude * continentalMask;

        if (data.trailEnabled)
        {
            float halfWidth = data.trailWidth * 0.5f;
            float fX = (float)worldX;
            float fZ = (float)worldZ;

            // Find the true minimum 2D distance from this vertex to the trail centerline.
            // The previous single-Z tangent approach measured distance to the tangent LINE
            // at the vertex's own Z, which is correct for a straight trail but leaves
            // inside-of-bend gaps where the nearest trail point is at a different Z.
            // Sweeping ±searchRange along Z and taking the minimum Euclidean distance
            // to any sampled centre point closes all bend gaps regardless of curvature.
            const int kSearchSamples = 9;
            float searchRange = halfWidth + data.trailBlendWidth;
            float minDist2D = float.MaxValue;
            for (int si = 0; si < kSearchSamples; si++)
            {
                float t = si / (float)(kSearchSamples - 1); // 0..1
                float sz  = fZ + math.lerp(-searchRange, searchRange, t);
                float scx = data.trailAmplitude * noise.snoise(new float2(sz * data.trailFrequency + data.trailSeed, 0f));
                float dx  = fX - scx;
                float dz  = fZ - sz;
                float d2  = dx * dx + dz * dz;
                if (d2 < minDist2D) minDist2D = d2;
            }
            float minDist = math.sqrt(minDist2D);

            if (minDist < halfWidth)
                return data.trailHeight;

            if (minDist < halfWidth + data.trailBlendWidth)
                return math.lerp(data.trailHeight, terrainHeight,
                    math.smoothstep(halfWidth, halfWidth + data.trailBlendWidth, minDist));
        }

        return terrainHeight;
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
