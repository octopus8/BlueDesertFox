# Extension Guide - Adding Custom Features

Guide for extending the terrain system with custom features like biomes, rendering LOD, terrain modification, and more.

## Extension Categories

- [Biome System](#biome-system)
- [Rendering LOD](#rendering-lod)
- [Terrain Modification](#terrain-modification)
- [Procedural Objects](#procedural-objects)
- [Custom Materials](#custom-materials)
- [Water/Liquids](#water-system)
- [Minimap Integration](#minimap)

---

## Biome System

Add multiple terrain types (grass, desert, snow) based on position or noise.

### Implementation Strategy

**Step 1**: Add Biome Component

```csharp
public enum BiomeType : byte
{
    Grassland,
    Desert,
    Snow,
    Rocky
}

public struct TerrainBiome : IComponentData
{
    public BiomeType biomeType;
    public float temperature;  // -1 to +1
    public float moisture;     // 0 to 1
}
```

**Step 2**: Determine Biome in Generation

```csharp
// In mesh generation job
BiomeType DetermineBiome(float2 worldPosition)
{
    float tempNoise = noise.cnoise(worldPosition * 0.001f);
    float moistNoise = noise.cnoise(worldPosition * 0.001f + 1000f);
    
    if (tempNoise < -0.3f) return BiomeType.Snow;
    if (moistNoise < 0.3f) return BiomeType.Desert;
    if (tempNoise > 0.3f) return BiomeType.Rocky;
    return BiomeType.Grassland;
}
```

**Step 3**: Apply Biome to Generation

```csharp
// Modify height based on biome
switch (biome)
{
    case BiomeType.Desert:
        height *= 0.5f; // Flatter terrain
        break;
    case BiomeType.Snow:
        height *= 1.5f; // More mountainous
        break;
}
```

**Step 4**: Multi-Material Rendering

```csharp
// In rendering system, assign material by biome
Material GetMaterialForBiome(BiomeType biome)
{
    return biome switch
    {
        BiomeType.Grassland => grassMaterial,
        BiomeType.Desert => desertMaterial,
        BiomeType.Snow => snowMaterial,
        BiomeType.Rocky => rockyMaterial,
        _ => defaultMaterial
    };
}
```

**Note**: Multiple materials breaks batching - use texture atlas instead for better performance.

---

## Rendering LOD

Add mesh LOD system separate from physics LOD.

### Implementation Strategy

**Step 1**: Add Rendering LOD Component

```csharp
public enum RenderingLODLevel : byte
{
    High,
    Medium,
    Low,
    VeryLow
}

public struct TerrainRenderingLOD : IComponentData
{
    public RenderingLODLevel currentLOD;
    public float distanceToCamera;
}
```

**Step 2**: Create LOD System

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainMeshGenerationSystem))]
public partial class TerrainRenderingLODSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        float3 cameraPos = playerRef.playerTransform.position;
        
        foreach (var (tile, lodComp, entity) in 
            SystemAPI.Query<RefRO<TerrainTile>, RefRW<TerrainRenderingLOD>>()
            .WithEntityAccess())
        {
            float distance = CalculateDistance(tile, cameraPos);
            RenderingLODLevel newLOD = DetermineLOD(distance);
            
            if (newLOD != lodComp.ValueRO.currentLOD)
            {
                lodComp.ValueRW.currentLOD = newLOD;
                RegenerateMeshWithLOD(entity, newLOD);
            }
        }
    }
}
```

**Step 3**: Generate Different Mesh Resolutions

```csharp
void RegenerateMeshWithLOD(Entity entity, RenderingLODLevel lod)
{
    int verticesPerSide = lod switch
    {
        RenderingLODLevel.High => 64,
        RenderingLODLevel.Medium => 32,
        RenderingLODLevel.Low => 16,
        RenderingLODLevel.VeryLow => 8,
        _ => 32
    };
    
    // Regenerate mesh with new vertex count
    // Mark tile.needsRegeneration = true
}
```

---

## Terrain Modification

Allow runtime height changes (explosions, deformation, etc.).

### Implementation Strategy

**Step 1**: Add Modification Component

```csharp
public struct TerrainModification : IComponentData
{
    public float3 worldPosition;
    public float radius;
    public float intensity;
    public bool applied;
}
```

**Step 2**: Create Modification System

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainMeshGenerationSystem))]
public partial class TerrainModificationSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var modifications = SystemAPI.Query<RefRO<TerrainModification>>();
        
        foreach (var mod in modifications)
        {
            if (mod.ValueRO.applied)
                continue;
            
            // Find affected tiles
            var affectedTiles = FindTilesInRadius(mod.ValueRO.worldPosition, mod.ValueRO.radius);
            
            foreach (var tileEntity in affectedTiles)
            {
                ModifyTileHeight(tileEntity, mod.ValueRO);
            }
            
            // Mark as applied
            // ... set applied = true
        }
    }
    
    void ModifyTileHeight(Entity tile, TerrainModification mod)
    {
        var vertices = EntityManager.GetBuffer<VertexElement>(tile);
        var normals = EntityManager.GetBuffer<NormalElement>(tile);
        var tileData = EntityManager.GetComponentData<TerrainTile>(tile);
        
        for (int i = 0; i < vertices.Length; i++)
        {
            float3 vertexWorld = tileData.gridCoordinate * config.tileSize + vertices[i].value;
            float dist = math.distance(vertexWorld, mod.worldPosition);
            
            if (dist < mod.radius)
            {
                float influence = 1f - (dist / mod.radius);
                float heightDelta = mod.intensity * influence;
                
                var vertex = vertices[i];
                vertex.value.y += heightDelta;
                vertices[i] = vertex;
            }
        }
        
        // Recalculate normals
        RecalculateNormals(tile);
        
        // Mark for mesh update
        var tileComp = EntityManager.GetComponentData<TerrainTile>(tile);
        tileComp.needsRegeneration = true;
        EntityManager.SetComponentData(tile, tileComp);
    }
}
```

**Step 3**: Trigger Modifications

```csharp
public void CreateExplosion(Vector3 position, float radius, float intensity)
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    
    var entity = em.CreateEntity();
    em.AddComponentData(entity, new TerrainModification
    {
        worldPosition = position,
        radius = radius,
        intensity = intensity,
        applied = false
    });
}
```

---

## Procedural Objects

Spawn objects (rocks, trees, grass) on terrain tiles.

### Implementation Strategy

**Step 1**: Add Object Spawning Component

```csharp
public struct TerrainObjectSpawner : IComponentData
{
    public Entity prefabEntity;  // Object to spawn
    public int spawnCount;       // Objects per tile
    public float minHeight;      // Height range
    public float maxHeight;
}
```

**Step 2**: Create Spawning System

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainRenderingSystem))]
public partial class TerrainObjectSpawningSystem : SystemBase
{
    protected override void OnUpdate()
    {
        var spawner = SystemAPI.GetSingleton<TerrainObjectSpawner>();
        
        foreach (var (tile, entity) in 
            SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<MeshReference>()
            .WithNone<ObjectsSpawned>() // Tag to track spawning
            .WithEntityAccess())
        {
            SpawnObjectsOnTile(tile, entity, spawner);
            EntityManager.AddComponent<ObjectsSpawned>(entity);
        }
    }
    
