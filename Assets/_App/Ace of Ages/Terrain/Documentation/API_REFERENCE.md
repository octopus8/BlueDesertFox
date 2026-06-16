# API Reference - Complete Component and System API

Complete API documentation with code examples for all terrain components and systems.

## Table of Contents

- [Components](#components)
  - [Configuration](#configuration-components)
  - [Runtime State](#runtime-state-components)
  - [Per-Tile](#per-tile-components)
  - [Buffers](#buffer-components)
  - [Physics](#physics-components)
- [Systems](#systems)
- [Enums](#enums)
- [Utility Functions](#utility-functions)
- [Code Examples](#code-examples)

---

## Components

### Configuration Components

#### TerrainTileConfig

```csharp
public struct TerrainTileConfig : IComponentData
```

**Purpose**: Global terrain configuration singleton.

**Fields**:
| Field | Type | Description |
|-------|------|-------------|
| `tileSize` | float | Size of each tile in meters (e.g., 100) |
| `viewDistance` | float | Maximum render distance in meters (e.g., 500) |
| `verticesPerSide` | int | Vertices per tile edge (e.g., 32 = 32×32 mesh) |
| `noiseFrequency` | float | Base noise sampling frequency (e.g., 0.01) |
| `noiseAmplitude` | float | Maximum height variation in meters (e.g., 20) |
| `noiseOctaves` | int | Number of noise layers (e.g., 4) |
| `noiseLacunarity` | float | Frequency multiplier per octave (e.g., 2.0) |
| `noisePersistence` | float | Amplitude multiplier per octave (e.g., 0.5) |
| `maxCollidersCreatedPerFrame` | int | Frame budget for collider creation (e.g., 3) |
| `lodFullResolutionDistance` | float | Full-res collider threshold (e.g., 150) |
| `lodHalfResolutionDistance` | float | Half-res collider threshold (e.g., 300) |
| `lodQuarterResolutionDistance` | float | Quarter-res collider threshold (e.g., 450) |
| `maxColliderCacheMemoryMB` | int | Cache memory limit in MB (e.g., 50) |
| `usePhysicsLODLayers` | bool | Enable physics layer separation |
| `closeTerrainPhysicsLayer` | int | Physics layer for close terrain (layer dropdown in Inspector) |
| `lowDetailPhysicsLayer` | int | Physics layer for LOD tiles (layer dropdown in Inspector) |

**Usage**:
```csharp
var config = SystemAPI.GetSingleton<TerrainTileConfig>();
float tileSize = config.tileSize;
```

---

#### ScrollConfig

```csharp
public struct ScrollConfig : IComponentData
```

**Purpose**: Auto-scrolling configuration singleton.

**Fields**:
| Field | Type | Description |
|-------|------|-------------|
| `enabled` | bool | Enable auto-scrolling |
| `scrollSpeed` | float | Scroll speed in m/s (positive = forward) |

**Usage**:
```csharp
// Get config
var config = SystemAPI.GetSingleton<ScrollConfig>();

// Modify at runtime
var query = em.CreateEntityQuery(typeof(ScrollConfig));
var entity = query.GetSingletonEntity();
em.SetComponentData(entity, new ScrollConfig 
{ 
    enabled = true, 
    scrollSpeed = 10f 
});
query.Dispose();
```

---

#### PlayerTrackingSearch

```csharp
public struct PlayerTrackingSearch : IComponentData
```

**Purpose**: Search parameters for finding player GameObject.

**Nested Types**:
```csharp
public enum Mode : byte
{
    FindByName = 0,
    FindByTag = 1,
    FindMainCamera = 2
}
```

**Fields**:
| Field | Type | Description |
|-------|------|-------------|
| `mode` | Mode | How to search for player |
| `searchString` | FixedString128Bytes | Name or tag to search |
| `initialized` | bool | True after player found |

**Usage**:
```csharp
var search = SystemAPI.GetSingleton<PlayerTrackingSearch>();
if (search.initialized)
{
    // Player found, tracking active
}
```

---

### Runtime State Components

#### ScrollOffset

```csharp
public struct ScrollOffset : IComponentData
```

**Purpose**: Tracks accumulated scroll distance.

**Fields**:
| Field | Type | Description |
|-------|------|-------------|
| `accumulatedOffset` | float3 | Total scroll distance (XZ plane, Y=0) |

**Usage**:
```csharp
// Read current offset
var offset = SystemAPI.GetSingleton<ScrollOffset>();
float totalDistance = math.length(offset.accumulatedOffset);

// Reset offset
var query = em.CreateEntityQuery(typeof(ScrollOffset));
var entity = query.GetSingletonEntity();
em.SetComponentData(entity, new ScrollOffset { accumulatedOffset = float3.zero });
query.Dispose();
```

---

#### PlayerTransformReference

```csharp
public class PlayerTransformReference : IComponentData
```

**Purpose**: Managed reference to player GameObject's Transform.

**Fields**:
| Field | Type | Description |
|-------|------|-------------|
| `playerTransform` | Transform | Player Transform reference |

**Usage**:
```csharp
// Get player position (from non-Burst system)
var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
if (playerRef != null && playerRef.playerTransform != null)
{
    float3 playerPosition = playerRef.playerTransform.position;
}
```

**Note**: Must be class (managed) - cannot use in Burst-compiled code.

---

### Per-Tile Components

#### TerrainTile

```csharp
public struct TerrainTile : IComponentData
```

**Purpose**: Identifies terrain tile entity and tracks state.

**Fields**:
| Field | Type | Description |
|-------|------|-------------|
| `gridCoordinate` | int2 | Grid position (e.g., (0,0), (1,-2)) |
| `meshGenerated` | bool | True if mesh data populated |
| `needsRegeneration` | bool | True if mesh needs regeneration |

**Usage**:
```csharp
foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>().WithEntityAccess())
{
    int2 gridPos = tile.ValueRO.gridCoordinate;
    bool hasData = tile.ValueRO.meshGenerated;
}
```

---

#### TerrainTileDistanceToPlayer

```csharp
public struct TerrainTileDistanceToPlayer : IComponentData
```

**Purpose**: Caches tile distance and LOD level.

**Fields**:
| Field | Type | Description |
|-------|------|-------------|
| `distance` | float | Distance to player in meters |
| `lodLevel` | TerrainPhysicsLODLevel | Current physics LOD level |

**Usage**:
```csharp
if (SystemAPI.HasComponent<TerrainTileDistanceToPlayer>(entity))
{
    var distInfo = SystemAPI.GetComponent<TerrainTileDistanceToPlayer>(entity);
    float dist = distInfo.distance;
    var lod = distInfo.lodLevel;
}
```

---

### Buffer Components

#### VertexElement

```csharp
public struct VertexElement : IBufferElementData
{
    public float3 value;
}
```

**Purpose**: Stores mesh vertex positions.

**Usage**:
```csharp
var buffer = EntityManager.GetBuffer<VertexElement>(entity);

// Add vertex
buffer.Add(new VertexElement { value = new float3(0, 5, 0) });

// Access vertex
float3 position = buffer[index].value;

// Convert to NativeArray (zero-copy)
var array = buffer.Reinterpret<float3>().AsNativeArray();
```

---

#### NormalElement

```csharp
public struct NormalElement : IBufferElementData
{
    public float3 value;
}
```

**Purpose**: Stores mesh vertex normals.

**Usage**: Same pattern as VertexElement

---

#### UVElement

```csharp
public struct UVElement : IBufferElementData
{
    public float2 value;
}
```

**Purpose**: Stores mesh UV coordinates.

**Usage**:
```csharp
var buffer = EntityManager.GetBuffer<UVElement>(entity);
buffer.Add(new UVElement { value = new float2(0.5f, 0.5f) });
```

---

#### IndexElement

```csharp
public struct IndexElement : IBufferElementData
{
    public int value;
}
```

**Purpose**: Stores mesh triangle indices.

**Usage**:
```csharp
var buffer = EntityManager.GetBuffer<IndexElement>(entity);

// Add triangle (v0, v1, v2)
buffer.Add(new IndexElement { value = 0 });
buffer.Add(new IndexElement { value = 1 });
buffer.Add(new IndexElement { value = 2 });
```

---

### Physics Components

#### PhysicsColliderNeedsPreparation

```csharp
public struct PhysicsColliderNeedsPreparation : IComponentData, IEnableableComponent
{
    public TerrainPhysicsLODLevel targetLOD;
}
```

**Purpose**: Tags tiles needing collider preparation.

**Usage**:
```csharp
// Add component
em.AddComponentData(entity, new PhysicsColliderNeedsPreparation 
{ 
    targetLOD = TerrainPhysicsLODLevel.HalfResolution 
});

// Enable/disable without removing
em.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
```

---

#### PhysicsColliderPrepared

```csharp
public struct PhysicsColliderPrepared : IComponentData
{
    public TerrainPhysicsLODLevel lodLevel;
    public int priority;
}
```

**Purpose**: Indicates collider data ready for creation.

**Usage**: Typically added/removed by systems, not manually.

---

#### PhysicsColliderValid

```csharp
public struct PhysicsColliderValid : IComponentData { }
```

**Purpose**: Tag component - collider is valid and current.

**Usage**:
```csharp
// Check if collider valid
bool isValid = SystemAPI.HasComponent<PhysicsColliderValid>(entity);

// Remove to trigger regeneration
em.RemoveComponent<PhysicsColliderValid>(entity);
```

---

## Systems

### PlayerTrackingInitSystem

```csharp
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class PlayerTrackingInitSystem : SystemBase
```

**Purpose**: Finds player GameObject at startup and populates PlayerTransformReference.

**Requirements**:
- Entities with `PlayerTrackingSearch` and `PlayerTransformReference`

**API**:
```csharp
// None - system runs automatically
// Check status via TerrainTrackingDebugger
```

---

### ScrollTerrainSystem

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TileSpawningSystem))]
public partial struct ScrollTerrainSystem : ISystem
```

**Purpose**: Updates scroll offset for auto-scrolling terrain.

**Requirements**:
- `ScrollConfig` singleton
- `ScrollOffset` singleton
- `PlayerTransformReference` singleton

**API**: Automatic - no public methods

---

### TileSpawningSystem

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TileSpawningSystem : ISystem
```

**Purpose**: Spawns and despawns tiles based on player position.

**Requirements**:
- `PlayerTransformReference` singleton
- `TerrainTileConfig` singleton
- `ScrollOffset` singleton

**API**: Automatic - no public methods

**Internal State**:
```csharp
private NativeParallelHashMap<int2, Entity> _activeTiles;
```

---

### TileScrollPositionSystem

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ScrollTerrainSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TileScrollPositionSystem : ISystem
```

**Purpose**: Updates tile positions with scroll offset.

**Requirements**:
- `ScrollConfig` singleton
- `ScrollOffset` singleton
- `TerrainTileConfig` singleton

**API**: Automatic - no public methods

---

### TerrainMeshGenerationSystem

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct TerrainMeshGenerationSystem : ISystem
```

**Purpose**: Generates procedural terrain meshes with Perlin noise.

**Requirements**:
- `TerrainTileConfig` singleton

**API**: Automatic - no public methods

**Internal State**:
```csharp
private NativeQueue<Entity> _pendingTiles;
```

**Jobs**:
```csharp
[BurstCompile]
partial struct MeshGenerationJob : IJobEntity
{
    // Generates vertices, normals, UVs, indices
}
```

---

### TerrainDistanceTrackingSystem

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TerrainPhysicsSystem))]
public partial class TerrainDistanceTrackingSystem : SystemBase
```

**Purpose**: Calculates tile distances and determines LOD levels.

**Requirements**:
- `TerrainTileConfig` singleton
- `PlayerTransformReference` singleton

**API**: Automatic - no public methods

---

### TerrainColliderPreparationSystem

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainMeshGenerationSystem))]
public partial struct TerrainColliderPreparationSystem : ISystem
```

**Purpose**: Prepares collider data with LOD decimation via Burst jobs.

**Requirements**:
- `TerrainTileConfig` singleton

**API**: 
```csharp
public JobHandle PreparationDependency { get; }
```

**Jobs**:
```csharp
[BurstCompile]
partial struct PrepareColliderDataJob : IJobEntity
{
    // Decimates vertices and regenerates triangles
}
```

---

### TerrainPhysicsSystem

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainColliderPreparationSystem))]
public partial class TerrainPhysicsSystem : SystemBase
```

**Purpose**: Creates Unity Physics colliders with caching and frame budgeting.

**Requirements**:
- `TerrainTileConfig` singleton

**API**: Automatic - no public methods (manages internal cache)

**Internal State**:
```csharp
private NativeHashMap<ColliderCacheKey, ColliderCacheEntry> _colliderCache;
private long _totalCacheMemoryBytes;
private long _currentFrameNumber;
```

---

### TerrainRenderingSystem

```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class TerrainRenderingSystem : SystemBase
```

**Purpose**: Creates Unity Mesh objects and sets up Entities Graphics rendering.

**Requirements**:
- `TerrainTileConfig` singleton

**API**: Automatic - no public methods

**Internal State**:
```csharp
private Material _terrainMaterial;
private EntityQuery _newTilesQuery;
```

---

## Enums

### TerrainPhysicsLODLevel

```csharp
public enum TerrainPhysicsLODLevel : byte
{
    FullResolution = 0,      // Use all vertices
    HalfResolution = 1,      // Use every 2nd vertex (25%)
    QuarterResolution = 2,   // Use every 4th vertex (6.25%)
    NoCollider = 3           // No collider
}
```

**Usage**:
```csharp
TerrainPhysicsLODLevel lod = TerrainPhysicsLODLevel.HalfResolution;

if (distance < config.lodFullResolutionDistance)
    lod = TerrainPhysicsLODLevel.FullResolution;
```

---

### PlayerTrackingSearch.Mode

```csharp
public enum Mode : byte
{
    FindByName = 0,           // Search by GameObject.Find(name)
    FindByTag = 1,            // Search by FindGameObjectWithTag(tag)
    FindMainCamera = 2        // Use Camera.main
}
```

**Usage**: Set in TerrainConfigAuthoring Inspector

---

## Utility Functions

### ColliderCacheKey.FromConfig

```csharp
public static ColliderCacheKey FromConfig(
    TerrainTileConfig config, 
    TerrainPhysicsLODLevel lodLevel)
```

**Purpose**: Creates cache key from configuration parameters.

**Parameters**:
- `config` - Terrain configuration
- `lodLevel` - LOD level for this collider

**Returns**: Cache key for looking up cached colliders

**Usage**:
```csharp
var key = ColliderCacheKey.FromConfig(config, TerrainPhysicsLODLevel.HalfResolution);
```

---

### TerrainColliderBlob.Create

```csharp
public static BlobAssetReference<TerrainColliderBlob> Create(
    NativeArray<float3> sourceVertices,
    NativeArray<int3> sourceTriangles,
    TerrainPhysicsLODLevel lodLevel,
    Allocator allocator)
```

**Purpose**: Creates BlobAsset containing collider mesh data.

**Parameters**:
- `sourceVertices` - Vertex positions
- `sourceTriangles` - Triangle indices (int3 per triangle)
- `lodLevel` - LOD level for metadata
- `allocator` - Memory allocator (typically Persistent)

**Returns**: BlobAssetReference to be stored in cache

**Usage**:
```csharp
var vertices = new NativeArray<float3>(100, Allocator.Temp);
var triangles = new NativeArray<int3>(200, Allocator.Temp);
// ... fill arrays ...

var blobRef = TerrainColliderBlob.Create(
    vertices, 
    triangles, 
    TerrainPhysicsLODLevel.FullResolution, 
    Allocator.Persistent
);

// Store in cache or use immediately
// Remember to Dispose when done!
```

---

## Code Examples

### Example 1: Query All Terrain Tiles

```csharp
using Unity.Entities;
using Unity.Mathematics;

public partial class CustomTerrainSystem : SystemBase
{
    protected override void OnUpdate()
    {
        foreach (var (tile, transform, entity) in 
            SystemAPI.Query<RefRO<TerrainTile>, RefRO<LocalTransform>>()
            .WithEntityAccess())
        {
            int2 gridPos = tile.ValueRO.gridCoordinate;
            float3 worldPos = transform.ValueRO.Position;
            
            Debug.Log($"Tile at grid {gridPos}, world {worldPos}");
        }
    }
}
```

---

### Example 2: Modify Terrain Configuration at Runtime

```csharp
using Unity.Entities;
using UnityEngine;

public class RuntimeConfigModifier : MonoBehaviour
{
    public void DoubleViewDistance()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        var query = em.CreateEntityQuery(typeof(TerrainTileConfig));
        var entity = query.GetSingletonEntity();
        var config = em.GetComponentData<TerrainTileConfig>(entity);
        
        config.viewDistance *= 2f;
        em.SetComponentData(entity, config);
        
        query.Dispose();
        
        Debug.Log($"View distance now: {config.viewDistance}m");
    }
}
```

---

### Example 3: Toggle Auto-Scrolling

```csharp
public class ScrollController : MonoBehaviour
{
    private bool _scrolling = false;
    
