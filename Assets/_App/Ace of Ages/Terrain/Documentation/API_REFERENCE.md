# Infinite Terrain System - API Reference

**Last Updated:** March 14, 2026

## Table of Contents
1. [Components API](#components-api)
2. [Systems API](#systems-api)
3. [Authoring Components](#authoring-components)
4. [Public Methods](#public-methods)

---

## Components API

### Configuration Components (Singletons)

#### `TerrainTileConfig`

**Type:** `IComponentData` (struct, blittable)  
**Usage:** Singleton - one instance per world  
**Namespace:** Global

```csharp
public struct TerrainTileConfig : IComponentData
{
    public float tileSize;
    public float viewDistance;
    public int verticesPerSide;
    public float noiseFrequency;
    public float noiseAmplitude;
    public int noiseOctaves;
    public float noiseLacunarity;
    public float noisePersistence;
}
```

**Fields:**

| Field | Type | Description | Typical Range |
|-------|------|-------------|---------------|
| `tileSize` | float | Size of each terrain tile in meters | 50-200 |
| `viewDistance` | float | Distance from player that tiles remain active (meters) | 200-1000 |
| `verticesPerSide` | int | Number of vertices per side of tile mesh (forms N×N grid) | 16-64 |
| `noiseFrequency` | float | Base frequency for noise sampling (higher = more variation) | 0.005-0.05 |
| `noiseAmplitude` | float | Maximum height of terrain features (meters) | 5-100 |
| `noiseOctaves` | int | Number of noise layers to combine | 1-8 |
| `noiseLacunarity` | float | Frequency multiplier for each octave | 1.5-3.0 |
| `noisePersistence` | float | Amplitude multiplier for each octave | 0.25-0.75 |

**Access Pattern:**
```csharp
var config = SystemAPI.GetSingleton<TerrainTileConfig>();
float tileSize = config.tileSize;
```

**Created By:** `TerrainConfigAuthoring.Baker` during baking

---

#### `FloatingOriginConfig`

**Type:** `IComponentData` (struct, blittable)  
**Usage:** Singleton  
**Namespace:** Global

```csharp
public struct FloatingOriginConfig : IComponentData
{
    public float shiftThreshold;
    public bool enabled;
}
```

**Fields:**

| Field | Type | Description | Typical Value |
|-------|------|-------------|---------------|
| `shiftThreshold` | float | Distance from origin that triggers world shift (meters) | 1000-5000 |
| `enabled` | bool | Master switch for floating origin system | true |

**Usage:**
```csharp
var config = SystemAPI.GetSingleton<FloatingOriginConfig>();
if (config.enabled && distanceFromOrigin > config.shiftThreshold)
{
    // Trigger shift
}
```

**Created By:** `TerrainConfigAuthoring.Baker`

---

#### `WorldOriginOffset`

**Type:** `IComponentData` (struct, blittable)  
**Usage:** Singleton  
**Namespace:** Global

```csharp
public struct WorldOriginOffset : IComponentData
{
    public double3 accumulatedOffset;
}
```

**Fields:**

| Field | Type | Description | Range |
|-------|------|-------------|-------|
| `accumulatedOffset` | double3 | Cumulative offset subtracted from all entities (meters) | Unlimited |

**Important:** Uses `double3` (not `float3`) for extended precision.

**Precision Comparison:**
- `float`: ~7 decimal digits (~10⁷ range before precision loss)
- `double`: ~15 decimal digits (~10¹⁵ range)

**Access Pattern:**
```csharp
// Read-only
var offset = SystemAPI.GetSingleton<WorldOriginOffset>();
double3 trueWorldPos = entityPos + offset.accumulatedOffset;

// Read-write
RefRW<WorldOriginOffset> offsetRef = SystemAPI.GetSingletonRW<WorldOriginOffset>();
offsetRef.ValueRW.accumulatedOffset += shiftAmount;
```

**Created By:** `TerrainConfigAuthoring.Baker` (initialized to zero)  
**Modified By:** `FloatingOriginSystem` (during world shifts)

---

### Tile Components

#### `TerrainTile`

**Type:** `IComponentData` (struct, blittable)  
**Usage:** Per-entity (one per tile)  
**Namespace:** Global

```csharp
public struct TerrainTile : IComponentData
{
    public int2 gridCoordinate;
    public bool meshGenerated;
    public bool needsRegeneration;
}
```

**Fields:**

| Field | Type | Description | Example |
|-------|------|-------------|---------|
| `gridCoordinate` | int2 | Position in tile grid (x, z) | (0, 0), (2, -1) |
| `meshGenerated` | bool | True if mesh data has been generated | true |
| `needsRegeneration` | bool | True if mesh needs to be regenerated (after origin shift or modification) | false |

**Grid Coordinate to World Position:**
```csharp
float3 worldPosition = new float3(
    gridCoordinate.x * tileSize,
    0,
    gridCoordinate.y * tileSize
);
```

**State Transitions:**
```
Created: meshGenerated=false, needsRegeneration=false
    ↓
Generated: meshGenerated=true, needsRegeneration=false
    ↓
Modified: meshGenerated=true, needsRegeneration=true
    ↓
Regenerated: meshGenerated=true, needsRegeneration=false
```

**Created By:** `TileSpawningSystem`  
**Modified By:** `TerrainMeshGenerationSystem`

---

#### `MeshReference`

**Type:** `IComponentData` (class, managed)  
**Usage:** Per-entity (one per tile)  
**Namespace:** Global

```csharp
public class MeshReference : IComponentData
{
    public UnityEngine.Mesh mesh;
}
```

**Fields:**

| Field | Type | Description | Lifetime |
|-------|------|-------------|----------|
| `mesh` | UnityEngine.Mesh | Reference to Unity mesh object | Until tile despawns |

**Note:** This is a **managed component** (class, not struct) because it holds a reference to a Unity Object.

**Cleanup:**
```csharp
// In TerrainRenderingSystem.OnDestroy:
var meshRef = EntityManager.GetComponentData<MeshReference>(entity);
if (meshRef.mesh != null)
    Object.Destroy(meshRef.mesh);  // Prevent memory leak
```

**Created By:** `TerrainRenderingSystem`

---

### Tag Components

#### `FloatingOriginEnabled`

**Type:** `IComponentData` (struct, empty)  
**Usage:** Tag - per-entity  
**Namespace:** Global

```csharp
public struct FloatingOriginEnabled : IComponentData
{
}
```

**Purpose:** Marks entities that should have their positions adjusted during world origin shifts.

**Should Be Added To:**
- Terrain tiles (automatically added by `TileSpawningSystem`)
- Player entity (via `FloatingOriginEnabledAuthoring` or manual)
- Any world-space objects (trees, buildings, etc.)

**Should NOT Be Added To:**
- UI elements (screen-space)
- Camera (typically parented to player)
- Entities that should stay at absolute origin

**Usage in Jobs:**
```csharp
[BurstCompile]
[WithAll(typeof(FloatingOriginEnabled))]  // Only process tagged entities
public partial struct ShiftWorldOriginJob : IJobEntity
{
    public float3 offset;
    public void Execute(ref LocalTransform transform)
    {
        transform.Position -= offset;
    }
}
```

**Created By:** `TileSpawningSystem` (for tiles), user-placed authoring components (for other objects)

---

#### `PlayerTag`

**Type:** `IComponentData` (struct, empty)  
**Usage:** Tag - singleton entity  
**Namespace:** Global

```csharp
public struct PlayerTag : IComponentData
{
}
```

**Purpose:** Identifies the player entity for terrain systems to track.

**Requirements:**
- Must be on exactly one entity
- Entity must have `LocalTransform` component
- Should have `FloatingOriginEnabled` tag

**Usage:**
```csharp
var playerEntity = SystemAPI.GetSingletonEntity<PlayerTag>();
var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
float3 playerPosition = playerTransform.Position;
```

**Created By:** `PlayerTagAuthoring` (place on player GameObject)

---

### Buffer Components

#### `VertexElement`

**Type:** `IBufferElementData` (struct, blittable)  
**Usage:** Dynamic buffer per tile entity  
**Namespace:** Global

```csharp
public struct VertexElement : IBufferElementData
{
    public float3 value;
}
```

**Contents:** Vertex positions in tile-local space (relative to tile origin).

**Example:**
```csharp
var vertexBuffer = EntityManager.GetBuffer<VertexElement>(tileEntity);
float3 firstVertex = vertexBuffer[0].value;  // e.g., (0, 5.2, 0)
```

---

#### `NormalElement`

**Type:** `IBufferElementData` (struct, blittable)  
**Usage:** Dynamic buffer per tile entity  
**Namespace:** Global

```csharp
public struct NormalElement : IBufferElementData
{
    public float3 value;
}
```

**Contents:** Vertex normals (unit vectors).

**Example:**
```csharp
var normalBuffer = EntityManager.GetBuffer<NormalElement>(tileEntity);
float3 firstNormal = normalBuffer[0].value;  // e.g., (0.1, 0.995, 0)
```

---

#### `UVElement`

**Type:** `IBufferElementData` (struct, blittable)  
**Usage:** Dynamic buffer per tile entity  
**Namespace:** Global

```csharp
public struct UVElement : IBufferElementData
{
    public float2 value;
}
```

**Contents:** Texture coordinates in [0, 1] range.

**Example:**
```csharp
var uvBuffer = EntityManager.GetBuffer<UVElement>(tileEntity);
float2 firstUV = uvBuffer[0].value;  // e.g., (0, 0)
```

---

#### `IndexElement`

**Type:** `IBufferElementData` (struct, blittable)  
**Usage:** Dynamic buffer per tile entity  
**Namespace:** Global

```csharp
public struct IndexElement : IBufferElementData
{
    public int value;
}
```

**Contents:** Triangle indices referencing vertices.

**Example:**
```csharp
var indexBuffer = EntityManager.GetBuffer<IndexElement>(tileEntity);
int firstIndex = indexBuffer[0].value;  // e.g., 0

// Indices are grouped in threes:
// [0, 1, 2] = first triangle
// [3, 4, 5] = second triangle
```

---

## Systems API

### `TileSpawningSystem`

**Type:** `ISystem` (struct)  
**Update Group:** `SimulationSystemGroup`  
**Update Order:** Before `TransformSystemGroup`  
**Namespace:** Global

```csharp
public partial struct TileSpawningSystem : ISystem
{
    public void OnCreate(ref SystemState state);
    public void OnDestroy(ref SystemState state);
    public void OnUpdate(ref SystemState state);
}
```

**Lifecycle:**

| Method | When Called | Purpose |
|--------|-------------|---------|
| `OnCreate` | System initialization | Create `NativeParallelHashMap`, set up queries |
| `OnUpdate` | Every frame | Spawn/despawn tiles based on player position |
| `OnDestroy` | System shutdown | Dispose `NativeParallelHashMap` |

**Dependencies:**
- Requires: `PlayerTag`, `TerrainTileConfig`, `WorldOriginOffset`
- Creates: `TerrainTile` entities with buffers
- Modifies: `_activeTiles` HashMap

**Query Requirements:**
```csharp
state.RequireForUpdate<PlayerTag>();
state.RequireForUpdate<TerrainTileConfig>();
state.RequireForUpdate<WorldOriginOffset>();
```

System won't run until these singletons exist.

**Key Data Structures:**
```csharp
private NativeParallelHashMap<int2, Entity> _activeTiles;
```

**Performance:**
- **Best Case:** No tiles to spawn/despawn = ~0.1ms
- **Worst Case:** 20 tiles spawned = ~2ms
- **Memory:** HashMap overhead + 16 bytes per active tile entry

---

### `TerrainMeshGenerationSystem`

**Type:** `ISystem` (struct, partial)  
**Update Group:** `SimulationSystemGroup`  
**Update Order:** After `TileSpawningSystem`  
**Namespace:** Global

```csharp
public partial struct TerrainMeshGenerationSystem : ISystem
{
    public void OnCreate(ref SystemState state);
    public void OnUpdate(ref SystemState state);
    
    // Private helper methods:
    private void GenerateTileMesh(...);
    [BurstCompile] private static float SampleNoise(...);
    [BurstCompile] private static float3 CalculateNormal(...);
}
```

**Lifecycle:**

| Method | When Called | Purpose |
|--------|-------------|---------|
| `OnCreate` | System initialization | Set up requirements |
| `OnUpdate` | Every frame | Generate meshes for tiles that need them |

**Dependencies:**
- Requires: `TerrainTileConfig`, `WorldOriginOffset`
- Reads: `TerrainTile`, `WorldOriginOffset`
- Writes: `VertexElement`, `NormalElement`, `UVElement`, `IndexElement` buffers

**Query:**
```csharp
SystemAPI.QueryBuilder()
    .WithAll<TerrainTile, VertexElement, NormalElement, UVElement, IndexElement>()
    .Build()
```

Processes all tiles, but only generates mesh if `tile.meshGenerated == false` or `tile.needsRegeneration == true`.

**Performance:**
- **Per Tile (32x32):** ~0.5-1ms
- **Burst Compiled:** Noise sampling only (main loop not Burst-able due to buffer access)

---

### `TerrainPhysicsSystem`

**Type:** `SystemBase` (class, managed)  
**Update Group:** `SimulationSystemGroup`  
**Update Order:** After `TerrainMeshGenerationSystem`  
**Namespace:** Global

```csharp
public partial class TerrainPhysicsSystem : SystemBase
{
    protected override void OnCreate();
    protected override void OnUpdate();
    protected override void OnDestroy();
    
    private void CreatePhysicsCollider(...);
}
```

**Lifecycle:**

| Method | When Called | Purpose |
|--------|-------------|---------|
| `OnCreate` | System initialization | Create entity query |
| `OnUpdate` | Every frame | Create colliders for tiles that need them |
| `OnDestroy` | System shutdown | Dispose all physics colliders |

**Dependencies:**
- Requires: `TerrainTileConfig`
- Reads: `TerrainTile`, `VertexElement`, `IndexElement`
- Writes: `PhysicsCollider`, `PhysicsWorldIndex`

**Query:**
```csharp
GetEntityQuery(
    ComponentType.ReadOnly<TerrainTile>(),
    ComponentType.ReadOnly<VertexElement>(),
    ComponentType.ReadOnly<IndexElement>(),
    ComponentType.Exclude<PhysicsCollider>()  // Only tiles without collider
);
```

**Performance:**
- **Per Tile (32x32):** ~1-2ms
- **Not Burst Compiled** (uses managed Unity.Physics API)

**Cleanup on Destroy:**
```csharp
protected override void OnDestroy()
{
    foreach (var entity in query.ToEntityArray(Allocator.Temp))
    {
        var collider = EntityManager.GetComponentData<PhysicsCollider>(entity);
        if (collider.IsValid)
            collider.Value.Dispose();  // Important: prevent memory leak
    }
}
```

---

### `TerrainRenderingSystem`

**Type:** `SystemBase` (class, managed)  
**Update Group:** `PresentationSystemGroup`  
**Namespace:** Global

```csharp
public partial class TerrainRenderingSystem : SystemBase
{
    protected override void OnCreate();
    protected override void OnStartRunning();
    protected override void OnUpdate();
    protected override void OnDestroy();
    
    private void CreateAndAssignMesh(...);
}
```

**Lifecycle:**

| Method | When Called | Purpose |
|--------|-------------|---------|
| `OnCreate` | System initialization | Create entity query |
| `OnStartRunning` | First frame system runs | Load/create terrain material |
| `OnUpdate` | Every frame | Convert buffers to Unity meshes, set up rendering |
| `OnDestroy` | System shutdown | Destroy all mesh objects |

**Dependencies:**
- Requires: `TerrainTileConfig`
- Reads: `TerrainTile`, mesh buffers
- Writes: `MeshReference`, Entities Graphics components

**Query:**
```csharp
GetEntityQuery(
    ComponentType.ReadOnly<TerrainTile>(),
    ComponentType.ReadOnly<VertexElement>(),
    ComponentType.ReadOnly<IndexElement>(),
    ComponentType.Exclude<MeshReference>()  // Only tiles without mesh
);
```

**Material Loading:**
```csharp
protected override void OnStartRunning()
{
    _terrainMaterial = Resources.Load<Material>("TerrainMaterial");
    if (_terrainMaterial == null)
    {
        // Create fallback material with URP Lit shader
        _terrainMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
    }
}
```

**Performance:**
- **Per Tile:** ~0.2-0.5ms
- **Not Burst Compiled** (uses managed Mesh API)

**Cleanup on Destroy:**
```csharp
protected override void OnDestroy()
{
    foreach (var entity in query.ToEntityArray(Allocator.Temp))
    {
        var meshRef = EntityManager.GetComponentData<MeshReference>(entity);
        if (meshRef.mesh != null)
            Object.Destroy(meshRef.mesh);  // Important: prevent memory leak
    }
}
```

---

### `FloatingOriginSystem`

**Type:** `ISystem` (struct)  
**Update Group:** `TransformSystemGroup`  
**Update Order:** After `LocalToWorldSystem`  
**Namespace:** Global

```csharp
public partial struct FloatingOriginSystem : ISystem
{
    [BurstCompile] public void OnCreate(ref SystemState state);
    [BurstCompile] public void OnUpdate(ref SystemState state);
}
```

**Lifecycle:**

| Method | When Called | Purpose |
|--------|-------------|---------|
| `OnCreate` | System initialization | Set up requirements |
| `OnUpdate` | Every frame | Check player distance, trigger shifts |

**Dependencies:**
- Requires: `PlayerTag`, `FloatingOriginConfig`, `WorldOriginOffset`
- Reads: `LocalTransform` (player)
- Writes: `WorldOriginOffset`, all `FloatingOriginEnabled` entities' `LocalTransform`

**Shift Trigger Logic:**
```csharp
float distanceFromOrigin = math.length(playerPosition);
if (distanceFromOrigin > config.shiftThreshold)
{
    // Shift triggered
}
```

**Performance:**
- **Idle (no shift):** ~0.05ms
- **During shift (100 entities):** ~0.5ms
- **Burst Compiled:** Yes (including parallel job)

---

### `TerrainRenderingDebugSystem`

**Type:** `SystemBase` (class, managed)  
**Update Group:** `SimulationSystemGroup`  
**Namespace:** Global

```csharp
public partial class TerrainRenderingDebugSystem : SystemBase
{
    protected override void OnCreate();
    protected override void OnUpdate();
}
```

**Purpose:** Debug logging system that reports terrain tile status every 2 seconds.

**Output Example:**
```
[TerrainDebug] ========== Terrain Tile Analysis ==========
[TerrainDebug] Total tiles: 9
[TerrainDebug] Tiles with mesh data: 9
[TerrainDebug] Tiles with rendering components: 9
[TerrainDebug] Sample tile at (0, 0): Entity(1:123)
```

**Enable/Disable:** Comment out system update to disable logging.

---

## Authoring Components

### `TerrainConfigAuthoring`

**Type:** `MonoBehaviour` (authoring component)  
**Location:** Place on GameObject in scene/SubScene  
**Namespace:** Global

```csharp
public class TerrainConfigAuthoring : MonoBehaviour
{
    // Tile Settings
    public float tileSize = 100f;
    public float viewDistance = 500f;
    public int verticesPerSide = 32;
    
    // Floating Origin
    public bool floatingOriginEnabled = true;
    public float shiftThreshold = 2000f;
    
    // Procedural Noise
    public float noiseFrequency = 0.01f;
    public float noiseAmplitude = 20f;
    public int noiseOctaves = 4;
    public float noiseLacunarity = 2.0f;
    public float noisePersistence = 0.5f;
    
    // Material
    public Material terrainMaterial;
}
```

**Baker Implementation:**
```csharp
public class Baker : Baker<TerrainConfigAuthoring>
{
    public override void Bake(TerrainConfigAuthoring authoring)
    {
        Entity entity = GetEntity(TransformUsageFlags.None);
        
        AddComponent(entity, new TerrainTileConfig { ... });
        AddComponent(entity, new FloatingOriginConfig { ... });
        AddComponent(entity, new WorldOriginOffset { ... });
    }
}
```

**Gizmo Visualization:**
- Green sphere: View distance
- Yellow sphere: Shift threshold
- Cyan square: Current tile at camera position

**Setup:**
1. Create GameObject: "TerrainConfig"
2. Add Component: TerrainConfigAuthoring
3. Configure in Inspector
4. Place in SubScene (recommended) or regular scene

---

### `PlayerTagAuthoring`

**Type:** `MonoBehaviour` (authoring component)  
**Location:** Place on player GameObject  
**Namespace:** Global  
**File:** `Assets/_App/Ace of Ages/DOTSAuthoring/PlayerTagAuthoring.cs`

```csharp
public class PlayerTagAuthoring : MonoBehaviour
{
    public class Baker : Baker<PlayerTagAuthoring>
    {
        public override void Bake(PlayerTagAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
        }
    }
}
```

**Setup:**
1. Add to player GameObject (e.g., XR Origin or Camera Offset)
2. Ensure player has Transform (will be converted to LocalTransform in baking)
3. System will automatically track this entity

---

### `FloatingOriginEnabledAuthoring`

**Type:** `MonoBehaviour` (authoring component)  
**Location:** Place on any GameObject that should shift with world origin  
**Namespace:** Global  
**File:** `Assets/_App/Ace of Ages/Terrain/FloatingOriginEnabledAuthoring.cs`

```csharp
public class FloatingOriginEnabledAuthoring : MonoBehaviour
{
    public class Baker : Baker<FloatingOriginEnabledAuthoring>
    {
        public override void Bake(FloatingOriginEnabledAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<FloatingOriginEnabled>(entity);
        }
    }
}
```

**Setup:**
1. Add to player GameObject
2. Add to any world-space objects (trees, buildings, NPCs)
3. Objects without this tag will NOT shift (stay at absolute world position)

---

## Public Methods

### Helper Functions

#### `SampleNoise`

**Location:** `TerrainMeshGenerationSystem.cs`  
**Signature:**
```csharp
[BurstCompile]
private static float SampleNoise(double worldX, double worldZ, TerrainTileConfig config)
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `worldX` | double | X coordinate in world space (true position) |
| `worldZ` | double | Z coordinate in world space (true position) |
| `config` | TerrainTileConfig | Noise parameters |

**Returns:** `float` - Height value in meters

**Usage:**
```csharp
double worldX = tilePosition.x + localX + accumulatedOffset.x;
double worldZ = tilePosition.z + localZ + accumulatedOffset.z;
float height = SampleNoise(worldX, worldZ, config);
```

**Performance:** ~100ns per call (Burst-compiled)

---

#### `CalculateNormal`

**Location:** `TerrainMeshGenerationSystem.cs`  
**Signature:**
```csharp
[BurstCompile]
private static float3 CalculateNormal(
    int x, int z, 
    NativeArray<float3> vertices, 
    int verticesPerSide)
```

**Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `x` | int | X index in vertex grid [0, verticesPerSide-1] |
| `z` | int | Z index in vertex grid [0, verticesPerSide-1] |
| `vertices` | NativeArray<float3> | All vertices in tile |
| `verticesPerSide` | int | Grid dimension |

**Returns:** `float3` - Normalized normal vector

**Algorithm:** Averages normals of up to 4 adjacent triangles.

**Performance:** ~50ns per call (Burst-compiled)

---

## Usage Examples

### Example 1: Accessing Singleton Configuration

```csharp
public partial struct MyCustomSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // Get terrain config
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Use configuration
        float maxHeight = config.noiseAmplitude;
        int resolution = config.verticesPerSide;
        
        UnityEngine.Debug.Log($"Terrain tiles are {config.tileSize}m with {resolution} vertices per side");
    }
}
```

---

### Example 2: Querying Active Tiles

```csharp
public partial class MyTerrainAnalyzer : SystemBase
{
    protected override void OnUpdate()
    {
        int tileCount = 0;
        
        Entities
            .WithAll<TerrainTile>()
            .ForEach((Entity entity, in TerrainTile tile) =>
            {
                tileCount++;
                UnityEngine.Debug.Log($"Tile {tileCount}: Grid {tile.gridCoordinate}, Generated: {tile.meshGenerated}");
            })
            .WithoutBurst()  // Burst can't call Debug.Log
            .Run();
        
        UnityEngine.Debug.Log($"Total active tiles: {tileCount}");
    }
}
```

---

### Example 3: Getting Mesh Data from Buffer

```csharp
public partial class MeshDataReader : SystemBase
{
    protected override void OnUpdate()
    {
        var entity = SystemAPI.GetSingletonEntity<TerrainTile>();
        var vertices = EntityManager.GetBuffer<VertexElement>(entity);
        
        UnityEngine.Debug.Log($"First vertex: {vertices[0].value}");
        UnityEngine.Debug.Log($"Total vertices: {vertices.Length}");
        
        // Convert to array if needed
        float3[] vertexArray = new float3[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertexArray[i] = vertices[i].value;
        }
    }
}
```

---

### Example 4: Manually Triggering Mesh Regeneration

```csharp
public partial class TerrainModifier : SystemBase
{
    protected override void OnUpdate()
    {
        // Find a specific tile
        Entities
            .WithAll<TerrainTile>()
            .ForEach((ref TerrainTile tile) =>
            {
                if (tile.gridCoordinate.Equals(new int2(0, 0)))
                {
                    // Mark for regeneration
                    tile.needsRegeneration = true;
                    UnityEngine.Debug.Log("Marked tile (0,0) for regeneration");
                }
            })
            .Run();
    }
}
```

**Next Frame:** `TerrainMeshGenerationSystem` will see `needsRegeneration == true` and regenerate the mesh.

---

### Example 5: Custom Noise Function

To replace the noise function:

```csharp
// In TerrainMeshGenerationSystem.cs, replace SampleNoise with:

[BurstCompile]
private static float SampleCustomNoise(double worldX, double worldZ, TerrainTileConfig config)
{
    // Example: Ridged noise (abs of Perlin)
    float total = 0f;
    float frequency = config.noiseFrequency;
    float amplitude = config.noiseAmplitude;
    
    for (int i = 0; i < config.noiseOctaves; i++)
    {
        float2 samplePos = new float2((float)worldX, (float)worldZ) * frequency;
        float noiseValue = noise.snoise(samplePos);
        
        // Ridge effect: abs creates sharp peaks
        noiseValue = math.abs(noiseValue);
        
        total += noiseValue * amplitude;
        amplitude *= config.noisePersistence;
        frequency *= config.noiseLacunarity;
    }
    
    return total * 0.5f;  // Scale down (ridges are additive)
}
```

---

### Example 6: Adding Custom Component to Tiles

```csharp
public struct TileBiomeType : IComponentData
{
    public int biomeID;  // 0=grass, 1=desert, 2=snow, etc.
}

// In TileSpawningSystem.cs, modify entity creation:
foreach (var gridCoord in tilesToSpawn)
{
    Entity tileEntity = ecb.CreateEntity();
    
    // ... existing components ...
    
    // Add custom component
    ecb.AddComponent(tileEntity, new TileBiomeType
    {
        biomeID = CalculateBiomeID(gridCoord)  // Your logic here
    });
}

// Then in TerrainMeshGenerationSystem, read it:
var biome = SystemAPI.GetComponent<TileBiomeType>(entity);
if (biome.biomeID == 0)
    // Use grass noise parameters
else if (biome.biomeID == 1)
    // Use desert noise parameters
```

---

### Example 7: Accessing Accumulated Offset

```csharp
public partial struct MySystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var worldOffset = SystemAPI.GetSingleton<WorldOriginOffset>();
        
        UnityEngine.Debug.Log($"World has shifted by: {worldOffset.accumulatedOffset} meters");
        UnityEngine.Debug.Log($"Total distance traveled: {math.length(worldOffset.accumulatedOffset)} meters");
        
        // Convert entity position to "true" world position
        float3 entityPos = new float3(100, 0, 50);
        double3 trueWorldPos = entityPos + worldOffset.accumulatedOffset;
    }
}
```

---

## Constants & Magic Numbers

### Hardcoded Values in Systems

| Value | Location | Purpose | Can Change? |
|-------|----------|---------|-------------|
| `256` | TileSpawningSystem | Initial HashMap capacity | Yes - affects memory allocation only |
| `0` | TerrainRenderingSystem | Default Unity layer | Yes - change to put terrain on different layer |
| `1` | TerrainRenderingSystem | Default rendering layer mask | Yes - for URP layer filtering |
| `1u << 0` | TerrainPhysicsSystem | Physics layer 0 | Yes - change collision layer |
| `~0u` | TerrainPhysicsSystem | Collides with all layers | Yes - restrict collision layers |

### Component Counts

**For 32x32 vertex tile:**
- Vertices: 1,024
- Normals: 1,024
- UVs: 1,024
- Indices: 5,766 (1,922 triangles * 3)

**For 64x64 vertex tile:**
- Vertices: 4,096
- Normals: 4,096
- UVs: 4,096
- Indices: 23,814 (7,938 triangles * 3)

**Memory scaling:** O(n²) where n = verticesPerSide

---

## Thread Safety & Burst Compilation

### Burst-Compiled Functions

| Function | System | Burst Compiled | Performance Gain |
|----------|--------|----------------|------------------|
| `OnCreate` | TileSpawningSystem | ✅ Yes | 2x |
| `OnDestroy` | TileSpawningSystem | ✅ Yes | 2x |
| `OnUpdate` | TileSpawningSystem | ❌ No (uses Debug.Log) | - |
| `SampleNoise` | TerrainMeshGenerationSystem | ✅ Yes | 10x |
| `CalculateNormal` | TerrainMeshGenerationSystem | ✅ Yes | 8x |
| `ShiftWorldOriginJob` | FloatingOriginSystem | ✅ Yes | 12x |
| `OnUpdate` | FloatingOriginSystem | ✅ Yes | 5x |

### Parallel Job Execution

**ShiftWorldOriginJob:**
```csharp
[BurstCompile]
[WithAll(typeof(FloatingOriginEnabled))]
public partial struct ShiftWorldOriginJob : IJobEntity
{
    public float3 offset;
    
