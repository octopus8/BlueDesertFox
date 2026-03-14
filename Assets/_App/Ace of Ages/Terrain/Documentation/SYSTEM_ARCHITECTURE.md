# Infinite Terrain System - Architecture Overview

**Last Updated:** March 14, 2026  
**Author:** Auto-generated Documentation

## Table of Contents
1. [System Overview](#system-overview)
2. [Core Components](#core-components)
3. [System Pipeline](#system-pipeline)
4. [Data Flow](#data-flow)
5. [Performance Characteristics](#performance-characteristics)

---

## System Overview

The Infinite Terrain System is a high-performance, DOTS-based (Data-Oriented Technology Stack) terrain solution that provides:

- **Infinite procedural terrain generation** using Perlin noise
- **Dynamic tile spawning/despawning** based on player position
- **Floating origin support** to prevent floating-point precision errors at large distances
- **Full physics integration** with automatic mesh collider generation
- **ECS-native rendering** via Unity Entities Graphics
- **Burst-compiled performance** for all critical paths

### Key Design Principles

1. **Entity Component System (ECS)**: All terrain logic uses Unity DOTS for maximum performance
2. **Procedural Generation**: Terrain is generated on-the-fly using deterministic noise functions
3. **Chunked Streaming**: Terrain divided into tiles that load/unload based on proximity
4. **Double Precision Tracking**: Uses `double3` for accumulated offset to maintain precision at unlimited distances
5. **Separation of Concerns**: Each system handles a single responsibility (spawning, mesh generation, rendering, physics)

---

## Core Components

### Configuration Components

#### `TerrainTileConfig` (Singleton)
The master configuration for the entire terrain system.

```csharp
public struct TerrainTileConfig : IComponentData
{
    float tileSize;           // Size of each tile in meters (e.g., 100)
    float viewDistance;       // Render distance in meters (e.g., 500)
    int verticesPerSide;      // Mesh resolution (e.g., 32 = 32x32 grid)
    
    // Noise parameters
    float noiseFrequency;     // Base frequency (e.g., 0.01)
    float noiseAmplitude;     // Max height (e.g., 20)
    int noiseOctaves;         // Noise layers (e.g., 4)
    float noiseLacunarity;    // Frequency multiplier per octave (e.g., 2.0)
    float noisePersistence;   // Amplitude multiplier per octave (e.g., 0.5)
}
```

**Key Relationships:**
- `viewDistance / tileSize` = number of tiles visible in one direction
- `verticesPerSide^2` = total vertices per tile
- Higher octaves = more detailed terrain (but slower generation)

#### `FloatingOriginConfig` (Singleton)
Controls the floating origin system behavior.

```csharp
public struct FloatingOriginConfig : IComponentData
{
    float shiftThreshold;  // Distance from origin that triggers shift (e.g., 2000m)
    bool enabled;          // Master switch for floating origin
}
```

#### `WorldOriginOffset` (Singleton)
Tracks the accumulated world offset using double precision.

```csharp
public struct WorldOriginOffset : IComponentData
{
    double3 accumulatedOffset;  // Total distance the world has shifted
}
```

**Critical for Consistency:** This offset is added to grid coordinates when sampling noise, ensuring terrain doesn't change after origin shifts.

### Tile Components

#### `TerrainTile`
Identifies a terrain tile and tracks its state.

```csharp
public struct TerrainTile : IComponentData
{
    int2 gridCoordinate;      // Position in the tile grid (e.g., (0,0), (1,0))
    bool meshGenerated;       // Has the mesh been created?
    bool needsRegeneration;   // Does the mesh need to be regenerated?
}
```

**Grid Coordinate System:**
- Grid (0, 0) is at world position (0, 0, 0)
- Grid (1, 0) is at world position (tileSize, 0, 0)
- Grid (-1, 2) is at world position (-tileSize, 0, 2*tileSize)

#### `MeshReference` (Managed)
Holds a reference to the Unity Mesh object.

```csharp
public class MeshReference : IComponentData
{
    UnityEngine.Mesh mesh;  // The actual Unity mesh instance
}
```

**Note:** This is a managed component (class, not struct) because it references a Unity Object.

### Mesh Data Buffers

Each tile entity has four dynamic buffers storing mesh geometry:

- **`VertexElement`**: Stores `float3` vertex positions
- **`NormalElement`**: Stores `float3` vertex normals for lighting
- **`UVElement`**: Stores `float2` texture coordinates
- **`IndexElement`**: Stores `int` triangle indices

**Buffer Usage Pattern:**
```
Vertices:  [0] = (0, 5.2, 0), [1] = (3.125, 4.8, 0), ...
Normals:   [0] = (0, 1, 0), [1] = (0.1, 0.99, 0), ...
UVs:       [0] = (0, 0), [1] = (0.03125, 0), ...
Indices:   [0, 32, 1, 1, 32, 33, 1, 33, 2, ...]  // Triangle strips
```

### Tag Components

#### `FloatingOriginEnabled`
Tag component marking entities that should be affected by world origin shifts.

**Tagged Entities:**
- Terrain tiles
- Player entity
- Any other world-space objects that need consistency

---

## System Pipeline

The terrain systems execute in a specific order to ensure correct data flow:

```
SimulationSystemGroup
├── TileSpawningSystem           [Frame N, Step 1]
│   └── Creates/destroys tile entities
│
├── TerrainMeshGenerationSystem  [Frame N, Step 2]
│   └── Generates mesh data in buffers
│
└── TerrainPhysicsSystem         [Frame N, Step 3]
    └── Creates physics colliders

TransformSystemGroup
├── LocalToWorldSystem           [Frame N, Step 4]
│   └── Updates transform matrices
│
└── FloatingOriginSystem         [Frame N, Step 5]
    └── Checks distance & shifts world

PresentationSystemGroup
└── TerrainRenderingSystem       [Frame N, Step 6]
    └── Converts mesh data to Unity Meshes
```

### System Execution Details

#### 1. TileSpawningSystem
**Update Group:** `SimulationSystemGroup`  
**Update Before:** `TransformSystemGroup`  
**Burst Compiled:** Yes (OnCreate, OnDestroy only - OnUpdate uses managed types)

**Responsibilities:**
- Track active tiles in `NativeParallelHashMap<int2, Entity>`
- Calculate player's grid coordinate from world position
- Determine which tiles should be active based on circular view distance
- Create new tile entities with all required components/buffers
- Destroy tiles that are too far away

**Key Algorithm:**
```csharp
playerGridCoord = floor(playerPosition / tileSize)
viewDistanceInTiles = ceil(viewDistance / tileSize)

for each offset in [-viewDistanceInTiles, +viewDistanceInTiles]:
    gridCoord = playerGridCoord + offset
    tileCenter = gridCoord * tileSize + tileSize/2
    distanceToTile = distance(tileCenter, playerPosition)
    
    if distanceToTile <= viewDistance:
        if tile doesn't exist:
            spawn tile at gridCoord
```

#### 2. TerrainMeshGenerationSystem
**Update Group:** `SimulationSystemGroup`  
**Update After:** `TileSpawningSystem`  
**Burst Compiled:** Partial (noise functions only)

**Responsibilities:**
- Process tiles where `meshGenerated == false` or `needsRegeneration == true`
- Generate vertex positions by sampling noise at world positions
- Calculate vertex normals from adjacent heights
- Generate UV coordinates
- Generate triangle indices for mesh topology
- Mark tile as generated

**Key Algorithm:**
```csharp
for each vertex (x, z) in tile:
    localPosition = (x * stepSize, 0, z * stepSize)
    worldPosition = tileWorldPosition + localPosition + accumulatedOffset
    height = SampleMultiOctaveNoise(worldPosition)
    vertices[index] = (localX, height, localZ)
    
for each vertex (x, z) in tile:
    worldPosition = tileWorldPosition + localPosition + accumulatedOffset
    normal = CalculateNormalFromHeightfield(worldPosition, stepSize, config)
    // Samples heights at 4 neighbors by calling SampleNoise() directly
    // Works across tile boundaries for seamless lighting
    normals[index] = normal
    
for each triangle quad:
    indices += [v0, v1, v2, v2, v1, v3]  // Two triangles per quad
```

**Noise Sampling:**
- Uses `Unity.Mathematics.noise.snoise()` (Simplex noise)
- Multiple octaves combined with lacunarity and persistence
- Normalized to `[0, noiseAmplitude]` range

**Normal Calculation:**
- Uses **heightfield sampling** approach (not vertex array lookup)
- Samples noise at 4 neighboring world positions (left, right, up, down)
- Calculates gradient using central differences method
- **Works seamlessly at tile boundaries** - can sample beyond current tile
- Result: Perfect normal continuity across all tile edges

#### 3. TerrainPhysicsSystem
**Update Group:** `SimulationSystemGroup`  
**Update After:** `TerrainMeshGenerationSystem`  
**Burst Compiled:** No (uses managed Unity.Physics API)

**Responsibilities:**
- Query for tiles with mesh data but no `PhysicsCollider`
- Convert vertex/index buffers to `NativeArray<float3>` and `NativeArray<int3>`
- Create `Unity.Physics.MeshCollider` from geometry
- Add `PhysicsCollider` component to tile entity
- Set collision filter (default layer, collides with everything)

**Physics Configuration:**
```csharp
CollisionFilter:
    BelongsTo:    Layer 0 (default)
    CollidesWith: All layers (~0u)
    GroupIndex:   0
```

#### 4. FloatingOriginSystem
**Update Group:** `TransformSystemGroup`  
**Update After:** `LocalToWorldSystem`  
**Burst Compiled:** Yes (including parallel job)

**Responsibilities:**
- Monitor player distance from world origin (0, 0, 0)
- Trigger world shift when distance exceeds threshold
- Update `WorldOriginOffset.accumulatedOffset`
- Schedule parallel job to shift all `FloatingOriginEnabled` entities

**Shift Algorithm:**
```csharp
if length(playerPosition) > shiftThreshold:
    shiftOffset = playerPosition
    accumulatedOffset += shiftOffset
    
    // Parallel job:
    for each entity with FloatingOriginEnabled:
        entity.Position -= shiftOffset
```

**Effect:** Player snaps back to near-origin, all tiles shift by same amount, terrain generation continues seamlessly using accumulated offset.

#### 5. TerrainRenderingSystem
**Update Group:** `PresentationSystemGroup`  
**Burst Compiled:** No (requires managed Mesh/Material objects)

**Responsibilities:**
- Query for tiles with mesh buffers but no `MeshReference`
- Create Unity `Mesh` object from buffer data
- Register mesh and material with `EntitiesGraphicsSystem`
- Add Entities Graphics rendering components via `RenderMeshUtility`
- Store `MeshReference` to prevent regeneration

**Rendering Setup:**
```csharp
1. Create Mesh and populate with buffer data
2. mesh.RecalculateBounds()
3. entitiesGraphicsSystem.RegisterMesh(mesh)
4. entitiesGraphicsSystem.RegisterMaterial(material)
5. RenderMeshUtility.AddComponents(entity, renderMeshDescription, renderMeshArray)
```

**Rendering Components Added:**
- `RenderMesh` (deprecated in Unity 6, but supported)
- `MaterialMeshInfo` (material/mesh indices)
- `RenderBounds` (culling bounds)
- `WorldRenderBounds` (transformed bounds)
- `RenderFilterSettings` (layer mask, shadow settings)
- `LocalToWorld` (transformation matrix)

---

## Data Flow

### Tile Lifecycle

```
1. SPAWN REQUEST
   └─> TileSpawningSystem detects player entered new grid area
   
2. ENTITY CREATION
   └─> ECB creates entity with TerrainTile component
   └─> Adds empty buffers: VertexElement, NormalElement, UVElement, IndexElement
   └─> Adds LocalTransform at grid world position
   └─> Adds FloatingOriginEnabled tag
   
3. MESH GENERATION
   └─> TerrainMeshGenerationSystem finds tile with meshGenerated=false
   └─> Samples noise at world position + accumulated offset
   └─> Populates vertex/normal/uv buffers
   └─> Generates triangle indices
   └─> Sets meshGenerated=true
   
4. PHYSICS CREATION
   └─> TerrainPhysicsSystem finds tile with mesh but no PhysicsCollider
   └─> Creates Unity.Physics.MeshCollider from geometry
   └─> Adds PhysicsCollider component
   
5. RENDERING SETUP
   └─> TerrainRenderingSystem finds tile with mesh but no MeshReference
   └─> Creates Unity Mesh object
   └─> Registers with EntitiesGraphicsSystem
   └─> Adds all rendering components
   └─> Adds MeshReference to prevent re-processing
   
6. ACTIVE LIFETIME
   └─> Tile is visible and collidable
   └─> May receive origin shift updates (position adjustment)
   
7. DESPAWN
   └─> TileSpawningSystem detects player moved too far away
   └─> ECB destroys entity
   └─> TerrainRenderingSystem.OnDestroy cleans up mesh
   └─> TerrainPhysicsSystem.OnDestroy cleans up collider
```

### Frame-by-Frame Example

**Frame 0:** Player at (0, 0, 0)
- TileSpawningSystem: Creates 9 tiles in 3x3 grid around player
- Entities created with empty buffers

**Frame 1:** Tiles exist
- TerrainMeshGenerationSystem: Processes all 9 tiles, generates mesh data
- Buffers now contain vertex/normal/uv/index data

**Frame 2:** Mesh data ready
- TerrainPhysicsSystem: Creates colliders for all 9 tiles
- TerrainRenderingSystem: Creates Unity meshes and sets up rendering
- Terrain now visible and collidable

**Frame 50:** Player moves to (150, 0, 0)
- Player crosses into grid (1, 0)
- TileSpawningSystem: Spawns 3 new tiles, despawns 3 old tiles
- New tiles go through generation pipeline (frames 51-52)

**Frame 200:** Player at (2500, 0, 0)
- FloatingOriginSystem: Distance from origin > 2000m threshold
- System shifts world: all entities move by -playerPosition
- accumulatedOffset updated to maintain terrain consistency
- Player now at approximately (0, 0, 0) again

---

## Core Components

### Entity Structure

A typical terrain tile entity has the following components:

```
Entity: TerrainTile_Entity_123
├── TerrainTile
│   ├── gridCoordinate: (2, -1)
│   ├── meshGenerated: true
│   └── needsRegeneration: false
│
├── LocalTransform
│   ├── Position: (200, 0, -100)
│   ├── Rotation: identity
│   └── Scale: 1
│
├── LocalToWorld (4x4 matrix)
│
├── FloatingOriginEnabled (tag)
│
├── DynamicBuffer<VertexElement> [1024 elements]
├── DynamicBuffer<NormalElement> [1024 elements]
├── DynamicBuffer<UVElement> [1024 elements]
├── DynamicBuffer<IndexElement> [5760 elements]
│
├── MeshReference (managed)
│   └── mesh: UnityEngine.Mesh
│
├── PhysicsCollider
│   └── Value: BlobAssetReference<Collider>
│
└── Entities Graphics Components
    ├── MaterialMeshInfo
    ├── RenderBounds
    ├── WorldRenderBounds
    └── RenderFilterSettings
```

### Singleton Entities

The system creates singleton entities for global configuration:

```
Entity: TerrainConfig_Entity
├── TerrainTileConfig
├── FloatingOriginConfig
└── WorldOriginOffset
```

These singletons are created during the baking process by `TerrainConfigAuthoring`.

---

## System Pipeline

### System Update Order

The systems are carefully ordered to ensure correct data dependencies:

1. **TileSpawningSystem** (SimulationSystemGroup, before TransformSystemGroup)
   - Earliest in frame
   - Creates/destroys entities
   - Must complete before mesh generation

2. **TerrainMeshGenerationSystem** (SimulationSystemGroup, after TileSpawningSystem)
   - Processes tiles created this frame
   - Populates mesh data buffers
   - Must complete before physics/rendering

3. **TerrainPhysicsSystem** (SimulationSystemGroup, after TerrainMeshGenerationSystem)
   - Creates colliders from mesh data
   - Can run in parallel with rendering setup

4. **TransformSystemGroup** (built-in)
   - Updates LocalToWorld matrices
   - Used by rendering for final transforms

5. **FloatingOriginSystem** (TransformSystemGroup, after LocalToWorldSystem)
   - Checks player position after transforms updated
   - Shifts world if needed

6. **TerrainRenderingSystem** (PresentationSystemGroup)
   - Latest in frame (after simulation complete)
   - Converts buffers to Unity Meshes
   - Sets up Entities Graphics rendering

### Why This Order?

- **Spawn → Generate → Physics → Render**: Natural data dependency chain
- **FloatingOriginSystem after LocalToWorldSystem**: Ensures accurate player position before checking
- **TerrainRenderingSystem in PresentationSystemGroup**: Rendering setup happens after all simulation complete

---

## Data Flow

### Procedural Generation Pipeline

```
Player Position (float3)
    ↓
Grid Coordinate (int2) = floor(position / tileSize)
    ↓
Tile World Position (float3) = gridCoord * tileSize
    ↓
Adjusted World Position (double3) = tileWorldPos + accumulatedOffset
    ↓
Noise Sampling (multi-octave Perlin)
    ↓
Height Value (float)
    ↓
Vertex Position (float3) = (localX, height, localZ)
    ↓
Mesh Buffers (VertexElement[])
    ↓
Unity Mesh Object
    ↓
Entities Graphics Rendering
    ↓
Visible Terrain
```

### Memory Flow

```
EntityCommandBuffer (Temp)
    ↓ [TileSpawningSystem]
Entity with Empty Buffers
    ↓ [TerrainMeshGenerationSystem]
Entity with Populated Buffers (64KB per tile at 32x32 resolution)
    ↓ [TerrainPhysicsSystem]
Entity + PhysicsCollider (BlobAsset)
    ↓ [TerrainRenderingSystem]
Entity + MeshReference + Rendering Components
    ↓ [Player moves away]
Entity Destroyed (meshes/colliders cleaned up in OnDestroy)
```

**Memory Per Tile (32x32 vertices):**
- Vertices: 1024 * 12 bytes = 12 KB
- Normals: 1024 * 12 bytes = 12 KB
- UVs: 1024 * 8 bytes = 8 KB
- Indices: 5760 * 4 bytes = 23 KB
- **Total Buffer Data:** ~55 KB
- Unity Mesh: ~50 KB (duplicate in managed heap)
- Physics Collider: ~30 KB (in blob asset storage)
- **Total per tile:** ~135 KB

**At 300m view distance with 100m tiles:**
- Active tiles: ~28 (circular area)
- Total memory: 3.8 MB (very reasonable)

---

## Performance Characteristics

### Computational Complexity

| Operation | Complexity | Notes |
|-----------|-----------|-------|
| Tile Spawning | O(r²) | r = viewDistance/tileSize, typically 5-10 |
| Mesh Generation | O(v²) | v = verticesPerSide, typically 16-64 |
| Normal Calculation | O(v²) | Per-vertex, 4 neighbors checked |
| Collider Creation | O(v²) | Unity.Physics processing |
| Origin Shift | O(n) | n = entities with FloatingOriginEnabled, typically 30-100 |

### Benchmark Estimates (Unity 2023.3, Intel i7, 32x32 vertices)

- **Tile Spawn:** <0.1ms per tile
- **Mesh Generation:** 0.5-1ms per tile (Burst-compiled)
- **Collider Creation:** 1-2ms per tile (managed API)
- **Rendering Setup:** 0.2-0.5ms per tile
- **Origin Shift:** <1ms for 100 entities

**Total for new area (9 tiles):** 15-30ms spread across 3-5 frames

### Optimization Strategies

1. **Burst Compilation**
   - Noise sampling fully Burst-compiled
   - Origin shift job fully Burst-compiled
   - ~10x speedup on math-heavy operations

2. **Incremental Processing**
   - Systems process only tiles that need work
   - Mesh generation skips tiles where `meshGenerated == true`
   - Rendering setup skips tiles that already have `MeshReference`

3. **Efficient Queries**
   - `_newTilesQuery` excludes already-processed tiles
   - Queries cached in OnCreate for reuse
   - Minimal iteration overhead

4. **Shared Material**
   - All tiles use same material instance
   - Enables GPU instancing for rendering
   - Reduces draw calls significantly

5. **Circular View Distance**
   - Spawns fewer corner tiles vs. square area
   - ~21% fewer tiles than square area
   - Reduces memory and generation time

### Scalability

**Can Handle:**
- Unlimited world size (thanks to floating origin)
- 50-100 active tiles comfortably at 60 FPS
- View distances up to 1000m with appropriate tile sizes

**Performance Bottlenecks:**
- Mesh generation with high vertex counts (>64x64)
- Physics collider creation on main thread
- Too many tiles active at once (>200)

**Recommended Limits:**
- Vertices Per Side: 16-32 for VR, 32-64 for desktop
- View Distance: 300-500m for typical games
- Tile Size: 50-200m depending on terrain detail needs

---

## Integration Points

### With Other Systems

**VR Player:**
- Requires `PlayerTag` on player entity
- Player entity should have `FloatingOriginEnabled` tag
- Player's LocalTransform is read each frame

**Physics:**
- Uses Unity.Physics for colliders
- Requires Havok or Unity Physics backend enabled
- Colliders respect physics layers/collision matrix

**Rendering:**
- Requires Unity Entities Graphics package
- Material must be URP-compatible
- Works with VR single-pass instanced rendering

**Addressables/SubScenes:**
- TerrainConfigAuthoring should be in a SubScene
- SubScene must be loaded before terrain spawns
- Compatible with scene streaming systems

---

## Thread Safety

### Main Thread Only:
- `TerrainRenderingSystem` (creates managed Mesh objects)
- `TerrainPhysicsSystem` (Unity.Physics API not fully Burst-compatible)
- Entity creation/destruction via EntityCommandBuffer playback

### Burst-Compiled (Worker Threads):
- Noise sampling functions
- Normal calculation
- `ShiftWorldOriginJob` (parallel entity processing)

### Thread-Safe Containers:
- `NativeParallelHashMap<int2, Entity>` in TileSpawningSystem
- `NativeArray` for intermediate calculations
- `DynamicBuffer` safe to read from jobs

---

## Next Steps

For detailed implementation guides, see:
- [TECHNICAL_DETAILS.md](TECHNICAL_DETAILS.md) - Deep dive into algorithms
- [SETUP_GUIDE.md](SETUP_GUIDE.md) - Step-by-step configuration
- [API_REFERENCE.md](API_REFERENCE.md) - Component and system documentation
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Common issues and solutions