    void SpawnObjectsOnTile(RefRO<TerrainTile> tile, Entity tileEntity, TerrainObjectSpawner spawner)
    {
        var vertices = EntityManager.GetBuffer<VertexElement>(tileEntity);
        var random = new Unity.Mathematics.Random((uint)(tile.ValueRO.gridCoordinate.GetHashCode()));
        
        for (int i = 0; i < spawner.spawnCount; i++)
        {
            // Random vertex
            int vertexIndex = random.NextInt(0, vertices.Length);
            float3 spawnPos = vertices[vertexIndex].value;
            
            // Check height range
            if (spawnPos.y >= spawner.minHeight && spawnPos.y <= spawner.maxHeight)
            {
                // Instantiate object
                Entity instance = EntityManager.Instantiate(spawner.prefabEntity);
                EntityManager.SetComponentData(instance, new LocalTransform
                {
                    Position = spawnPos,
                    Rotation = quaternion.identity,
                    Scale = 1f
                });
            }
        }
    }
}
```

---

## Custom Materials

### Approach 1: Height-Based Material

Blend materials based on terrain height:

```csharp
// In custom shader
float4 frag(v2f i) : SV_Target
{
    float height = i.worldPos.y;
    
    float4 grassColor = tex2D(_GrassTex, i.uv);
    float4 rockColor = tex2D(_RockTex, i.uv);
    float4 snowColor = tex2D(_SnowTex, i.uv);
    
    float4 color;
    if (height < 10)
        color = grassColor;
    else if (height < 40)
        color = lerp(grassColor, rockColor, (height - 10) / 30);
    else
        color = lerp(rockColor, snowColor, (height - 40) / 20);
    
    return color;
}
```

### Approach 2: Slope-Based Material

Blend based on terrain steepness:

```csharp
// In shader
float slope = 1.0 - i.normal.y; // 0 = flat, 1 = vertical