    public void ToggleScroll()
    {
        _scrolling = !_scrolling;
        
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(ScrollConfig));
        var entity = query.GetSingletonEntity();
        
        var config = em.GetComponentData<ScrollConfig>(entity);
        config.enabled = _scrolling;
        config.scrollSpeed = _scrolling ? 10f : 0f;
        em.SetComponentData(entity, config);
        
        query.Dispose();
    }
}
```

---

### Example 4: Get Tile Count

```csharp
public int GetActiveTileCount()
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    
    var query = em.CreateEntityQuery(typeof(TerrainTile));
    int count = query.CalculateEntityCount();
    query.Dispose();
    
    return count;
}
```

---

### Example 5: Check if Tile Exists at Position

```csharp
public bool TileExistsAt(int2 gridCoordinate)
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    
    var query = em.CreateEntityQuery(typeof(TerrainTile));
    var tiles = query.ToComponentDataArray<TerrainTile>(Allocator.Temp);
    
    bool exists = false;
    foreach (var tile in tiles)
    {
        if (tile.gridCoordinate.Equals(gridCoordinate))
        {
            exists = true;
            break;
        }
    }
    
    tiles.Dispose();
    query.Dispose();
    
    return exists;
}
```

---

### Example 6: Access Mesh Buffers

```csharp
public void PrintTileVertices(Entity tileEntity)
{
    var em = World.DefaultGameObjectInjectionWorld.EntityManager;
    
    if (!em.HasBuffer<VertexElement>(tileEntity))
    {
        Debug.Log("Tile has no vertex buffer");
        return;
    }
    
    var buffer = em.GetBuffer<VertexElement>(tileEntity);
    
    Debug.Log($"Tile has {buffer.Length} vertices");
    
    for (int i = 0; i < math.min(10, buffer.Length); i++)
    {
        Debug.Log($"  Vertex {i}: {buffer[i].value}");
    }
}
```

---

### Example 7: Monitor Scroll Distance

```csharp
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ScrollDistanceMonitor : MonoBehaviour
{
    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(ScrollOffset));
        
        if (query.CalculateEntityCount() > 0)
        {
            var offset = em.GetComponentData<ScrollOffset>(query.GetSingletonEntity());
            float distance = math.length(offset.accumulatedOffset);
            
            Debug.Log($"Scrolled: {distance:F1}m, Direction: {offset.accumulatedOffset}");
        }
        
        query.Dispose();
    }
}
```

---

### Example 8: Force Regenerate All Tiles

```csharp
public void RegenerateAllTiles()
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    
    var query = em.CreateEntityQuery(typeof(TerrainTile));
    var entities = query.ToEntityArray(Allocator.Temp);
    
    foreach (var entity in entities)
    {
        var tile = em.GetComponentData<TerrainTile>(entity);
        tile.needsRegeneration = true;
        tile.meshGenerated = false;
        em.SetComponentData(entity, tile);
        
        // Clear mesh buffers
        em.GetBuffer<VertexElement>(entity).Clear();
        em.GetBuffer<NormalElement>(entity).Clear();
        em.GetBuffer<UVElement>(entity).Clear();
        em.GetBuffer<IndexElement>(entity).Clear();
    }
    
    entities.Dispose();
    query.Dispose();
    
    Debug.Log("All tiles marked for regeneration");
}
```

---

### Example 9: Get Player Position from Terrain System

```csharp
public Vector3? GetTrackedPlayerPosition()
{
    var world = World.DefaultGameObjectInjectionWorld;
    if (world == null) return null;
    
    var em = world.EntityManager;
    var query = em.CreateEntityQuery(typeof(PlayerTransformReference));
    
    if (query.CalculateEntityCount() == 0)
    {
        query.Dispose();
        return null;
    }
    
    var entity = query.GetSingletonEntity();
    var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
    query.Dispose();
    
    if (playerRef == null || playerRef.playerTransform == null)
        return null;
    
    return playerRef.playerTransform.position;
}
```

---

### Example 10: Custom Tile Inspector

```csharp
public class TileInspector : MonoBehaviour
{
    [ContextMenu("Inspect Nearest Tile")]
    public void InspectNearestTile()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        var playerPos = GetTrackedPlayerPosition();
        if (!playerPos.HasValue) return;
        