    public void Execute(ref LocalTransform transform)
    {
        transform.Position -= offset;  // Thread-safe: each entity independent
    }
}

// Scheduled in FloatingOriginSystem:
shiftJob.ScheduleParallel();  // Runs on multiple worker threads
```

**Thread Safety:** Each entity processed independently, no shared state modifications.

---

## Error Handling

### Common Errors

#### "PlayerTag not found"
**Cause:** No entity with PlayerTag in scene  
**Fix:** Add `PlayerTagAuthoring` to player GameObject

#### "TerrainMaterial not found in Resources"
**Cause:** Material missing from Resources folder  
**Fix:** Run Tools → Terrain → Create Terrain Material (or let `TerrainMaterialCreator` run on startup)

#### "Failed to add render components"
**Cause:** EntitiesGraphicsSystem not available or material/mesh invalid  
**Fix:** Ensure Entities Graphics package installed, check material shader

#### "Failed to create collider"
**Cause:** Invalid mesh data (empty buffers, negative indices)  
**Fix:** Check console for mesh generation errors, verify vertices > 0

### Defensive Programming

**Null Checks:**
```csharp
if (_terrainMaterial == null)
{
    Debug.LogWarning("Material is null, skipping...");
    return;
}
```

**Buffer Validation:**
```csharp
if (vertices.Length > 0 && indices.Length > 0)
{
    CreateAndAssignMesh(...);
}
else
{
    Debug.LogWarning($"Tile has empty buffers!");
}
```

**Component Checks:**
```csharp
if (!SystemAPI.HasComponent<LocalTransform>(playerEntity))
    return;  // Skip frame if player not ready