float4 color;
if (slope < 0.3)
    color = grassColor;  // Flat areas
else if (slope < 0.7)
    color = lerp(grassColor, rockColor, (slope - 0.3) / 0.4);
else
    color = rockColor;  // Steep areas
```

---

## Water System

Add water planes at specific height levels.

### Implementation

**Step 1**: Create Water Component

```csharp
public struct WaterPlane : IComponentData
{
    public float waterLevel;  // Y height of water
    public float3 tilePosition;
}
```

**Step 2**: Spawn Water Tiles

```csharp
// When spawning terrain tiles, also check for water
foreach (var tile in newTiles)
{
    if (TileNeedsWater(tile))
    {
        Entity waterEntity = em.CreateEntity();
        em.AddComponentData(waterEntity, new WaterPlane 
        { 
            waterLevel = 0f, 
            tilePosition = tile.position 
        });
        // Add mesh, rendering components for water plane
    }
}

bool TileNeedsWater(TerrainTile tile)
{
    // Check if tile contains vertices below water level
    var vertices = GetBuffer<VertexElement>(tile.entity);
    return vertices.Any(v => v.value.y < 0f);
}
```

**Step 3**: Water Material

```
Shader: URP/Lit with transparency
Alpha: 0.7
Color: Blue (0, 0.3, 0.8)
Smoothness: 0.9
```

---

## Minimap Integration

Render terrain to minimap texture.

### Approach 1: Camera-Based Minimap

```csharp
public class TerrainMinimap : MonoBehaviour
{
    public Camera minimapCamera;
    public RenderTexture minimapTexture;
    
    void Start()
    {
        // Setup orthographic camera above player
        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = 250f;  // View radius
        minimapCamera.transform.rotation = Quaternion.Euler(90, 0, 0); // Look down
        minimapCamera.targetTexture = minimapTexture;
    }
    
    void Update()
    {
        // Follow player XZ position
        var playerPos = GetPlayerPosition();
        minimapCamera.transform.position = new Vector3(
            playerPos.x, 
            500f,  // High above terrain
            playerPos.z
        );
    }
}
```

### Approach 2: Texture-Based Minimap

Generate heightmap texture from tile data:

```csharp
public class TerrainHeightmapGenerator : MonoBehaviour
{
    private Texture2D _heightmap;
    
    public Texture2D GenerateHeightmap(int resolution)
    {
        _heightmap = new Texture2D(resolution, resolution);
        
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile), typeof(VertexElement));
        var entities = query.ToEntityArray(Allocator.Temp);
        
        foreach (var entity in entities)
        {
            var tile = em.GetComponentData<TerrainTile>(entity);
            var vertices = em.GetBuffer<VertexElement>(entity);
            
            // Convert vertices to texture pixels
            foreach (var vertex in vertices)
            {
                int x = (int)((vertex.value.x + tile.gridCoordinate.x * 100f) / 1000f * resolution);
                int y = (int)((vertex.value.z + tile.gridCoordinate.y * 100f) / 1000f * resolution);
                
                float height01 = (vertex.value.y + 50f) / 100f; // Normalize to 0-1
                _heightmap.SetPixel(x, y, new Color(height01, height01, height01));
            }
        }
        
        _heightmap.Apply();
        entities.Dispose();
        query.Dispose();
        
        return _heightmap;
    }
}
```

---

## Procedural Details (Grass, Rocks)

Spawn detail objects using GPU instancing.

### Implementation

**Step 1**: Collect Spawn Points

```csharp
public struct DetailSpawnPoint : IBufferElementData
{
    public float3 position;
    public float3 normal;
    public byte detailType;  // 0 = grass, 1 = rock, etc.
}

// In mesh generation, add spawn points
foreach (vertex at position with normal)
{
    if (ShouldSpawnDetail(position, normal))
    {
        detailSpawnPoints.Add(new DetailSpawnPoint 
        { 
            position, 
            normal, 
            detailType = ChooseDetailType(position) 
        });
    }
}
```

**Step 2**: Render with GPU Instancing

```csharp
public class DetailInstanceRenderer : MonoBehaviour
{
    public Mesh grassMesh;
    public Material grassMaterial;
    