        var query = em.CreateEntityQuery(typeof(TerrainTile), typeof(LocalTransform));
        var entities = query.ToEntityArray(Allocator.Temp);
        var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        var tiles = query.ToComponentDataArray<TerrainTile>(Allocator.Temp);
        
        float minDist = float.MaxValue;
        int nearestIndex = -1;
        
        for (int i = 0; i < entities.Length; i++)
        {
            float dist = math.distance(transforms[i].Position, (float3)playerPos.Value);
            if (dist < minDist)
            {
                minDist = dist;
                nearestIndex = i;
            }
        }
        
        if (nearestIndex >= 0)
        {
            var tile = tiles[nearestIndex];
            var transform = transforms[nearestIndex];
            
            Debug.Log($"=== Nearest Tile ===");
            Debug.Log($"Grid: {tile.gridCoordinate}");
            Debug.Log($"Position: {transform.Position}");
            Debug.Log($"Distance: {minDist:F1}m");
            Debug.Log($"Mesh Generated: {tile.meshGenerated}");
            
            // Check for additional components
            var entity = entities[nearestIndex];
            Debug.Log($"Has MeshReference: {em.HasComponent<MeshReference>(entity)}");
            Debug.Log($"Has PhysicsCollider: {em.HasComponent<Unity.Physics.PhysicsCollider>(entity)}");
        }
        