```

---

## Integration Examples

### Example: Getting Height at World Position

```csharp
public static float GetTerrainHeightAt(float3 worldPosition, TerrainTileConfig config, WorldOriginOffset offset)
{
    // Convert to true world position
    double3 trueWorldPos = worldPosition + offset.accumulatedOffset;
    
    // Sample noise (same function terrain uses)
    float height = SampleNoise(trueWorldPos.x, trueWorldPos.z, config);
    
    return height;
}
```

**Use Case:** AI pathfinding, object placement, effect spawning

---

### Example: Spawning Objects on Terrain

```csharp
public partial class TreeSpawner : SystemBase
{
    protected override void OnUpdate()
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        var offset = SystemAPI.GetSingleton<WorldOriginOffset>();
        
        Entities
            .WithAll<TerrainTile>()
            .ForEach((Entity tileEntity, in TerrainTile tile) =>
            {
                if (!tile.meshGenerated) return;
                
                // Get tile world position
                var transform = SystemAPI.GetComponent<LocalTransform>(tileEntity);
                float3 tilePos = transform.Position;
                
                // Spawn tree at random position on tile
                float3 localPos = new float3(
                    UnityEngine.Random.Range(0, config.tileSize),
                    0,
                    UnityEngine.Random.Range(0, config.tileSize)
                );
                
                float3 worldPos = tilePos + localPos;
                
                // Get height at this position
                double3 truePos = worldPos + offset.accumulatedOffset;
                float height = SampleNoise(truePos.x, truePos.z, config);
                
                // Create tree at (worldPos.x, height, worldPos.z)
                // ... tree creation logic ...
            })
            .WithoutBurst()
            .Run();
    }
}
```

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | March 2026 | Initial implementation with all core systems |
| 1.1 | March 2026 | Added rendering debug system, material auto-creation |

---

## See Also

- [SYSTEM_ARCHITECTURE.md](SYSTEM_ARCHITECTURE.md) - High-level overview
- [TECHNICAL_DETAILS.md](TECHNICAL_DETAILS.md) - Deep implementation details
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Common issues and fixes

