# Infinite Terrain System - Extension Guide

**Last Updated:** March 14, 2026  
**Audience:** Advanced Developers

## Table of Contents
1. [Adding Custom Features](#adding-custom-features)
2. [LOD System Implementation](#lod-system-implementation)
3. [Biome System](#biome-system)
4. [Terrain Modification](#terrain-modification)
5. [Vegetation System](#vegetation-system)
6. [Custom Noise Functions](#custom-noise-functions)
7. [Multi-Threading Optimizations](#multi-threading-optimizations)

---

## Adding Custom Features

### Pattern: Add Component to Tiles

**Step 1: Define Component**

```csharp
// In TileComponents.cs or new file:
public struct TileFeatures : IComponentData
{
    public float temperatureAtCenter;  // 0-100
    public float moistureLevel;        // 0-1
    public int vegetationDensity;      // 0-100
    public bool hasWater;
}
```

**Step 2: Add During Spawning**

```csharp
// In TileSpawningSystem.cs, inside tilesToSpawn loop:
ecb.AddComponent(tileEntity, new TileFeatures
{
    temperatureAtCenter = CalculateTemperature(gridCoord),
    moistureLevel = CalculateMoisture(gridCoord),
    vegetationDensity = 50,
    hasWater = false
});
```

**Step 3: Use in Generation**

```csharp
// In TerrainMeshGenerationSystem.cs:
var features = SystemAPI.GetComponent<TileFeatures>(entity);

if (features.moistureLevel > 0.7f)
{
    // Modify noise parameters for wet terrain
    config.noiseAmplitude *= 0.5f;  // Flatter
}
```

---

## LOD System Implementation

### Goal
Reduce detail (vertex count) for distant tiles to improve performance.

### Design

**Add LOD component:**

```csharp
// In TileComponents.cs:
public struct TileLOD : IComponentData
{
    public int lodLevel;  // 0=highest detail, 3=lowest
    public int currentVerticesPerSide;  // Varies by LOD
}
```

**Define LOD levels:**

```csharp
public static class LODSettings
{
    public const int LOD0_DISTANCE = 100;   // High detail: 0-100m
    public const int LOD1_DISTANCE = 200;   // Medium: 100-200m
    public const int LOD2_DISTANCE = 400;   // Low: 200-400m
    // Beyond 400m: LOD3 (lowest)
    
    public const int LOD0_VERTICES = 64;
    public const int LOD1_VERTICES = 32;
    public const int LOD2_VERTICES = 16;
    public const int LOD3_VERTICES = 8;
}
```

### Implementation

**Step 1: Calculate LOD During Spawning**

```csharp
// In TileSpawningSystem.cs:
foreach (var gridCoord in tilesToSpawn)
{
    Entity tileEntity = ecb.CreateEntity();
    
    // Calculate distance from player to tile center
    float2 tileCenter = new float2(
        gridCoord.x * config.tileSize + config.tileSize * 0.5f,
        gridCoord.y * config.tileSize + config.tileSize * 0.5f
    );
    float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
    float distanceToTile = math.distance(tileCenter, playerPos2D);
    
    // Determine LOD level
    int lodLevel;
    int verticesForLOD;
    if (distanceToTile < LODSettings.LOD0_DISTANCE)
    {
        lodLevel = 0;
        verticesForLOD = LODSettings.LOD0_VERTICES;
    }
    else if (distanceToTile < LODSettings.LOD1_DISTANCE)
    {
        lodLevel = 1;
        verticesForLOD = LODSettings.LOD1_VERTICES;
    }
    else if (distanceToTile < LODSettings.LOD2_DISTANCE)
    {
        lodLevel = 2;
        verticesForLOD = LODSettings.LOD2_VERTICES;
    }
    else
    {
        lodLevel = 3;
        verticesForLOD = LODSettings.LOD3_VERTICES;
    }
    
    // Add LOD component
    ecb.AddComponent(tileEntity, new TileLOD
    {
        lodLevel = lodLevel,
        currentVerticesPerSide = verticesForLOD
    });
    
    // ... rest of tile creation ...
}
```

**Step 2: Update LOD Over Time**

Create new system:

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct TileLODUpdateSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTag>();
        state.RequireForUpdate<TerrainTileConfig>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
        var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        float3 playerPosition = playerTransform.Position;
        
        // Check each tile's distance and update LOD if changed
        foreach (var (tile, lod, entity) in 
            SystemAPI.Query<RefRO<TerrainTile>, RefRW<TileLOD>>().WithEntityAccess())
        {
            // Calculate distance to tile
            float2 tileCenter = new float2(
                tile.ValueRO.gridCoordinate.x * config.tileSize + config.tileSize * 0.5f,
                tile.ValueRO.gridCoordinate.y * config.tileSize + config.tileSize * 0.5f
            );
            float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
            float distanceToTile = math.distance(tileCenter, playerPos2D);
            
            // Determine new LOD level
            int newLODLevel = CalculateLODLevel(distanceToTile);
            
            // If LOD changed, trigger regeneration
            if (newLODLevel != lod.ValueRO.lodLevel)
            {
                lod.ValueRW.lodLevel = newLODLevel;
                lod.ValueRW.currentVerticesPerSide = GetVerticesForLOD(newLODLevel);
                
                // Mark tile for regeneration
                var tileRW = SystemAPI.GetComponentRW<TerrainTile>(entity);
                tileRW.ValueRW.needsRegeneration = true;
                
                UnityEngine.Debug.Log($"Tile {tile.ValueRO.gridCoordinate} LOD changed: {lod.ValueRO.lodLevel} → {newLODLevel}");
            }
        }
    }
    
    private static int CalculateLODLevel(float distance)
    {
        if (distance < LODSettings.LOD0_DISTANCE) return 0;
        if (distance < LODSettings.LOD1_DISTANCE) return 1;
        if (distance < LODSettings.LOD2_DISTANCE) return 2;
        return 3;
    }
    
    private static int GetVerticesForLOD(int lodLevel)
    {
        switch (lodLevel)
        {
            case 0: return LODSettings.LOD0_VERTICES;
            case 1: return LODSettings.LOD1_VERTICES;
            case 2: return LODSettings.LOD2_VERTICES;
            default: return LODSettings.LOD3_VERTICES;
        }
    }
}
```

**Step 3: Use LOD in Mesh Generation**

```csharp
// In TerrainMeshGenerationSystem.cs, modify GenerateTileMesh():
private void GenerateTileMesh(
    ref TerrainTile tile,
    DynamicBuffer<VertexElement> vertexBuffer,
    // ... other buffers ...
    TerrainTileConfig config,
    WorldOriginOffset worldOffset,
    TileLOD lod)  // ← Add LOD parameter
{
    // Use LOD-specific vertex count
    int verticesPerSide = lod.currentVerticesPerSide;  // Instead of config.verticesPerSide
    
    // ... rest of generation code ...
}

// Update OnUpdate to pass LOD:
var lod = SystemAPI.GetComponent<TileLOD>(entity);
GenerateTileMesh(ref tile, vertexBuffer, normalBuffer, uvBuffer, indexBuffer, config, worldOffset, lod);
```

**Benefits:**
- Distant tiles: 8x8 = 64 vertices (vs. 32x32 = 1024)
- 16x fewer vertices at distance
- Significant GPU/memory savings

---

## Biome System

### Goal
Different terrain styles in different regions (desert, forest, mountains, etc.).

### Design

**Add biome component:**

```csharp
// In TileComponents.cs:
public struct BiomeType : IComponentData
{
    public int biomeID;  // 0=plains, 1=desert, 2=mountains, 3=forest
}

public struct BiomeConfig : IComponentData
{
    public float noiseFrequency;
    public float noiseAmplitude;
    public int noiseOctaves;
    public float noiseLacunarity;
    public float noisePersistence;
    public Color terrainColor;
}
```

### Implementation

**Step 1: Biome Determination**

```csharp
// In TileSpawningSystem.cs:
public static int DetermineBiome(int2 gridCoord)
{
    // Use low-frequency noise to determine biome regions
    float2 biomePos = new float2(gridCoord.x, gridCoord.y) * 0.001f;  // Very low frequency
    float biomeNoise = noise.snoise(biomePos);
    
    // Map noise to biome IDs
    if (biomeNoise < -0.5f) return 0;  // Plains
    if (biomeNoise < 0.0f) return 1;   // Desert
    if (biomeNoise < 0.5f) return 2;   // Mountains
    return 3;                          // Forest
}

// When creating tile:
ecb.AddComponent(tileEntity, new BiomeType
{
    biomeID = DetermineBiome(gridCoord)
});
```

**Step 2: Biome-Specific Generation**

```csharp
// In TerrainMeshGenerationSystem.cs:
private TerrainTileConfig GetConfigForBiome(int biomeID, TerrainTileConfig baseConfig)
{
    var config = baseConfig;
    
    switch (biomeID)
    {
        case 0: // Plains
            config.noiseAmplitude = 5f;
            config.noiseFrequency = 0.01f;
            config.noiseOctaves = 2;
            break;
            
        case 1: // Desert
            config.noiseAmplitude = 15f;
            config.noiseFrequency = 0.008f;
            config.noiseOctaves = 3;
            break;
            
        case 2: // Mountains
            config.noiseAmplitude = 80f;
            config.noiseFrequency = 0.015f;
            config.noiseOctaves = 6;
            break;
            
        case 3: // Forest
            config.noiseAmplitude = 20f;
            config.noiseFrequency = 0.012f;
            config.noiseOctaves = 4;
            break;
    }
    
    return config;
}

// In GenerateTileMesh:
var biome = SystemAPI.GetComponent<BiomeType>(entity);
var biomeConfig = GetConfigForBiome(biome.biomeID, config);
float height = SampleNoise(worldX, worldZ, biomeConfig);  // Use biome config
```

**Step 3: Biome-Specific Materials**

```csharp
// In TerrainRenderingSystem.cs:
private Material GetMaterialForBiome(int biomeID)
{
    switch (biomeID)
    {
        case 0: return _plainsMaterial;
        case 1: return _desertMaterial;
        case 2: return _mountainMaterial;
        case 3: return _forestMaterial;
        default: return _terrainMaterial;
    }
}

// In CreateAndAssignMesh:
var biome = EntityManager.GetComponentData<BiomeType>(entity);
var material = GetMaterialForBiome(biome.biomeID);
```

**Advanced: Biome Blending**

```csharp
// Sample multiple biomes and blend at boundaries:
float SampleBlendedNoise(double worldX, double worldZ, float2 gridCoord)
{
    // Sample nearby biome centers
    float totalHeight = 0f;
    float totalWeight = 0f;
    
    for (int bx = -1; bx <= 1; bx++)
    {
        for (int bz = -1; bz <= 1; bz++)
        {
            int2 biomeGrid = new int2((int)gridCoord.x + bx, (int)gridCoord.y + bz);
            int biomeID = DetermineBiome(biomeGrid);
            
            // Calculate distance to biome center
            float2 biomeCenter = new float2(biomeGrid.x, biomeGrid.y) * config.tileSize;
            float2 samplePos = new float2((float)worldX, (float)worldZ);
            float distance = math.distance(biomeCenter, samplePos);
            
            // Weight by inverse distance
            float weight = 1.0f / (1.0f + distance * 0.001f);
            
            // Sample height for this biome
            var biomeConfig = GetConfigForBiome(biomeID, baseConfig);
            float height = SampleNoise(worldX, worldZ, biomeConfig);
            
            totalHeight += height * weight;
            totalWeight += weight;
        }
    }
    
    return totalHeight / totalWeight;
}
```

---

## Terrain Modification

### Goal
Allow runtime editing of terrain (digging, building, explosions).

### Design

**Add modification buffer:**

```csharp
// In TileComponents.cs:
public struct HeightModification : IBufferElementData
{
    public float2 localPosition;  // Position within tile (0-tileSize)
    public float heightDelta;     // Change in height (+/-)
    public float radius;          // Area of effect
}
```

### Implementation

**Step 1: Apply Modification**

```csharp
// Create new system:
public partial class TerrainModificationSystem : SystemBase
{
    public void ModifyTerrainAt(float3 worldPosition, float heightDelta, float radius)
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Find affected tile(s)
        int2 gridCoord = new int2(
            (int)math.floor(worldPosition.x / config.tileSize),
            (int)math.floor(worldPosition.z / config.tileSize)
        );
        
        // Find tile entity
        Entities
            .WithAll<TerrainTile>()
            .ForEach((Entity entity, ref TerrainTile tile) =>
            {
                if (tile.gridCoordinate.Equals(gridCoord))
                {
                    // Add modification to buffer
                    var modifications = EntityManager.GetBuffer<HeightModification>(entity);
                    
                    float2 localPos = new float2(
                        worldPosition.x - gridCoord.x * config.tileSize,
                        worldPosition.z - gridCoord.y * config.tileSize
                    );
                    
                    modifications.Add(new HeightModification
                    {
                        localPosition = localPos,
                        heightDelta = heightDelta,
                        radius = radius
                    });
                    
                    // Mark for regeneration
                    tile.needsRegeneration = true;
                    
                    UnityEngine.Debug.Log($"Added modification to tile {gridCoord}");
                }
            })
            .WithoutBurst()
            .Run();
    }
    
    protected override void OnUpdate()
    {
        // System can be used for other modification logic
    }
}
```

**Step 2: Apply Modifications During Generation**

```csharp
// In TerrainMeshGenerationSystem.cs, modify GenerateTileMesh:

// After sampling base noise height:
float height = SampleNoise(worldX, worldZ, config);

// Apply modifications from buffer
if (SystemAPI.HasBuffer<HeightModification>(entity))
{
    var modifications = SystemAPI.GetBuffer<HeightModification>(entity);
    
    foreach (var mod in modifications)
    {
        float2 vertexLocalPos = new float2(localX, localZ);
        float distToMod = math.distance(vertexLocalPos, mod.localPosition);
        
        if (distToMod < mod.radius)
        {
            // Apply modification with smooth falloff
            float influence = 1.0f - (distToMod / mod.radius);
            influence = math.smoothstep(0f, 1f, influence);  // Smooth curve
            
            height += mod.heightDelta * influence;
        }
    }
}

vertices[index] = new float3(localX, height, localZ);
```

**Step 3: Call from Gameplay Code**

```csharp
// Example: Explosion deforms terrain
public class ExplosionEffect : MonoBehaviour
{
    void OnExplode()
    {
        Vector3 explosionPos = transform.position;
        
        var world = World.DefaultGameObjectInjectionWorld;
        var modSystem = world.GetExistingSystemManaged<TerrainModificationSystem>();
        
        if (modSystem != null)
        {
            // Create crater: negative height delta
            modSystem.ModifyTerrainAt(explosionPos, -5f, 10f);
        }
    }
}
```

**Optimization: Serialize Modifications**

```csharp
// Save modifications to disk for persistent world:
public void SaveModifications(string filePath)
{
    var modifications = new List<SerializedModification>();
    
    Entities
        .WithAll<HeightModification>()
        .ForEach((in DynamicBuffer<HeightModification> buffer, in TerrainTile tile) =>
        {
            foreach (var mod in buffer)
            {
                modifications.Add(new SerializedModification
                {
                    gridCoordinate = tile.gridCoordinate,
                    localPosition = mod.localPosition,
                    heightDelta = mod.heightDelta,
                    radius = mod.radius
                });
            }
        })
        .WithoutBurst()
        .Run();
    
    // Serialize to JSON or binary
    string json = JsonUtility.ToJson(new ModificationList { items = modifications });
    System.IO.File.WriteAllText(filePath, json);
}
```

---

## Vegetation System

### Goal
Spawn trees, rocks, grass on terrain tiles.

### Design

**Add vegetation component:**

```csharp
public struct VegetationPlacement : IBufferElementData
{
    public float3 localPosition;  // Position within tile
    public int vegetationType;    // 0=tree, 1=rock, 2=bush
    public float scale;           // Size variation
    public quaternion rotation;   // Random rotation
}

public struct VegetationGenerated : IComponentData
{
    public bool generated;
}
```

### Implementation

**Step 1: Generate Placements**

Create new system:

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainMeshGenerationSystem))]
public partial struct VegetationPlacementSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainTileConfig>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        var worldOffset = SystemAPI.GetSingleton<WorldOriginOffset>();
        
        // Process tiles that have mesh but no vegetation
        foreach (var (tile, entity) in 
            SystemAPI.Query<RefRO<TerrainTile>>()
                .WithNone<VegetationGenerated>()
                .WithAll<TerrainTile>()
                .WithEntityAccess())
        {
            if (!tile.ValueRO.meshGenerated)
                continue;
            
            // Add vegetation buffer if not present
            if (!SystemAPI.HasBuffer<VegetationPlacement>(entity))
            {
                state.EntityManager.AddBuffer<VegetationPlacement>(entity);
            }
            
            var vegBuffer = SystemAPI.GetBuffer<VegetationPlacement>(entity);
            vegBuffer.Clear();
            
            // Determine vegetation density using noise
            float2 tileCenter = new float2(
                tile.ValueRO.gridCoordinate.x * config.tileSize + config.tileSize * 0.5f,
                tile.ValueRO.gridCoordinate.y * config.tileSize + config.tileSize * 0.5f
            );
            
            // Use different noise for vegetation density
            float densityNoise = noise.snoise(tileCenter * 0.005f);
            int treesToSpawn = (int)math.lerp(5, 20, (densityNoise + 1f) * 0.5f);
            
            // Generate random positions
            Random rng = Random.CreateFromIndex((uint)entity.Index);
            
            for (int i = 0; i < treesToSpawn; i++)
            {
                float2 localPos2D = rng.NextFloat2(0, config.tileSize);
                
                // Sample height at this position
                double3 worldPos = new double3(
                    tile.ValueRO.gridCoordinate.x * config.tileSize + localPos2D.x,
                    0,
                    tile.ValueRO.gridCoordinate.y * config.tileSize + localPos2D.y
                ) + worldOffset.accumulatedOffset;
                
                float height = SampleNoise(worldPos.x, worldPos.z, config);
                
                // Reject if slope too steep
                // (sample nearby heights to calculate slope)
                float nearbyHeight = SampleNoise(worldPos.x + 1, worldPos.z, config);
                float slope = math.abs(nearbyHeight - height);
                
                if (slope < 5f)  // Reasonable slope
                {
                    vegBuffer.Add(new VegetationPlacement
                    {
                        localPosition = new float3(localPos2D.x, height, localPos2D.y),
                        vegetationType = rng.NextInt(0, 3),  // Random type
                        scale = rng.NextFloat(0.8f, 1.2f),
                        rotation = quaternion.RotateY(rng.NextFloat(0, math.PI * 2))
                    });
                }
            }
            
            // Mark as generated
            state.EntityManager.AddComponent<VegetationGenerated>(entity);
        }
    }
    
    // Copy SampleNoise function here or make it public/static elsewhere
}
```

**Step 2: Spawn Vegetation Entities**

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(VegetationPlacementSystem))]
public partial class VegetationSpawningSystem : SystemBase
{
    private EntityPrefab _treePrefab;
    private EntityPrefab _rockPrefab;
    private EntityPrefab _bushPrefab;
    
    protected override void OnCreate()
    {
        // Load prefabs (you'll need to create these)
        // _treePrefab = ...
    }
    
    protected override void OnUpdate()
    {
        Entities
            .WithAll<VegetationGenerated>()
            .ForEach((Entity tileEntity, in TerrainTile tile, in DynamicBuffer<VegetationPlacement> placements, in LocalTransform tileTransform) =>
            {
                foreach (var placement in placements)
                {
                    // Calculate world position
                    float3 worldPos = tileTransform.Position + placement.localPosition;
                    
                    // Spawn vegetation entity
                    Entity vegEntity = EntityManager.Instantiate(GetPrefabForType(placement.vegetationType));
                    
                    // Set transform
                    EntityManager.SetComponentData(vegEntity, new LocalTransform
                    {
                        Position = worldPos,
                        Rotation = placement.rotation,
                        Scale = placement.scale
                    });
                    
                    // Add FloatingOriginEnabled so it shifts with world
                    EntityManager.AddComponent<FloatingOriginEnabled>(vegEntity);
                    
                    // Link to parent tile for cleanup
                    EntityManager.AddComponentData(vegEntity, new VegetationParent
                    {
                        tileEntity = tileEntity
                    });
                }
            })
            .WithStructuralChanges()
            .WithoutBurst()
            .Run();
    }
    
    private Entity GetPrefabForType(int type)
    {
        switch (type)
        {
            case 0: return _treePrefab;
            case 1: return _rockPrefab;
            case 2: return _bushPrefab;
            default: return Entity.Null;
        }
    }
}

// Add parent tracking:
public struct VegetationParent : IComponentData
{
    public Entity tileEntity;
}
```

**Step 3: Cleanup When Tile Despawns**

```csharp
// In TileSpawningSystem.cs, when destroying tiles:
foreach (var gridCoord in tilesToDespawn)
{
    if (_activeTiles.TryGetValue(gridCoord, out Entity tileEntity))
    {
        // Find and destroy all vegetation children
        Entities
            .WithAll<VegetationParent>()
            .ForEach((Entity vegEntity, in VegetationParent parent) =>
            {
                if (parent.tileEntity == tileEntity)
                {
                    ecb.DestroyEntity(vegEntity);
                }
            })
            .Run();
        
        // Then destroy tile
        ecb.DestroyEntity(tileEntity);
        _activeTiles.Remove(gridCoord);
    }
}
```

---

## Custom Noise Functions

### Ridged Noise (Mountains with Sharp Peaks)

```csharp
[BurstCompile]
private static float SampleRidgedNoise(double worldX, double worldZ, TerrainTileConfig config)
{
    float total = 0f;
    float frequency = config.noiseFrequency;
    float amplitude = config.noiseAmplitude;
    
    for (int i = 0; i < config.noiseOctaves; i++)
    {
        float2 samplePos = new float2((float)worldX, (float)worldZ) * frequency;
        float noiseValue = noise.snoise(samplePos);
        
        // Ridge: absolute value creates sharp peaks
        noiseValue = math.abs(noiseValue);
        
        // Invert: valleys become peaks
        noiseValue = 1.0f - noiseValue;
        
        // Square for sharper peaks
        noiseValue = noiseValue * noiseValue;
        
        total += noiseValue * amplitude;
        amplitude *= config.noisePersistence;
        frequency *= config.noiseLacunarity;
    }
    
    return total * 0.5f;
}
```

**Visual Effect:** Mountain ridges, sharp peaks, dramatic terrain.

---

### Cellular/Voronoi Noise (Tiled Patterns)

```csharp
[BurstCompile]
private static float SampleCellularNoise(double worldX, double worldZ, TerrainTileConfig config)
{
    float2 samplePos = new float2((float)worldX, (float)worldZ) * config.noiseFrequency;
    
    // Find nearest cell point
    float2 cellCoord = math.floor(samplePos);
    float minDist = 10000f;
    
    // Check 3x3 neighborhood
    for (int x = -1; x <= 1; x++)
    {
        for (int y = -1; y <= 1; y++)
        {
            float2 neighbor = cellCoord + new float2(x, y);
            
            // Generate pseudo-random point in this cell
            float2 point = neighbor + noise.snoise(neighbor * 0.1f);
            
            // Calculate distance to point
            float dist = math.distance(samplePos, point);
            minDist = math.min(minDist, dist);
        }
    }
    
    return minDist * config.noiseAmplitude * 10f;
}
```

**Visual Effect:** Cells, polygonal regions, useful for biome boundaries.

---

### Terraced Terrain (Layered/Stepped)

```csharp
[BurstCompile]
private static float SampleTerracedNoise(double worldX, double worldZ, TerrainTileConfig config)
{
    float height = SampleNoise(worldX, worldZ, config);  // Base noise
    
    // Quantize height to create terraces
    float terraceHeight = 5f;  // Height of each terrace
    float numTerraces = math.floor(height / terraceHeight);
    
    return numTerraces * terraceHeight;
}
```

**Visual Effect:** Step pyramid terrain, mesa formations.

---

### Hybrid Noise (Multiple Functions Blended)

```csharp
[BurstCompile]
private static float SampleHybridNoise(double worldX, double worldZ, TerrainTileConfig config)
{
    // Sample multiple noise types
    float perlin = SampleNoise(worldX, worldZ, config);
    float ridged = SampleRidgedNoise(worldX, worldZ, config);
    float cellular = SampleCellularNoise(worldX, worldZ, config);
    
    // Use additional noise to control blend weights
    float2 blendPos = new float2((float)worldX, (float)worldZ) * 0.001f;
    float blendNoise = noise.snoise(blendPos);  // [-1, 1]
    
    // Blend based on noise value
    if (blendNoise < -0.3f)
        return perlin;  // Smooth terrain
    else if (blendNoise < 0.3f)
        return math.lerp(perlin, ridged, (blendNoise + 0.3f) / 0.6f);  // Transition
    else
        return ridged;  // Mountain ridges
}
```

**Visual Effect:** Varied terrain with smooth transitions between styles.

---

## Multi-Threading Optimizations

### Goal
Generate multiple tiles in parallel jobs for better performance.

### Challenge
DynamicBuffer writes are restricted in parallel jobs.

### Solution: Two-Phase Generation

**Phase 1: Parallel Mesh Generation (Burst)**

```csharp
[BurstCompile]
public partial struct ParallelMeshGenJob : IJobEntity
{
    [ReadOnly] public TerrainTileConfig config;
    [ReadOnly] public WorldOriginOffset worldOffset;
    
    // Write to NativeArrays (safe in parallel)
    [NativeDisableParallelForRestriction]
    public NativeArray<float3> vertexOutput;
    [NativeDisableParallelForRestriction]
    public NativeArray<float3> normalOutput;
    [NativeDisableParallelForRestriction]
    public NativeArray<int> indexOutput;
    
    [NativeDisableParallelForRestriction]
    public NativeArray<int> outputOffsets;  // Where each tile's data starts
    
    public void Execute([EntityIndexInQuery] int entityIndex, in TerrainTile tile)
    {
        if (tile.meshGenerated && !tile.needsRegeneration)
            return;
        
        int verticesPerSide = config.verticesPerSide;
        int vertexOffset = outputOffsets[entityIndex];
        
        // Generate vertices into NativeArray
        for (int z = 0; z < verticesPerSide; z++)
        {
            for (int x = 0; x < verticesPerSide; x++)
            {
                int index = vertexOffset + z * verticesPerSide + x;
                
                // ... same generation logic ...
                float height = SampleNoise(worldX, worldZ, config);
                vertexOutput[index] = new float3(localX, height, localZ);
            }
        }
        
        // Generate normals
        // ...
        
        // Generate indices
        // ...
    }
}
```

**Phase 2: Copy to Buffers (Main Thread)**

```csharp
public void OnUpdate(ref SystemState state)
{
    var entities = query.ToEntityArray(Allocator.Temp);
    int tileCount = entities.Length;
    
    // Allocate output arrays
    int vertsPerTile = config.verticesPerSide * config.verticesPerSide;
    var vertexOutput = new NativeArray<float3>(vertsPerTile * tileCount, Allocator.TempJob);
    var normalOutput = new NativeArray<float3>(vertsPerTile * tileCount, Allocator.TempJob);
    // ... other arrays ...
    
    // Calculate offsets for each tile
    var offsets = new NativeArray<int>(tileCount, Allocator.TempJob);
    for (int i = 0; i < tileCount; i++)
        offsets[i] = i * vertsPerTile;
    
    // Schedule parallel job
    var job = new ParallelMeshGenJob
    {
        config = config,
        worldOffset = worldOffset,
        vertexOutput = vertexOutput,
        normalOutput = normalOutput,
        outputOffsets = offsets
    };
    
    var jobHandle = job.ScheduleParallel(state.Dependency);
    jobHandle.Complete();  // Wait for completion
    
    // Copy results to buffers (main thread)
    for (int i = 0; i < tileCount; i++)
    {
        var entity = entities[i];
        var vertexBuffer = SystemAPI.GetBuffer<VertexElement>(entity);
        vertexBuffer.Clear();
        
        int offset = offsets[i];
        for (int v = 0; v < vertsPerTile; v++)
        {
            vertexBuffer.Add(new VertexElement { value = vertexOutput[offset + v] });
        }
        
        // ... copy normals, UVs, indices ...
    }
    
    // Cleanup
    vertexOutput.Dispose();
    normalOutput.Dispose();
    offsets.Dispose();
    entities.Dispose();
}
```

**Performance Gain:**
- Single-threaded: 4ms per tile * 10 tiles = 40ms
- Multi-threaded (8 cores): 4ms per tile / 8 = 0.5ms * 10 = 5ms
- **Speedup: 8x faster** (scales with core count)

---

## Custom Tile Queries

### Query Tiles by Distance

```csharp
public partial class NearbyTileQuery : SystemBase
{
    public NativeList<Entity> GetTilesNear(float3 position, float radius)
    {
        var nearbyTiles = new NativeList<Entity>(Allocator.Temp);
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        Entities
            .WithAll<TerrainTile>()
            .ForEach((Entity entity, in TerrainTile tile, in LocalTransform transform) =>
            {
                float2 tileCenter = new float2(
                    transform.Position.x + config.tileSize * 0.5f,
                    transform.Position.z + config.tileSize * 0.5f
                );
                float2 queryPos = new float2(position.x, position.z);
                float distance = math.distance(tileCenter, queryPos);
                
                if (distance <= radius)
                {
                    nearbyTiles.Add(entity);
                }
            })
            .WithoutBurst()
            .Run();
        
        return nearbyTiles;
    }
    
    protected override void OnUpdate() { }
}
```

**Usage:**
```csharp
var querySystem = World.GetExistingSystemManaged<NearbyTileQuery>();
var nearbyTiles = querySystem.GetTilesNear(explosionPosition, 50f);

foreach (var tileEntity in nearbyTiles)
{
    // Apply explosion damage/deformation
}

nearbyTiles.Dispose();
```

---

### Query Tile at Specific Position

```csharp
public static Entity GetTileAtPosition(float3 worldPosition, EntityManager em, TerrainTileConfig config)
{
    int2 gridCoord = new int2(
        (int)math.floor(worldPosition.x / config.tileSize),
        (int)math.floor(worldPosition.z / config.tileSize)
    );
    
    var query = em.CreateEntityQuery(typeof(TerrainTile));
    var entities = query.ToEntityArray(Allocator.Temp);
    
    foreach (var entity in entities)
    {
        var tile = em.GetComponentData<TerrainTile>(entity);
        if (tile.gridCoordinate.Equals(gridCoord))
        {
            entities.Dispose();
            return entity;
        }
    }
    
    entities.Dispose();
    return Entity.Null;
}
```

---

## Advanced Material Techniques

### Per-Tile Material Variation

**Goal:** Different tiles have different materials (desert vs. grass).

**Implementation:**

```csharp
// In TerrainRenderingSystem.cs:
private Material GetMaterialForTile(Entity entity)
{
    if (EntityManager.HasComponent<BiomeType>(entity))
    {
        var biome = EntityManager.GetComponentData<BiomeType>(entity);
        
        switch (biome.biomeID)
        {
            case 0: return _plainsMaterial;
            case 1: return _desertMaterial;
            case 2: return _mountainMaterial;
            default: return _terrainMaterial;
        }
    }
    
    return _terrainMaterial;
}

// In CreateAndAssignMesh:
var material = GetMaterialForTile(entity);
var registeredMaterial = entitiesGraphicsSystem.RegisterMaterial(material);
```

**Note:** Breaks batching (each material = separate draw call), but allows visual variety.

---

### Height-Based Texture Splatting

**Use vertex colors to encode height:**

```csharp
// In TerrainMeshGenerationSystem.cs, add color buffer:
ecb.AddBuffer<ColorElement>(tileEntity);  // Add to spawning

public struct ColorElement : IBufferElementData
{
    public float4 value;  // RGBA
}

// During generation:
float normalizedHeight = height / config.noiseAmplitude;  // [0, 1]
float4 color = new float4(normalizedHeight, 0, 0, 1);  // Encode height in red channel
colorBuffer.Add(new ColorElement { value = color });

// In CreateAndAssignMesh:
Color[] colors = new Color[vertexBuffer.Length];
for (int i = 0; i < colorBuffer.Length; i++)
    colors[i] = colorBuffer[i].value;
mesh.colors = colors;
```

**Custom Shader:**
```glsl
// In terrain shader:
fixed4 frag(v2f i) : SV_Target
{
    float height = i.color.r;  // Read from vertex color
    
    fixed4 lowColor = tex2D(_GrassTexture, i.uv);
    fixed4 midColor = tex2D(_RockTexture, i.uv);
    fixed4 highColor = tex2D(_SnowTexture, i.uv);
    
    // Blend textures based on height
    fixed4 finalColor = lerp(lowColor, midColor, saturate(height * 2 - 0.5));
    finalColor = lerp(finalColor, highColor, saturate(height * 2 - 1.5));
    
    return finalColor;
}
```

---

## Performance Monitoring System

### Create Metrics Component

```csharp
public class TerrainMetrics : IComponentData
{
    public int activeTileCount;
    public int tilesGeneratedThisFrame;
    public float averageGenerationTime;
    public long totalMemoryUsed;
}
```

### Metrics Collection System

```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class TerrainMetricsSystem : SystemBase
{
    private System.Diagnostics.Stopwatch _stopwatch;
    
    protected override void OnCreate()
    {
        _stopwatch = new System.Diagnostics.Stopwatch();
    }
    
    protected override void OnUpdate()
    {
        _stopwatch.Restart();
        
        // Count active tiles
        var tileQuery = GetEntityQuery(typeof(TerrainTile));
        int activeTiles = tileQuery.CalculateEntityCount();
        
        // Count tiles with meshes
        var meshQuery = GetEntityQuery(typeof(MeshReference));
        int meshCount = meshQuery.CalculateEntityCount();
        
        _stopwatch.Stop();
        
        // Log metrics every second
        if (Time.ElapsedTime % 1.0 < Time.DeltaTime)
        {
            UnityEngine.Debug.Log($"[Metrics] Active Tiles: {activeTiles}, Meshed: {meshCount}, Query Time: {_stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        }
    }
}
```

---

## Integration with Existing Systems

### With NavMesh / AI Pathfinding

**Challenge:** NavMesh needs baked data, terrain is dynamic.

**Solution 1: Runtime NavMesh**
```csharp
using Unity.AI.Navigation;

// When tile mesh created:
NavMeshSurface surface = tileGameObject.AddComponent<NavMeshSurface>();
surface.BuildNavMesh();  // Bake dynamically
```

**Solution 2: Physics Raycasting**
```csharp
// AI uses physics raycasts instead of NavMesh:
RaycastHit hit;
if (Physics.Raycast(position + Vector3.up * 10, Vector3.down, out hit, 100f))
{
    float groundHeight = hit.point.y;
    // AI knows ground height
}
```

---

### With Water System

**Add water plane component:**

```csharp
public struct WaterPlane : IComponentData
{
    public float waterLevel;  // e.g., 0 (sea level)
}

// In mesh generation:
float height = SampleNoise(worldX, worldZ, config);

if (height < waterLevel)
{
    height = waterLevel;  // Clamp to water level (flat water surface)
    // Or: generate underwater terrain normally
}
```

---

### With Weather System

**Dynamic noise modification:**

```csharp
public struct WeatherState : IComponentData
{
    public float snowLevel;  // Height where snow appears
    public float rainAmount; // 0-1
}

// In mesh generation or material:
if (height > weatherState.snowLevel)
{
    // Use snow material
    // Or: set vertex color for snow shader
}
```

---

## Debugging Tools

### Visual Debugging Overlay

```csharp
public class TerrainDebugOverlay : MonoBehaviour
{
    void OnGUI()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        var query = em.CreateEntityQuery(typeof(TerrainTile));
        int tileCount = query.CalculateEntityCount();
        
        var config = em.CreateEntityQuery(typeof(TerrainTileConfig)).GetSingleton<TerrainTileConfig>();
        var offset = em.CreateEntityQuery(typeof(WorldOriginOffset)).GetSingleton<WorldOriginOffset>();
        
        GUI.Label(new Rect(10, 10, 300, 20), $"Active Tiles: {tileCount}");
        GUI.Label(new Rect(10, 30, 300, 20), $"View Distance: {config.viewDistance}m");
        GUI.Label(new Rect(10, 50, 300, 20), $"Tile Size: {config.tileSize}m");
        GUI.Label(new Rect(10, 70, 300, 20), $"World Offset: {offset.accumulatedOffset}");
        GUI.Label(new Rect(10, 90, 300, 20), $"FPS: {1f / Time.deltaTime:F1}");
    }
}
```

---

### Tile Highlight System

```csharp
// Show tile boundaries in Scene View:
public class TileHighlighter : MonoBehaviour
{
    void OnDrawGizmos()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile), typeof(LocalTransform));
        var entities = query.ToEntityArray(Allocator.Temp);
        
        var config = em.CreateEntityQuery(typeof(TerrainTileConfig)).GetSingleton<TerrainTileConfig>();
        
        foreach (var entity in entities)
        {
            var tile = em.GetComponentData<TerrainTile>(entity);
            var transform = em.GetComponentData<LocalTransform>(entity);
            
            // Draw tile bounds
            Gizmos.color = tile.meshGenerated ? Color.green : Color.red;
            Vector3 center = transform.Position + new float3(config.tileSize * 0.5f, 0, config.tileSize * 0.5f);
            Vector3 size = new Vector3(config.tileSize, 1, config.tileSize);
            Gizmos.DrawWireCube(center, size);
            
            // Draw grid coordinate label
            UnityEditor.Handles.Label(center, $"{tile.gridCoordinate}");
        }
        
        entities.Dispose();
    }
}
```

---

## Best Practices

### 1. Configuration Management

**Use ScriptableObject for presets:**

```csharp
[CreateAssetMenu(fileName = "TerrainPreset", menuName = "Terrain/Preset")]
public class TerrainPresetSO : ScriptableObject
{
    public string presetName;
    public float tileSize = 100f;
    public float viewDistance = 300f;
    public int verticesPerSide = 32;
    // ... all config fields ...
    