        entities.Dispose();
        transforms.Dispose();
        tiles.Dispose();
        query.Dispose();
    }
}
```

---

## System Access Patterns

### Accessing Singletons

```csharp
// In ISystem (Burst compatible)
var config = SystemAPI.GetSingleton<TerrainTileConfig>();

// In SystemBase (non-Burst)
var config = SystemAPI.GetSingleton<TerrainTileConfig>();

// With EntityManager
var query = em.CreateEntityQuery(typeof(TerrainTileConfig));
var entity = query.GetSingletonEntity();
var config = em.GetComponentData<TerrainTileConfig>(entity);
query.Dispose();
```

### Accessing Managed Singletons

```csharp
// In ISystem (NOT Burst compatible)
var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();

// In SystemBase
var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();

// With EntityManager
var entity = em.CreateEntityQuery(typeof(PlayerTransformReference)).GetSingletonEntity();
var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
```

### Modifying Singleton Data

```csharp
// In ISystem
RefRW<ScrollOffset> offset = SystemAPI.GetSingletonRW<ScrollOffset>();
offset.ValueRW.accumulatedOffset += new float3(1, 0, 0);

// In SystemBase
var offset = SystemAPI.GetSingletonRW<ScrollOffset>();
offset.ValueRW.accumulatedOffset += new float3(1, 0, 0);