    private Matrix4x4[] _matrices;
    
    void Update()
    {
        // Collect all detail spawn points from visible tiles
        var spawnPoints = CollectSpawnPoints();
        
        // Convert to matrices
        _matrices = new Matrix4x4[spawnPoints.Length];
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            _matrices[i] = Matrix4x4.TRS(
                spawnPoints[i].position,
                Quaternion.FromToRotation(Vector3.up, spawnPoints[i].normal),
                Vector3.one
            );
        }
        
        // Render instances
        Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, _matrices);
    }
}
```

**Performance**: Can render thousands of instances efficiently.

---

## Terrain Holes

Create holes in terrain (caves, tunnels).

### Implementation

**Step 1**: Add Hole Data

```csharp
public struct TerrainHole : IBufferElementData
{
    public float3 center;
    public float radius;
}
```

**Step 2**: Modify Mesh Generation

```csharp
// In mesh generation job
bool IsVertexInHole(float3 vertex, DynamicBuffer<TerrainHole> holes)
{
    foreach (var hole in holes)
    {
        if (math.distance(vertex, hole.center) < hole.radius)
            return true;
    }
    return false;
}

// When generating triangles, skip if vertices in hole
if (!IsVertexInHole(v0, holes) && !IsVertexInHole(v1, holes) && !IsVertexInHole(v2, holes))
{
    // Add triangle
    indices.Add(triangle);
}
```

**Step 3**: Update Colliders

- Holes automatically reflected in mesh colliders
- No additional physics work needed

---

## Path/Road System

Flatten terrain along paths for roads, rivers, etc.

### Implementation

**Step 1**: Define Path

```csharp
public struct TerrainPath : IBufferElementData
{
    public float3 point;
}

public struct TerrainPathData : IComponentData
{
    public float pathWidth;
    public float targetHeight;
    public float blendDistance;
}
```

**Step 2**: Modify Heights Along Path

```csharp
// In mesh generation job
float ModifyHeightForPath(float3 vertex, DynamicBuffer<TerrainPath> path, TerrainPathData pathData)
{
    float minDistance = float.MaxValue;
    
    // Find closest path segment
    for (int i = 0; i < path.Length - 1; i++)
    {
        float dist = DistanceToLineSegment(vertex, path[i].point, path[i+1].point);
        minDistance = math.min(minDistance, dist);
    }
    
    // Blend height if near path
    if (minDistance < pathData.pathWidth + pathData.blendDistance)
    {
        if (minDistance < pathData.pathWidth)
        {
            return pathData.targetHeight;  // Flat road
        }
        else
        {
            float t = (minDistance - pathData.pathWidth) / pathData.blendDistance;
            return math.lerp(pathData.targetHeight, originalHeight, t);  // Blend to terrain
        }
    }
    
    return originalHeight;
}
```

---

## Vertex Colors for Variation

Add vertex colors for shader-based variation.

### Implementation

**Step 1**: Add Color Buffer

```csharp
public struct ColorElement : IBufferElementData
{
    public Color32 value;
}
```

**Step 2**: Generate Colors

```csharp
// In mesh generation job
foreach (vertex at height)
{
    Color32 color;
    if (height < 5)
        color = new Color32(100, 180, 100, 255);  // Green (grass)
    else if (height < 30)
        color = new Color32(150, 150, 120, 255);  // Brown (dirt)
    else
        color = new Color32(200, 200, 200, 255);  // Grey (rock)
    
    colors.Add(new ColorElement { value = color });
}
```

**Step 3**: Apply to Mesh

```csharp
// In TerrainRenderingSystem
var colors = EntityManager.GetBuffer<ColorElement>(entity);
var colorsNative = colors.Reinterpret<Color32>().AsNativeArray();
mesh.SetColors(colorsNative);
```

**Step 4**: Use in Shader

```hlsl
v2f vert(appdata v)
{
    v2f o;
    o.vertex = UnityObjectToClipPos(v.vertex);
    o.color = v.color;  // Pass vertex color
    return o;
}

