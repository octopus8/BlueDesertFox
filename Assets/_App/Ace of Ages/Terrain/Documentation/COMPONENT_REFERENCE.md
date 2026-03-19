# Component Reference - All Terrain Components

Complete reference for all components used in the terrain system.

## Component Categories

- **[Configuration Singletons](#configuration-singletons)** - Global configuration (one per world)
- **[Runtime Singletons](#runtime-singletons)** - Global state (one per world)
- **[Tile Components](#tile-components)** - Per-tile data (one per tile entity)
- **[Mesh Buffers](#mesh-buffers)** - Mesh data storage (per-tile)
- **[Physics Components](#physics-components)** - Collision system components
- **[Managed Components](#managed-components)** - Components holding Unity Object references

---

## Configuration Singletons

These components exist once per ECS world and store configuration data baked from `TerrainConfigAuthoring`.

### TerrainTileConfig

**File**: `TileComponents.cs`  
**Type**: `IComponentData` (struct)  
**Singleton**: Yes  
**Baked From**: TerrainConfigAuthoring

**Purpose**: Stores all terrain tile configuration parameters.

**Fields**:
```csharp
public struct TerrainTileConfig : IComponentData
{
    // Tile settings
    public float tileSize;                    // Size of each tile (meters)
    public float viewDistance;                // Render distance (meters)
    public int verticesPerSide;               // Mesh resolution
    
    // Noise parameters
    public float noiseFrequency;              // Base noise frequency
    public float noiseAmplitude;              // Height variation (meters)
    public int noiseOctaves;                  // Detail layers
    public float noiseLacunarity;             // Frequency multiplier
    public float noisePersistence;            // Amplitude multiplier
    
    // Physics optimization
    public int maxCollidersCreatedPerFrame;   // Frame budget
    public float lodFullResolutionDistance;   // Full-res threshold
    public float lodHalfResolutionDistance;   // Half-res threshold
    public float lodQuarterResolutionDistance; // Quarter-res threshold
    public int maxColliderCacheMemoryMB;      // Cache memory limit
    public bool usePhysicsLODLayers;          // Enable layer separation
    public int lowDetailPhysicsLayer;         // LOD layer index
}
```

**Typical Values**:
```csharp
tileSize: 100f
viewDistance: 500f
verticesPerSide: 32
noiseFrequency: 0.01f
noiseAmplitude: 20f
noiseOctaves: 4
maxCollidersCreatedPerFrame: 3
```

**Read By**: All terrain systems  
**Modified At**: Bake time only (can modify at runtime for testing)

---

### ScrollConfig

**File**: `TileComponents.cs`  
**Type**: `IComponentData` (struct)  
**Singleton**: Yes  
**Baked From**: TerrainConfigAuthoring

**Purpose**: Configuration for auto-scrolling terrain feature.

**Fields**:
```csharp
public struct ScrollConfig : IComponentData
{
    public bool enabled;        // Enable auto-scrolling
    public float scrollSpeed;   // Speed (m/s), positive = forward
}
```

**Typical Values**:
```csharp
enabled: false
scrollSpeed: 5.0f
```

**Read By**: ScrollTerrainSystem, TileScrollPositionSystem  
**Modified At**: Runtime (can toggle scrolling dynamically)

---

### PlayerTrackingSearch

**File**: `TileComponents.cs`  
**Type**: `IComponentData` (struct)  
**Singleton**: Yes  
**Baked From**: TerrainConfigAuthoring

**Purpose**: Stores search parameters for finding player GameObject at runtime.

**Fields**:
```csharp
public struct PlayerTrackingSearch : IComponentData
{
    public enum Mode : byte
    {
        FindByName = 0,
        FindByTag = 1,
        FindAutoHandPlayer = 2,
        FindMainCamera = 3
    }
    
    public Mode mode;                           // Search method
    public FixedString128Bytes searchString;    // Name or tag
    public bool initialized;                    // Search complete
}
```

**Typical Values**:
```csharp
mode: Mode.FindAutoHandPlayer
searchString: "" (not used for this mode)
initialized: false (becomes true after player found)
```

**Read By**: PlayerTrackingInitSystem  
**Modified At**: Runtime by PlayerTrackingInitSystem (sets initialized flag)

---

## Runtime Singletons

These components exist once per world and store runtime state that changes every frame.

### ScrollOffset

**File**: `TileComponents.cs`  
**Type**: `IComponentData` (struct)  
**Singleton**: Yes

**Purpose**: Tracks accumulated terrain scroll distance for auto-scrolling feature.

**Fields**:
```csharp
public struct ScrollOffset : IComponentData
{
    public float3 accumulatedOffset;  // Total scroll distance (XZ plane)
}
```

**Example Values**:
```csharp
// After 10 seconds at 5 m/s forward:
accumulatedOffset: (0, 0, 50)  // 50 meters forward

// After 20 seconds at 5 m/s, player rotated 90° right:
accumulatedOffset: (50, 0, 50)  // 50m forward + 50m right
```

**Updated By**: ScrollTerrainSystem  
**Read By**: TileSpawningSystem, TileScrollPositionSystem

---

## Tile Components

These components exist on each terrain tile entity.

### TerrainTile

**File**: `TileComponents.cs`  
**Type**: `IComponentData` (struct)  
**Per Entity**: Yes

**Purpose**: Identifies a terrain tile and tracks its generation state.

**Fields**:
```csharp
public struct TerrainTile : IComponentData
{
    public int2 gridCoordinate;      // Grid position (e.g., (0,0), (1,2))
    public bool meshGenerated;       // True if mesh data populated
    public bool needsRegeneration;   // True if mesh needs regeneration
}
```

**Example**:
```csharp
// Tile at grid (1, -2)
gridCoordinate: (1, -2)
meshGenerated: true
needsRegeneration: false
```

**Added By**: TileSpawningSystem  
**Updated By**: TerrainMeshGenerationSystem (sets meshGenerated)

---

### TerrainTileDistanceToPlayer

**File**: `TerrainPhysicsComponents.cs`  
**Type**: `IComponentData` (struct)  
**Per Entity**: Yes

**Purpose**: Caches distance from tile to player and LOD level.

**Fields**:
```csharp
public struct TerrainTileDistanceToPlayer : IComponentData
{
    public float distance;                      // Distance in meters
    public TerrainPhysicsLODLevel lodLevel;     // Current LOD
}
```

**Example**:
```csharp
distance: 245.6f
lodLevel: TerrainPhysicsLODLevel.HalfResolution
```

**Added By**: TerrainDistanceTrackingSystem  
**Updated By**: TerrainDistanceTrackingSystem (every frame)

---

### PhysicsColliderNeedsPreparation

**File**: `TerrainPhysicsComponents.cs`  
**Type**: `IComponentData, IEnableableComponent` (struct)  
**Per Entity**: Yes

**Purpose**: Tags tiles that need collider data preparation.

**Fields**:
```csharp
public struct PhysicsColliderNeedsPreparation : IComponentData, IEnableableComponent
{
    public TerrainPhysicsLODLevel targetLOD;  // Desired LOD level
}
```

**Example**:
```csharp
targetLOD: TerrainPhysicsLODLevel.HalfResolution
```

**Added By**: TerrainDistanceTrackingSystem  
**Removed By**: TerrainColliderPreparationSystem (after preparation)  
**Enableable**: Can disable temporarily without removing

---

### PhysicsColliderPrepared

**File**: `TerrainPhysicsComponents.cs`  
**Type**: `IComponentData` (struct)  
**Per Entity**: Yes

**Purpose**: Indicates tile has prepared collider data ready for main-thread creation.

**Fields**:
```csharp
public struct PhysicsColliderPrepared : IComponentData
{
    public TerrainPhysicsLODLevel lodLevel;  // LOD of prepared data
    public int priority;                      // Sort order (lower = first)
}
```

**Example**:
```csharp
lodLevel: TerrainPhysicsLODLevel.FullResolution
priority: 150 // Distance 150m, in front of camera
```

**Added By**: TerrainColliderPreparationSystem  
**Removed By**: TerrainPhysicsSystem (after collider created)

---

### PhysicsColliderValid

**File**: `TerrainPhysicsComponents.cs`  
**Type**: `IComponentData` (struct, tag)  
**Per Entity**: Yes

**Purpose**: Tags tiles with valid physics colliders that don't need regeneration.

**Fields**: None (tag component)

**Added By**: TerrainPhysicsSystem  
**Removed By**: TerrainDistanceTrackingSystem (when LOD changes)

---

## Mesh Buffers

Dynamic buffers that store mesh data for each tile.

### VertexElement

**File**: `TileComponents.cs`  
**Type**: `IBufferElementData` (struct)  
**Per Entity**: Yes (dynamic buffer)

**Purpose**: Stores vertex positions for tile mesh.

**Fields**:
```csharp
public struct VertexElement : IBufferElementData
{
    public float3 value;  // Vertex position (local space)
}
```

**Size**: `verticesPerSide²` elements (e.g., 32×32 = 1024 elements)

**Populated By**: TerrainMeshGenerationSystem  
**Read By**: TerrainRenderingSystem, TerrainColliderPreparationSystem

---

### NormalElement

**File**: `TileComponents.cs`  
**Type**: `IBufferElementData` (struct)  
**Per Entity**: Yes (dynamic buffer)

**Purpose**: Stores vertex normals for lighting calculations.

**Fields**:
```csharp
public struct NormalElement : IBufferElementData
{
    public float3 value;  // Normal vector (normalized)
}
```

**Size**: Same as VertexElement (one normal per vertex)

**Populated By**: TerrainMeshGenerationSystem  
**Read By**: TerrainRenderingSystem

---

### UVElement

**File**: `TileComponents.cs`  
**Type**: `IBufferElementData` (struct)  
**Per Entity**: Yes (dynamic buffer)

**Purpose**: Stores UV texture coordinates.

**Fields**:
```csharp
public struct UVElement : IBufferElementData
{
    public float2 value;  // UV coordinates (0-1 range typically)
}
```

**Size**: Same as VertexElement

**Populated By**: TerrainMeshGenerationSystem  
**Read By**: TerrainRenderingSystem

---

### IndexElement

**File**: `TileComponents.cs`  
**Type**: `IBufferElementData` (struct)  
**Per Entity**: Yes (dynamic buffer)

**Purpose**: Stores triangle indices for mesh topology.

**Fields**:
```csharp
public struct IndexElement : IBufferElementData
{
    public int value;  // Vertex index
}
```

**Size**: `(verticesPerSide - 1)² × 6` elements (2 triangles per quad × 3 indices)  
**Example**: 32×32 grid = 31×31 quads = 5766 indices

**Populated By**: TerrainMeshGenerationSystem  
**Read By**: TerrainRenderingSystem, TerrainColliderPreparationSystem

---

### ColliderPreparedVertexElement

**File**: `TerrainPhysicsComponents.cs`  
**Type**: `IBufferElementData` (struct)  
**Per Entity**: Yes (dynamic buffer)

**Purpose**: Stores LOD-decimated vertices for collider creation.

**Fields**:
```csharp
public struct ColliderPreparedVertexElement : IBufferElementData
{
    public float3 value;  // Decimated vertex position
}
```

**Size**: Depends on LOD level
- Full: Same as VertexElement
- Half: 25% of VertexElement
- Quarter: 6.25% of VertexElement

**Populated By**: TerrainColliderPreparationSystem  
**Read By**: TerrainPhysicsSystem  
**Removed By**: TerrainPhysicsSystem (after collider created)

---

### ColliderPreparedTriangleElement

**File**: `TerrainPhysicsComponents.cs`  
**Type**: `IBufferElementData` (struct)  
**Per Entity**: Yes (dynamic buffer)

**Purpose**: Stores triangle indices for LOD-decimated collider mesh.

**Fields**:
```csharp
public struct ColliderPreparedTriangleElement : IBufferElementData
{
    public int3 value;  // Triangle indices (i0, i1, i2)
}
```

**Size**: Depends on LOD level (proportional to decimated vertex count)

**Populated By**: TerrainColliderPreparationSystem  
**Read By**: TerrainPhysicsSystem  
**Removed By**: TerrainPhysicsSystem (after collider created)

---

## Managed Components

Components that hold references to Unity Objects (must be classes).

### MeshReference

**File**: `TileComponents.cs`  
**Type**: `IComponentData` (class - managed)  
**Per Entity**: Yes

**Purpose**: Holds reference to Unity Mesh object for rendering.

**Fields**:
```csharp
public class MeshReference : IComponentData
{
    public UnityEngine.Mesh mesh;
}
```

**Added By**: TerrainRenderingSystem  
**Cleaned Up**: When entity destroyed (automatic)

**Note**: Must be class (not struct) to hold managed reference.

---

### PlayerTransformReference

**File**: `TileComponents.cs`  
**Type**: `IComponentData` (class - managed)  
**Singleton**: Yes

**Purpose**: Holds reference to player GameObject's Transform.

**Fields**:
```csharp
public class PlayerTransformReference : IComponentData
{
    public UnityEngine.Transform playerTransform;
}
```

**Example**:
```csharp
playerTransform: Transform of "XR Origin Hands (XR Rig)" GameObject
```

**Added By**: Baking system (empty initially)  
**Populated By**: PlayerTrackingInitSystem (at runtime)  
**Read By**: All systems that need player position

**Note**: Must be class to hold managed Transform reference.

---

## Physics Components

Components specific to the physics collider system.

### TerrainPhysicsLODLevel (Enum)

**File**: `TerrainPhysicsComponents.cs`  
**Type**: Enum (byte)

**Purpose**: Defines physics collider LOD levels.

**Values**:
```csharp
public enum TerrainPhysicsLODLevel : byte
{
    FullResolution = 0,      // Use all vertices
    HalfResolution = 1,      // Use every 2nd vertex
    QuarterResolution = 2,   // Use every 4th vertex
    NoCollider = 3           // Too far, no collider
}
```

**Used By**: All physics systems

---

### TerrainColliderBlob

**File**: `TerrainPhysicsComponents.cs`  
**Type**: Blob Asset (struct)

**Purpose**: Stores pre-baked collider mesh data for caching.

**Fields**:
```csharp
public struct TerrainColliderBlob
{
    public BlobArray<float3> vertices;    // Collider vertices
    public BlobArray<int3> triangles;     // Collider triangles
    public int vertexCount;               // Number of vertices
    public int triangleCount;             // Number of triangles
    public TerrainPhysicsLODLevel lodLevel; // LOD of this data
}
```

**Created By**: `TerrainColliderBlob.Create()` factory method  
**Stored In**: Collider cache (NativeHashMap)  
**Lifetime**: Persists until cache eviction

---

### ColliderCacheKey

**File**: `TerrainPhysicsComponents.cs`  
**Type**: Struct (implements IEquatable)

**Purpose**: Key for looking up cached colliders.

**Fields**:
```csharp
public struct ColliderCacheKey : IEquatable<ColliderCacheKey>
{
    public int verticesPerSide;
    public TerrainPhysicsLODLevel lodLevel;
    public uint noiseParamsHash;  // Hash of all noise parameters
}
```

**Factory Method**:
```csharp
ColliderCacheKey key = ColliderCacheKey.FromConfig(config, lodLevel);
```

**Hash Calculation**: Combines all noise parameters into single uint

**Used By**: TerrainPhysicsSystem for cache lookups

---

### ColliderCacheEntry

**File**: `TerrainPhysicsComponents.cs`  
**Type**: Struct

**Purpose**: Entry in LRU cache tracking collider usage.

**Fields**:
```csharp
public struct ColliderCacheEntry
{
    public BlobAssetReference<TerrainColliderBlob> blobAsset;
    public long lastAccessFrame;
    public int estimatedMemoryBytes;
}
```

**Stored In**: `NativeHashMap<ColliderCacheKey, ColliderCacheEntry>`  
**Updated**: Every time cached collider is accessed (LRU tracking)

---

## Rendering Components

### MaterialMeshInfo

**Source**: Unity.Rendering  
**Type**: `IComponentData` (struct)  
**Per Entity**: Yes

**Purpose**: Identifies which material and mesh to render.

**Added By**: TerrainRenderingSystem via `RenderMeshUtility.AddComponents()`  
**Used By**: Entities Graphics (automatic rendering)

---

### RenderBounds

**Source**: Unity.Rendering  
**Type**: `IComponentData` (struct)  
**Per Entity**: Yes

**Purpose**: Stores AABB bounds for frustum culling.

**Fields**:
```csharp
public struct RenderBounds : IComponentData
{
    public AABB Value;  // Axis-aligned bounding box
}
```

**Calculated From**: Mesh vertex positions  
**Added By**: TerrainRenderingSystem  
**Used By**: Entities Graphics culling system

---

### RenderFilterSettings

**Source**: Unity.Rendering  
**Type**: `ISharedComponentData` (struct)  
**Per Entity**: Yes (shared across entities with same settings)

**Purpose**: Rendering configuration (shadows, layers, motion vectors).

**Added By**: TerrainRenderingSystem  
**Used By**: Entities Graphics

---

## Transform Components

### LocalTransform

**Source**: Unity.Transforms  
**Type**: `IComponentData` (struct)  
**Per Entity**: Yes

**Purpose**: Stores entity's local transform (position, rotation, scale).

**Fields**:
```csharp
public struct LocalTransform : IComponentData
{
    public float3 Position;
    public quaternion Rotation;
    public float Scale;
}
```

**Added By**: TileSpawningSystem  
**Updated By**: TileScrollPositionSystem (applies scroll offset)

---

### LocalToWorld

**Source**: Unity.Transforms  
**Type**: `IComponentData` (struct)  
**Per Entity**: Yes

**Purpose**: Stores entity's world transform matrix.

**Fields**:
```csharp
public struct LocalToWorld : IComponentData
{
    public float4x4 Value;  // Transform matrix
}
```

**Added By**: TileSpawningSystem  
**Updated By**: Unity Transform system (automatic)

---

## Component Lifecycle

### Typical Tile Component Lifecycle

```
Entity Created:
  ├─ TerrainTile (meshGenerated = false)
  ├─ LocalTransform
  ├─ LocalToWorld
  ├─ VertexElement [empty buffer]
  ├─ NormalElement [empty buffer]
  ├─ UVElement [empty buffer]
  └─ IndexElement [empty buffer]

After Mesh Generation:
  ├─ Buffers populated (1024+ elements each)
  └─ TerrainTile.meshGenerated = true

After Rendering Setup:
  ├─ MeshReference (Unity Mesh object)
  ├─ MaterialMeshInfo
  ├─ RenderBounds
  └─ RenderFilterSettings

After Distance Tracking:
  ├─ TerrainTileDistanceToPlayer
  └─ PhysicsColliderNeedsPreparation (if needs collider)

After Collider Preparation:
  ├─ ColliderPreparedVertexElement [buffer]
  ├─ ColliderPreparedTriangleElement [buffer]
  └─ PhysicsColliderPrepared

After Collider Creation:
  ├─ PhysicsCollider (Unity Physics)
  ├─ PhysicsColliderValid
  └─ Prepared buffers removed

Entity Destroyed:
  └─ All components cleaned up automatically
```

## Component Queries

### Finding Tiles Needing Work

**Tiles without meshes**:
```csharp
EntityQuery query = GetEntityQuery(
    ComponentType.ReadOnly<TerrainTile>(),
    ComponentType.ReadOnly<VertexElement>()
);

foreach (var tile in SystemAPI.Query<RefRO<TerrainTile>>())
{
    if (!tile.ValueRO.meshGenerated)
    {
        // Process tile
    }
}
```

**Tiles ready to render**:
```csharp
foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
    .WithAll<VertexElement>()
    .WithNone<MeshReference>()
    .WithEntityAccess())
{
    if (tile.ValueRO.meshGenerated)
    {
        // Create mesh
    }
}
```

**Tiles needing colliders**:
```csharp
foreach (var (prepared, entity) in 
    SystemAPI.Query<RefRO<PhysicsColliderPrepared>>()
    .WithEntityAccess())
{
    // Create collider
}
```

## Memory Footprint

### Per-Tile Memory Usage (32×32 mesh)

```
Components:
  TerrainTile:                     12 bytes
  LocalTransform:                  32 bytes
  LocalToWorld:                    64 bytes
  TerrainTileDistanceToPlayer:     8 bytes
  PhysicsColliderValid:            0 bytes (tag)

Buffers:
  VertexElement:     1024 × 12 =   12 KB
  NormalElement:     1024 × 12 =   12 KB
  UVElement:         1024 × 8  =   8 KB
  IndexElement:      5766 × 4  =   23 KB

Managed:
  MeshReference:                   ~8 KB (Unity Mesh overhead)
  
Physics:
  PhysicsCollider:                 ~4 KB (varies by LOD)

Total per tile: ~67 KB
```

### Singleton Memory Usage

```
TerrainTileConfig:                 80 bytes
ScrollConfig:                      8 bytes
ScrollOffset:                      12 bytes
PlayerTrackingSearch:              136 bytes
PlayerTransformReference:          8 bytes (reference only)

Total singletons: ~244 bytes (negligible)
```

### Cache Memory Usage

```
Collider Cache: Configurable (default 50 MB)
  - Stores BlobAssetReferences
  - Actual collider data varies by LOD
  - LRU eviction when limit exceeded
```

## Component Access Patterns

### Read-Only Access

```csharp
// Fastest - use RefRO
foreach (var tile in SystemAPI.Query<RefRO<TerrainTile>>())
{
    float2 gridPos = tile.ValueRO.gridCoordinate;
}
```

### Read-Write Access

```csharp
// Use RefRW for modifications
foreach (var tile in SystemAPI.Query<RefRW<TerrainTile>>())
{
    tile.ValueRW.meshGenerated = true;
}
```

### Buffer Access

```csharp
// Get buffer and modify
var vertices = EntityManager.GetBuffer<VertexElement>(entity);
vertices.Add(new VertexElement { value = new float3(0, 0, 0) });
```

### Managed Component Access

```csharp
// Must use GetComponentObject for managed components
var playerRef = EntityManager.GetComponentObject<PlayerTransformReference>(entity);
Transform player = playerRef.playerTransform;
```

## Related Documentation

- **[API Reference](API_REFERENCE.md)** - Complete API with code examples
- **[System Reference](SYSTEM_REFERENCE.md)** - Systems that use these components
- **[Technical Details](TECHNICAL_DETAILS.md)** - Implementation details

---

**Back to**: [Documentation Hub](README.md)