    public void ApplyTo(TerrainConfigAuthoring authoring)
    {
        authoring.tileSize = tileSize;
        authoring.viewDistance = viewDistance;
        // ... copy all fields ...
    }
}

// In Inspector: Add button to load presets
[CustomEditor(typeof(TerrainConfigAuthoring))]
public class TerrainConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        if (GUILayout.Button("Load Preset"))
        {
            // Show preset picker
        }
    }
}
```

---

### 2. Error Recovery

**Add validation:**

```csharp
// In TerrainMeshGenerationSystem:
if (config.verticesPerSide < 2)
{
    UnityEngine.Debug.LogError("verticesPerSide must be >= 2");
    return;
}

if (config.tileSize <= 0)
{
    UnityEngine.Debug.LogError("tileSize must be > 0");
    return;
}

if (vertexBuffer.Length != normalBuffer.Length)
{
    UnityEngine.Debug.LogError("Vertex and normal buffer size mismatch!");
    vertexBuffer.Clear();
    normalBuffer.Clear();
    return;
}
```

---

### 3. Memory Management

**Dispose all NativeContainers:**

```csharp
// Always use try-finally:
var tempArray = new NativeArray<float3>(100, Allocator.Temp);
try
{
    // Use array
}
finally
{
    if (tempArray.IsCreated)
        tempArray.Dispose();
}
```

**Or use using statement (Unity 2023.3+):**
```csharp
using var tempArray = new NativeArray<float3>(100, Allocator.Temp);
// Automatically disposed at end of scope
```

---

## Testing Utilities

### Unit Test: Verify Noise Consistency

```csharp
[Test]
public void TestNoiseConsistency()
{
    var config = new TerrainTileConfig
    {
        noiseFrequency = 0.01f,
        noiseAmplitude = 20f,
        noiseOctaves = 4,
        noiseLacunarity = 2.0f,
        noisePersistence = 0.5f
    };
    
    // Sample at same world position twice
    double worldX = 12345.6789;
    double worldZ = 98765.4321;
    
    float height1 = SampleNoise(worldX, worldZ, config);
    float height2 = SampleNoise(worldX, worldZ, config);
    
    Assert.AreEqual(height1, height2, 0.001f, "Noise should be deterministic");
}
```

---

### Integration Test: Verify Tile Spawning

```csharp
[UnityTest]
public IEnumerator TestTileSpawning()
{
    // Setup: Create test scene with TerrainConfig and Player
    // ...
    
    yield return null;  // Wait one frame
    
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    
    var tileQuery = em.CreateEntityQuery(typeof(TerrainTile));
    int tileCount = tileQuery.CalculateEntityCount();
    
    Assert.Greater(tileCount, 0, "Should have spawned tiles");
    Assert.Less(tileCount, 200, "Shouldn't spawn too many tiles");
}
```

---

## See Also

- [SYSTEM_ARCHITECTURE.md](SYSTEM_ARCHITECTURE.md) - System overview
- [API_REFERENCE.md](API_REFERENCE.md) - Complete API documentation
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Problem solving guide