float4 frag(v2f i) : SV_Target
{
    float4 tex = tex2D(_MainTex, i.uv);
    return tex * i.color;  // Tint by vertex color
}
```

---

## Multi-Layer Texturing

Apply multiple textures based on height or slope.

### Shader Implementation

```hlsl
Shader "Custom/TerrainMultiLayer"
{
    Properties
    {
        _GrassTexture ("Grass", 2D) = "white" {}
        _DirtTexture ("Dirt", 2D) = "white" {}
        _RockTexture ("Rock", 2D) = "white" {}
        _SnowTexture ("Snow", 2D) = "white" {}
    }
    
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            float4 frag(v2f i) : SV_Target
            {
                float height = i.worldPos.y;
                float slope = 1.0 - i.normal.y;
                
                // Sample all textures
                float4 grass = tex2D(_GrassTexture, i.uv);
                float4 dirt = tex2D(_DirtTexture, i.uv);
                float4 rock = tex2D(_RockTexture, i.uv);
                float4 snow = tex2D(_SnowTexture, i.uv);
                
                // Height-based blending
                float4 color = grass;
                color = lerp(color, dirt, saturate((height - 5) / 10));
                color = lerp(color, rock, saturate((height - 20) / 20));
                color = lerp(color, snow, saturate((height - 50) / 10));
                
                // Slope-based blending (steeper = more rock)
                color = lerp(color, rock, saturate(slope * 2));
                
                return color;
            }
            ENDHLSL
        }
    }
}
```

---

## Custom Noise Functions

Replace Perlin noise with custom generation.

### Example: Sine Wave Terrain

```csharp
// In mesh generation job
float SampleCustomHeight(float2 worldPosition, TerrainTileConfig config)
{
    // Sine wave pattern
    float height = math.sin(worldPosition.x * 0.1f) * 10f +
                   math.cos(worldPosition.y * 0.1f) * 10f;
    
    return height;
}
```

### Example: Cellular/Voronoi Noise

```csharp
float SampleVoronoi(float2 position)
{
    float2 i = math.floor(position);
    float2 f = math.frac(position);
    
    float minDist = 1.0f;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 neighbor = new float2(x, y);
            float2 point = Random(i + neighbor);
            float dist = math.length(f - neighbor - point);
            minDist = math.min(minDist, dist);
        }
    }
    
    return minDist;
}
```

---

## Terrain Metadata System

Store gameplay data per tile (temperature, resources, etc.).

### Implementation

**Step 1**: Add Metadata Component

```csharp
public struct TerrainMetadata : IComponentData
{
    public float temperature;      // -1 to +1
    public float resourceDensity;  // 0 to 1
    public byte territoryOwner;    // Team/faction ID
}
```

**Step 2**: Generate Metadata

```csharp
// During tile spawning
TerrainMetadata GenerateMetadata(int2 gridCoordinate)
{
    float2 pos = new float2(gridCoordinate);
    
    return new TerrainMetadata
    {
        temperature = noise.cnoise(pos * 0.001f),
        resourceDensity = noise.cnoise(pos * 0.003f + 500f),
        territoryOwner = 0
    };
}
```

**Step 3**: Use Metadata

```csharp
// Query tiles by metadata
foreach (var (tile, metadata) in 
    SystemAPI.Query<RefRO<TerrainTile>, RefRO<TerrainMetadata>>())
{
    if (metadata.ValueRO.resourceDensity > 0.7f)
    {
        // High resource area - spawn resource nodes
    }
}
```

---

## Advanced: Infinite Precision

For truly unlimited worlds, implement double-precision coordinates.

### Implementation Concept

**Step 1**: Replace float3 with double3

```csharp
public struct TerrainTileDouble : IComponentData
{
    public int2 gridCoordinate;
    public double3 preciseOffset;  // Sub-tile offset
}
```

**Step 2**: Floating Origin System

```csharp
// When player exceeds threshold distance from origin
if (math.length(playerPosition) > 10000.0)
{
    // Shift world origin
    ShiftAllEntities(-playerPosition);
    playerPosition = double3.zero;
}
```

**Complexity**: Significant - requires modifying many systems.

---

## Related Documentation

- **[Technical Details](TECHNICAL_DETAILS.md)** - Algorithm details
- **[API Reference](API_REFERENCE.md)** - Component and system APIs
- **[Integration Guide](INTEGRATION.md)** - Integrating with game systems
- **[Performance Optimization](PERFORMANCE.md)** - Optimizing custom features

---

**Back to**: [Documentation Hub](README.md)