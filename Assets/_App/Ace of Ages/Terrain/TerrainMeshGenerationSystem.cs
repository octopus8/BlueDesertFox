using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// System that generates procedural terrain meshes using noise functions.
/// Processes tiles that need mesh generation in jobs for performance.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct TerrainMeshGenerationSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<WorldOriginOffset>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        var worldOffset = SystemAPI.GetSingleton<WorldOriginOffset>();
        
        // Use entity query to iterate through tiles
        var entityQuery = SystemAPI.QueryBuilder()
            .WithAll<TerrainTile, VertexElement, NormalElement, UVElement, IndexElement>()
            .Build();
        
        var entities = entityQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        int processedCount = 0;
        
        foreach (var entity in entities)
        {
            ref var tile = ref SystemAPI.GetComponentRW<TerrainTile>(entity).ValueRW;
            
            if (!tile.meshGenerated || tile.needsRegeneration)
            {
                var vertexBuffer = SystemAPI.GetBuffer<VertexElement>(entity);
                var normalBuffer = SystemAPI.GetBuffer<NormalElement>(entity);
                var uvBuffer = SystemAPI.GetBuffer<UVElement>(entity);
                var indexBuffer = SystemAPI.GetBuffer<IndexElement>(entity);
                
                UnityEngine.Debug.Log($"[TerrainMeshGen] Generating mesh for tile at {tile.gridCoordinate}");
                
                GenerateTileMesh(
                    ref tile,
                    vertexBuffer,
                    normalBuffer,
                    uvBuffer,
                    indexBuffer,
                    config,
                    worldOffset
                );
                
                processedCount++;
            }
        }
        
        if (processedCount > 0)
        {
            UnityEngine.Debug.Log($"[TerrainMeshGen] Generated {processedCount} tile meshes this frame");
        }
        
        entities.Dispose();
    }

    /// <summary>
    /// Generates mesh data for a single terrain tile using procedural noise.
    /// </summary>
    private void GenerateTileMesh(
        ref TerrainTile tile,
        DynamicBuffer<VertexElement> vertexBuffer,
        DynamicBuffer<NormalElement> normalBuffer,
        DynamicBuffer<UVElement> uvBuffer,
        DynamicBuffer<IndexElement> indexBuffer,
        TerrainTileConfig config,
        WorldOriginOffset worldOffset)
    {
        int verticesPerSide = config.verticesPerSide;
        int totalVertices = verticesPerSide * verticesPerSide;
        int totalTriangles = (verticesPerSide - 1) * (verticesPerSide - 1) * 2;
        int totalIndices = totalTriangles * 3;
        
        // Clear existing data
        vertexBuffer.Clear();
        normalBuffer.Clear();
        uvBuffer.Clear();
        indexBuffer.Clear();
        
        // Reserve capacity
        vertexBuffer.EnsureCapacity(totalVertices);
        normalBuffer.EnsureCapacity(totalVertices);
        uvBuffer.EnsureCapacity(totalVertices);
        indexBuffer.EnsureCapacity(totalIndices);
        
        // Calculate world position for noise sampling (using accumulated offset)
        double3 tileWorldPos = new double3(
            tile.gridCoordinate.x * config.tileSize,
            0,
            tile.gridCoordinate.y * config.tileSize
        ) + worldOffset.accumulatedOffset;
        
        // Use NativeArray for intermediate storage (can be written in parallel if needed)
        var vertices = new NativeArray<float3>(totalVertices, Allocator.Temp);
        var normals = new NativeArray<float3>(totalVertices, Allocator.Temp);
        var uvs = new NativeArray<float2>(totalVertices, Allocator.Temp);
        
        // Generate vertices
        float stepSize = config.tileSize / (verticesPerSide - 1);
        
        for (int z = 0; z < verticesPerSide; z++)
        {
            for (int x = 0; x < verticesPerSide; x++)
            {
                int index = z * verticesPerSide + x;
                
                // Local position within tile
                float localX = x * stepSize;
                float localZ = z * stepSize;
                
                // World position for noise sampling (using double precision)
                double worldX = tileWorldPos.x + localX;
                double worldZ = tileWorldPos.z + localZ;
                
                // Sample noise at this position
                float height = SampleNoise(worldX, worldZ, config);
                
                // Store vertex position (relative to tile origin)
                vertices[index] = new float3(localX, height, localZ);
                
                // Store UV coordinates
                uvs[index] = new float2(
                    (float)x / (verticesPerSide - 1),
                    (float)z / (verticesPerSide - 1)
                );
            }
        }
        
        // Calculate normals
        for (int z = 0; z < verticesPerSide; z++)
        {
            for (int x = 0; x < verticesPerSide; x++)
            {
                int index = z * verticesPerSide + x;
                
                // Calculate world position for this vertex
                float localX = x * stepSize;
                float localZ = z * stepSize;
                double worldX = tileWorldPos.x + localX;
                double worldZ = tileWorldPos.z + localZ;
                
                // Calculate normal by sampling neighboring heights directly from noise
                // This ensures correct normals even at tile edges
                normals[index] = CalculateNormalFromHeightfield(
                    worldX, worldZ, stepSize, config);
            }
        }
        
        // Copy to buffers
        for (int i = 0; i < totalVertices; i++)
        {
            vertexBuffer.Add(new VertexElement { value = vertices[i] });
            normalBuffer.Add(new NormalElement { value = normals[i] });
            uvBuffer.Add(new UVElement { value = uvs[i] });
        }
        
        // Generate indices (triangles)
        for (int z = 0; z < verticesPerSide - 1; z++)
        {
            for (int x = 0; x < verticesPerSide - 1; x++)
            {
                int baseIndex = z * verticesPerSide + x;
                
                // First triangle
                indexBuffer.Add(new IndexElement { value = baseIndex });
                indexBuffer.Add(new IndexElement { value = baseIndex + verticesPerSide });
                indexBuffer.Add(new IndexElement { value = baseIndex + 1 });
                
                // Second triangle
                indexBuffer.Add(new IndexElement { value = baseIndex + 1 });
                indexBuffer.Add(new IndexElement { value = baseIndex + verticesPerSide });
                indexBuffer.Add(new IndexElement { value = baseIndex + verticesPerSide + 1 });
            }
        }
        
        vertices.Dispose();
        normals.Dispose();
        uvs.Dispose();
        
        tile.meshGenerated = true;
        tile.needsRegeneration = false;
        
        UnityEngine.Debug.Log($"[TerrainMeshGen] ✓ Mesh generated: {totalVertices} vertices, {totalTriangles} triangles for tile at {tile.gridCoordinate}");
    }

    /// <summary>
    /// Samples multi-octave noise at the given world position.
    /// Uses the accumulated world offset to maintain consistency across origin shifts.
    /// </summary>
    [BurstCompile]
    private static float SampleNoise(double worldX, double worldZ, TerrainTileConfig config)
    {
        float total = 0f;
        float frequency = config.noiseFrequency;
        float amplitude = config.noiseAmplitude;
        float maxValue = 0f;
        
        for (int i = 0; i < config.noiseOctaves; i++)
        {
            // Sample noise using float (converted from double)
            float2 samplePos = new float2((float)worldX, (float)worldZ) * frequency;
            float noiseValue = noise.snoise(samplePos);
            
            total += noiseValue * amplitude;
            maxValue += amplitude;
            
            amplitude *= config.noisePersistence;
            frequency *= config.noiseLacunarity;
        }
        
        return total / maxValue * config.noiseAmplitude;
    }

    /// <summary>
    /// Calculates the normal vector by sampling heights from the noise function at neighboring positions.
    /// This approach works correctly even at tile edges since it can sample beyond the current tile.
    /// </summary>
    [BurstCompile]
    private static float3 CalculateNormalFromHeightfield(
        double worldX, double worldZ, float stepSize, TerrainTileConfig config)
    {
        // Sample heights at 4 neighboring positions (cross pattern)
        float heightLeft = SampleNoise(worldX - stepSize, worldZ, config);
        float heightRight = SampleNoise(worldX + stepSize, worldZ, config);
        float heightDown = SampleNoise(worldX, worldZ - stepSize, config);
        float heightUp = SampleNoise(worldX, worldZ + stepSize, config);
        
        // Calculate tangent vectors
        float3 tangentX = new float3(2.0f * stepSize, heightRight - heightLeft, 0);
        float3 tangentZ = new float3(0, heightUp - heightDown, 2.0f * stepSize);
        
        // Normal is cross product of tangents
        float3 normal = math.normalize(math.cross(tangentZ, tangentX));
        
        return normal;
    }
}