// With EntityManager
var query = em.CreateEntityQuery(typeof(ScrollOffset));
var entity = query.GetSingletonEntity();
var offset = em.GetComponentData<ScrollOffset>(entity);
offset.accumulatedOffset += new float3(1, 0, 0);
em.SetComponentData(entity, offset);
query.Dispose();
```

### Querying Tiles

```csharp
// Query with SystemAPI
foreach (var (tile, entity) in 
    SystemAPI.Query<RefRO<TerrainTile>>()
    .WithEntityAccess())
{
    // Process tile
}

// Query with EntityManager
var query = em.CreateEntityQuery(typeof(TerrainTile));
var entities = query.ToEntityArray(Allocator.Temp);

foreach (var entity in entities)
{
    var tile = em.GetComponentData<TerrainTile>(entity);
    // Process tile
}

entities.Dispose();
query.Dispose();
```

### Buffer Access

```csharp
// Get buffer
var buffer = EntityManager.GetBuffer<VertexElement>(entity);

// Read elements
foreach (var element in buffer)
{
    float3 vertex = element.value;
}

// Add elements
buffer.Add(new VertexElement { value = new float3(0, 0, 0) });

// Clear buffer
buffer.Clear();

// Convert to NativeArray (zero-copy)
var array = buffer.Reinterpret<float3>().AsNativeArray();
```

---

## Thread Safety

### Burst-Compatible Components

Can access from Burst-compiled code:
- ✅ TerrainTileConfig
- ✅ ScrollConfig
- ✅ ScrollOffset
- ✅ PlayerTrackingSearch
- ✅ TerrainTile
- ✅ All buffer components
- ✅ All physics components (except managed)

### Main Thread Only

Must access from main thread:
- ❌ PlayerTransformReference (managed)
- ❌ MeshReference (managed)
- ❌ Any component holding Unity Object references

### Usage Example

```csharp
[BurstCompile]  // ✅ OK - no managed components
public partial struct MyBurstSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        // ✅ Can access struct singletons
        
        // ❌ Cannot access PlayerTransformReference (compile error)
        // var playerRef = SystemAPI.GetSingleton<PlayerTransformReference>();
    }
}

public partial class MyMainThreadSystem : SystemBase  // ✅ Can access managed
{
    protected override void OnUpdate()
    {
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        // ✅ Can access managed components
    }
}
```

---

## Related Documentation

- **[Component Reference](COMPONENT_REFERENCE.md)** - Detailed component descriptions
- **[System Reference](SYSTEM_REFERENCE.md)** - System details
- **[Technical Details](TECHNICAL_DETAILS.md)** - Implementation details
- **[Code Examples](EXTENSIONS.md)** - More advanced examples

---

**Back to**: [Documentation Hub](README.md)

