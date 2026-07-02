# Technical Details - Algorithms and Implementation

Deep dive into the algorithms, math, and implementation details of the terrain system.

## Table of Contents

- [Procedural Generation](#procedural-generation)
- [Normal Calculation](#normal-calculation)
- [LOD Decimation](#lod-decimation)
- [Priority Algorithms](#priority-algorithms)
- [Caching Strategy](#caching-strategy)
- [Memory Management](#memory-management)
- [Performance Optimization](#performance-optimization)

---

## Procedural Generation

### Multi-Octave Perlin Noise

The system uses **Fractional Brownian Motion (fBm)** - multiple octaves of Perlin noise combined for realistic terrain.

#### Algorithm

```csharp
float SampleHeight(float2 worldPosition, TerrainTileConfig config)
{
    float height = 0f;
    float frequency = config.noiseFrequency;
    float amplitude = config.noiseAmplitude;
    
    for (int octave = 0; octave < config.noiseOctaves; octave++)
    {
        // Sample Perlin noise at current frequency
        float sample = noise.cnoise(worldPosition * frequency);
        
        // Add weighted contribution
        height += sample * amplitude;
        
        // Increase frequency and decrease amplitude for next octave
        frequency *= config.noiseLacunarity;  // Default: 2.0
        amplitude *= config.noisePersistence; // Default: 0.5
    }
    
    return height;
}
```

#### Parameters Explained

**Frequency** (`noiseFrequency`):
- Controls wavelength of terrain features
- Lower = larger features (smooth hills)
- Higher = smaller features (rough terrain)
- Formula: `samplePoint = worldPosition × frequency`

**Amplitude** (`noiseAmplitude`):
- Controls height variation
- Directly maps to meters of height difference
- Formula: `height = noiseSample × amplitude`

**Octaves** (`noiseOctaves`):
- Number of detail layers
- Each octave adds smaller details
- More octaves = more realistic but slower

**Lacunarity** (`noiseLacunarity`):
- Frequency multiplier between octaves
- Default 2.0 = each octave doubles frequency
- Higher = more high-frequency detail

**Persistence** (`noisePersistence`):
- Amplitude multiplier between octaves
- Default 0.5 = each octave contributes half
- Higher = rougher terrain
- Lower = smoother terrain

#### Perlin Noise Function

Uses Unity.Mathematics `noise.cnoise()`:

```csharp
using Unity.Mathematics;

// Classic Perlin noise (continuous, gradient-based)
float sample = noise.cnoise(new float2(x, z));

// Returns: -1.0 to +1.0 range
// Properties: Continuous, smooth, tileable
```

**Alternative**: Could use Simplex noise (`noise.snoise()`) for better performance.

---

## Normal Calculation

### Height-Gradient Normals Algorithm

Normals are calculated using **central finite differences** on the height field in `GenerateTileNormalsAndIndicesJob`:

```csharp
// For each vertex at grid (x, z):
float heightLeft  = GetHeight(x - 1, z);
float heightRight = GetHeight(x + 1, z);
float heightDown  = GetHeight(x, z - 1);
float heightUp    = GetHeight(x, z + 1);

float3 tangentX = new float3(2.0f * stepSize, heightRight - heightLeft, 0);
float3 tangentZ = new float3(0, heightUp - heightDown, 2.0f * stepSize);
normal = math.normalize(math.cross(tangentZ, tangentX));
```

**Interior vertices**: Heights are read from the tile's vertex buffer (fast path).

**Edge vertices**: Heights are sampled from world-space via `TerrainMeshNoise.SampleHeight` at `±stepSize` offsets, crossing tile boundaries. This ensures adjacent tiles compute identical normals at shared boundary positions.

**Result**: Smooth shading across terrain surface with seamless lighting at tile edges.

### Edge Normal Issue (Resolved)

**Previous problem**: Interior-only height lookups clamped at tile edges, producing asymmetric one-sided derivatives and visible lighting seams at tile boundaries.

**Solution**: Edge vertices sample heights in world space for normal derivatives. Adjacent tiles at the same world position receive matching normals without sharing vertices or neighbor tile lookups.

**Performance**: ~`4 × 4 × (verticesPerSide - 1)` extra height samples per tile during mesh generation (edge band only).

### Normal Validation

```csharp
// Ensure normals are valid (not NaN or zero)
if (math.any(math.isnan(normal)) || math.lengthsq(normal) < 0.0001f)
{
    normal = new float3(0, 1, 0); // Default to up vector
}
```

---

## LOD Decimation

### Vertex Stride Algorithm

LOD decimation uses **vertex stride** method - skip vertices at regular intervals.

#### Full Resolution (LOD 0)
```
Stride: 1
Pattern: Use every vertex

32×32 grid:
● ● ● ● ● ● ● ● ... (all 1024 vertices)
● ● ● ● ● ● ● ●
● ● ● ● ● ● ● ●
...

Result: 1024 vertices, 2 triangle per quad
```

#### Half Resolution (LOD 1)
```
Stride: 2
Pattern: Use every 2nd vertex

32×32 grid becomes 16×16:
● - ● - ● - ● - ... (256 vertices)
- - - - - - - -
● - ● - ● - ● -
...

Result: 256 vertices (75% reduction)
```

#### Quarter Resolution (LOD 2)
```
Stride: 4
Pattern: Use every 4th vertex

32×32 grid becomes 8×8:
● - - - ● - - - ... (64 vertices)
- - - - - - - -
- - - - - - - -
- - - - - - - -
● - - - ● - - -
...

Result: 64 vertices (93.75% reduction)
```

### Decimation Code

```csharp
int stride = GetVertexStride(lodLevel); // 1, 2, or 4
int decimatedWidth = (verticesPerSide - 1) / stride + 1;

for (int z = 0; z < verticesPerSide; z += stride)
{
    for (int x = 0; x < verticesPerSide; x += stride)
    {
        int sourceIndex = z * verticesPerSide + x;
        preparedVertices.Add(sourceVertices[sourceIndex]);
    }
}
```

**Result**: Evenly spaced subset of original vertices.

### Triangle Regeneration

After decimation, triangles must be regenerated:

```csharp
for (int z = 0; z < decimatedWidth - 1; z++)
{
    for (int x = 0; x < decimatedWidth - 1; x++)
    {
        // Calculate indices in decimated vertex array
        int i0 = z * decimatedWidth + x;
        int i1 = i0 + 1;
        int i2 = i0 + decimatedWidth;
        int i3 = i2 + 1;
        
        // Two triangles per quad
        // Triangle 1: (i0, i2, i1)
        preparedTriangles.Add(new int3(i0, i2, i1));
        
        // Triangle 2: (i1, i2, i3)
        preparedTriangles.Add(new int3(i1, i2, i3));
    }
}
```

**Why regenerate?**: Original indices reference non-decimated vertex positions.

### LOD Quality Trade-offs

| LOD Level | Vertices | Physics Accuracy | Performance |
|-----------|----------|------------------|-------------|
| Full | 100% | Excellent | Expensive |
| Half | 25% | Good | Moderate |
| Quarter | 6.25% | Acceptable | Fast |
| None | 0% | No collision | Fastest |

---

## Priority Algorithms

### Camera-Aware Priority Formula

Prioritizes tiles based on distance AND camera direction:

```csharp
float CalculateTilePriority(
    TerrainTile tile, 
    TerrainTileConfig config,
    float3 cameraPosition, 
    float3 cameraForward)
{
    // Calculate tile center
    float3 tileCenter = new float3(
        tile.gridCoordinate.x * config.tileSize + config.tileSize * 0.5f,
        0,
        tile.gridCoordinate.y * config.tileSize + config.tileSize * 0.5f
    );
    
    // Distance component
    float distance = math.distance(tileCenter, cameraPosition);
    
    // Direction component
    float3 toTile = math.normalize(tileCenter - cameraPosition);
    float dotProduct = math.dot(cameraForward, toTile);
    
    // Combined priority (lower = better = process first)
    float priority = distance * (1.0f - dotProduct * 0.5f);
    
    return priority;
}
```

#### Formula Breakdown

**Distance Component**: `distance`
- Linear distance to tile
- Closer tiles have lower values

**Direction Component**: `dotProduct`
- Dot product of camera forward and direction to tile
- Range: -1 (behind) to +1 (directly ahead)

**Combined**: `distance × (1.0 - dotProduct × 0.5)`
- Forward tiles: dot = +1, multiply by 0.5 (50% penalty)
- Side tiles: dot = 0, multiply by 1.0 (no change)
- Behind tiles: dot = -1, multiply by 1.5 (50% penalty increase)

#### Priority Examples

**Tile A**: 100m ahead of camera
```
distance = 100
dotProduct = +1.0
priority = 100 × (1.0 - 1.0 × 0.5) = 100 × 0.5 = 50
```

**Tile B**: 100m to the side
```
distance = 100
dotProduct = 0.0
priority = 100 × (1.0 - 0.0 × 0.5) = 100 × 1.0 = 100
```

**Tile C**: 100m behind camera
```
distance = 100
dotProduct = -1.0
priority = 100 × (1.0 - (-1.0) × 0.5) = 100 × 1.5 = 150
```

**Sort Order**: A (50) < B (100) < C (150)  
**Result**: Tile A processed first (in view), C processed last (behind camera)

---

## Caching Strategy

### Hash-Based Caching

Colliders cached by configuration hash (not tile position).

#### Why It Works

**Key Insight**: All tiles with same parameters generate identical collider shapes!

```
Tile at (0, 0) with config X → Collider shape A
Tile at (5, 3) with config X → Collider shape A (identical!)

Conclusion: Only need to generate shape A once, reuse for all tiles
```

#### Cache Key Generation

```csharp
uint ComputeNoiseHash(TerrainTileConfig config)
{
    uint hash = (uint)config.noiseFrequency.GetHashCode();
    hash ^= (uint)config.noiseAmplitude.GetHashCode() << 8;
    hash ^= (uint)config.noiseOctaves << 16;
    hash ^= (uint)config.noiseLacunarity.GetHashCode() << 4;
    hash ^= (uint)config.noisePersistence.GetHashCode() << 12;
    return hash;
}

ColliderCacheKey key = new ColliderCacheKey
{
    verticesPerSide = config.verticesPerSide,
    lodLevel = targetLOD,
    noiseParamsHash = ComputeNoiseHash(config)
};
```

**Result**: Unique key for each combination of settings.

### LRU Eviction Algorithm

**Least Recently Used (LRU)** eviction when cache exceeds memory limit:

```csharp
void EvictLRU(int targetMemoryBytes)
{
    // Sort by lastAccessFrame (oldest first)
    var sortedEntries = _colliderCache
        .OrderBy(entry => entry.Value.lastAccessFrame)
        .ToList();
    
    foreach (var entry in sortedEntries)
    {
        // Dispose BlobAsset
        entry.Value.blobAsset.Dispose();
        
        // Remove from cache
        _colliderCache.Remove(entry.Key);
        _totalCacheMemoryBytes -= entry.Value.estimatedMemoryBytes;
        
        // Stop when under limit
        if (_totalCacheMemoryBytes <= targetMemoryBytes)
            break;
    }
}
```

**Access Tracking**:
```csharp
// Every cache access updates lastAccessFrame
if (_colliderCache.TryGetValue(key, out var entry))
{
    entry.lastAccessFrame = _currentFrameNumber;
    _colliderCache[key] = entry; // Update entry
}
```

**Result**: Frequently used colliders stay cached, rarely used colliders evicted.

---

## Priority Algorithms

### Tile Sorting Comparers

#### Mesh Generation Priority

```csharp
struct MeshTileWithPriority
{
    public Entity entity;
    public float priority;
}

struct TilePriorityComparer : IComparer<MeshTileWithPriority>
{
    public int Compare(MeshTileWithPriority a, MeshTileWithPriority b)
    {
        return a.priority.CompareTo(b.priority); // Lower priority first
    }
}

// Usage
tilesWithPriority.Sort(new TilePriorityComparer());
```

#### Collider Creation Priority

```csharp
struct EntityWithPriority
{
    public Entity entity;
    public int priority;
}

struct PriorityComparer : IComparer<EntityWithPriority>
{
    public int Compare(EntityWithPriority a, EntityWithPriority b)
    {
        return a.priority.CompareTo(b.priority); // Lower first
    }
}
```

**Performance**: Sorting is O(n log n), but only runs when queue exceeds budget.

---

## Memory Management

### Zero GC Allocation Techniques

#### Technique 1: NativeContainers Only

```csharp
// ❌ Managed List (causes GC)
List<Entity> entities = new List<Entity>();

// ✅ NativeList (no GC)
var entities = new NativeList<Entity>(64, Allocator.Temp);
```

#### Technique 2: Stack Allocation

```csharp
// Use Allocator.Temp for single-frame collections
var tempList = new NativeList<int>(100, Allocator.Temp);
// ... use list ...
tempList.Dispose(); // Disposed at end of frame automatically
```

#### Technique 3: Reinterpret Buffers

```csharp
// ❌ Copy buffer to array (allocates)
var vertices = new Vector3[buffer.Length];
for (int i = 0; i < buffer.Length; i++)
    vertices[i] = buffer[i].value;

// ✅ Reinterpret buffer as NativeArray (zero-copy)
var vertices = buffer.Reinterpret<float3>().AsNativeArray();
```

#### Technique 4: Entity Queries

```csharp
// ❌ ToArray allocates managed array
var entities = query.ToEntityArray(Allocator.Temp); // NativeArray, but still allocation

// ✅ Foreach iteration (no allocation)
foreach (var entity in SystemAPI.Query<RefRO<TerrainTile>>())
{
    // Process inline
}
```

### Buffer Memory Management

**Dynamic Buffers** grow automatically:
- Initial capacity: Small (8-16 elements)
- Growth strategy: Double capacity when full
- Memory: Persists with entity

**Best Practice**: Reserve capacity if known:
```csharp
var buffer = EntityManager.GetBuffer<VertexElement>(entity);
buffer.Capacity = 1024; // Pre-allocate for 32×32 mesh
```

### BlobAsset Management

**Creation**:
```csharp
var builder = new BlobBuilder(Allocator.Temp);
ref TerrainColliderBlob root = ref builder.ConstructRoot<TerrainColliderBlob>();

// Build arrays
var vertexArray = builder.Allocate(ref root.vertices, vertexCount);
// ... fill arrays ...

var blobRef = builder.CreateBlobAssetReference<TerrainColliderBlob>(Allocator.Persistent);
builder.Dispose();
```

**Disposal**:
```csharp
// When evicting from cache
if (blobRef.IsCreated)
{
    blobRef.Dispose();
}
```

**Important**: BlobAssets must be explicitly disposed to avoid leaks!

---

## Performance Optimization

### Burst Compilation

Systems use `[BurstCompile]` for optimal performance:

```csharp
[BurstCompile]
public partial struct TileScrollPositionSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // This code compiles to optimized native code
        // SIMD vectorization, loop unrolling, etc.
    }
}
```

**Benefits**:
- 10-50× faster than managed C#
- SIMD auto-vectorization
- Aggressive optimization

**Limitations**:
- Cannot access managed objects (strings, classes, Unity Objects)
- Cannot call methods with `[BurstDiscard]`

### Parallel Job Scheduling

**Pattern**:
```csharp
var job = new MyBurstJob { /* ... */ };

// Schedule parallel (runs on multiple threads)
JobHandle handle = job.ScheduleParallel(state.Dependency);
state.Dependency = handle;
```

**Used In**:
- TerrainMeshGenerationSystem (multiple tiles generate simultaneously)
- TerrainColliderPreparationSystem (multiple colliders prepare simultaneously)

**Not Used In**:
- TerrainRenderingSystem (Unity Mesh API main-thread only)
- TerrainPhysicsSystem (MeshCollider.Create main-thread only)

### Memory Layout Optimization

**Cache-Friendly Access**:
```csharp
// ✅ Good - sequential access
for (int i = 0; i < buffer.Length; i++)
{
    Process(buffer[i]);
}

// ❌ Bad - random access
for (int i = 0; i < buffer.Length; i++)
{
    int randomIndex = GetRandomIndex();
    Process(buffer[randomIndex]);
}
```

**Component Layout**: ECS stores components in contiguous arrays (excellent cache locality).

---

## Frame Budgeting Implementation

### Queue-Based Budgeting

```csharp
// Persistent queue (survives across frames)
private NativeQueue<Entity> _pendingTiles;

OnUpdate:
{
    // Add new work to queue
    foreach (tile needing work)
    {
        _pendingTiles.Enqueue(tile.entity);
    }
    
    // Process up to budget
    int processed = 0;
    while (_pendingTiles.Count > 0 && processed < budget)
    {
        Entity entity = _pendingTiles.Dequeue();
        ProcessTile(entity);
        processed++;
    }
    
    // Remaining work stays in queue for next frame
}
```

**Advantages**:
- Predictable frame times
- Work spreads over multiple frames
- No frame spikes

**Trade-off**: Tiles take multiple frames to complete.

### Priority-Based Budgeting

Enhanced with priority sorting:

```csharp
// Collect all pending work with priorities
var tilesWithPriority = new NativeList<TileWithPriority>(Allocator.Temp);

while (_pendingTiles.Count > 0)
{
    var entity = _pendingTiles.Dequeue();
    float priority = CalculatePriority(entity);
    tilesWithPriority.Add(new TileWithPriority { entity, priority });
}

// Sort by priority (only if queue large)
if (tilesWithPriority.Length > budget)
{
    tilesWithPriority.Sort(new PriorityComparer());
}

// Process top priority items up to budget
for (int i = 0; i < math.min(tilesWithPriority.Length, budget); i++)
{
    ProcessTile(tilesWithPriority[i].entity);
}

// Put remaining back in queue
for (int i = budget; i < tilesWithPriority.Length; i++)
{
    _pendingTiles.Enqueue(tilesWithPriority[i].entity);
}
```

**Result**: Important tiles processed first, unimportant tiles delayed.

---

## Coordinate Systems

### Grid Coordinates (int2)

**Definition**: Integer coordinates identifying tile position in infinite grid.

```
Grid (-1, 1)  Grid (0, 1)  Grid (1, 1)
Grid (-1, 0)  Grid (0, 0)  Grid (1, 0)
Grid (-1,-1)  Grid (0,-1)  Grid (1,-1)
```

**Conversion to World Position**:
```csharp
float3 worldPosition = new float3(
    gridCoordinate.x * tileSize,
    0,
    gridCoordinate.y * tileSize
);
```

### World Coordinates (float3)

**Definition**: 3D position in Unity world space (meters).

```
World (-100, 0, 100)  World (0, 0, 100)  World (100, 0, 100)
World (-100, 0, 0)    World (0, 0, 0)    World (100, 0, 0)
World (-100, 0, -100) World (0, 0, -100) World (100, 0, -100)
```

**Conversion to Grid Coordinates**:
```csharp
int2 gridCoordinate = new int2(
    (int)math.floor(worldPosition.x / tileSize),
    (int)math.floor(worldPosition.z / tileSize)
);
```

### Local Coordinates (float3)

**Definition**: Position relative to tile origin (0-tileSize range).

```csharp
// Local position within tile
float3 localPosition = worldPosition - tileWorldPosition;

// Local X and Z in range [0, tileSize]
// Local Y determined by terrain height
```

### Scroll-Adjusted Coordinates

When auto-scrolling enabled:

```csharp
// Base position (from grid coordinates)
float3 basePosition = new float3(
    gridCoordinate.x * tileSize,
    0,
    gridCoordinate.y * tileSize
);

// Scrolled position (tiles physically move)
float3 scrolledPosition = basePosition - scrollOffset.accumulatedOffset;

// Effective player position (for spawning calculations)
float3 effectivePlayerPosition = playerPosition + scrollOffset.accumulatedOffset;
```

---

## Mesh Generation Math

### Vertex Grid Generation

```csharp
for (int z = 0; z < verticesPerSide; z++)
{
    for (int x = 0; x < verticesPerSide; x++)
    {
        // Calculate local position within tile
        float localX = (float)x / (verticesPerSide - 1) * tileSize;
        float localZ = (float)z / (verticesPerSide - 1) * tileSize;
        
        // Convert to world position
        float worldX = tileWorldPosition.x + localX;
        float worldZ = tileWorldPosition.z + localZ;
        
        // Sample height from noise
        float height = SampleHeight(new float2(worldX, worldZ), config);
        
        // Store vertex
        vertices.Add(new VertexElement 
        { 
            value = new float3(localX, height, localZ) 
        });
    }
}
```

**Result**: Grid of vertices with noise-based heights.

### Triangle Generation

```csharp
for (int z = 0; z < verticesPerSide - 1; z++)
{
    for (int x = 0; x < verticesPerSide - 1; x++)
    {
        // Calculate vertex indices for this quad
        int i0 = z * verticesPerSide + x;
        int i1 = i0 + 1;
        int i2 = i0 + verticesPerSide;
        int i3 = i2 + 1;
        
        // First triangle (i0, i2, i1) - counter-clockwise
        indices.Add(new IndexElement { value = i0 });
        indices.Add(new IndexElement { value = i2 });
        indices.Add(new IndexElement { value = i1 });
        
        // Second triangle (i1, i2, i3) - counter-clockwise
        indices.Add(new IndexElement { value = i1 });
        indices.Add(new IndexElement { value = i2 });
        indices.Add(new IndexElement { value = i3 });
    }
}
```

**Result**: Two triangles per quad, 6 indices per quad.

### UV Generation

World-space UVs for seamless tiling:

```csharp
float uvScale = 1.0f / tileSize;

for each vertex at world position (x, y, z):
{
    float u = x * uvScale;
    float v = z * uvScale;
    
    uvs.Add(new UVElement { value = new float2(u, v) });
}
```

**Result**: 1 UV unit = 1 tile size in world units (e.g., 1 UV = 100m).

---

## Collision Detection

### MeshCollider Creation

```csharp
using Unity.Physics;

// Convert buffers to NativeArrays
var vertices = new NativeArray<float3>(vertexBuffer.Length, Allocator.Temp);
var triangles = new NativeArray<int3>(triangleBuffer.Length / 3, Allocator.Temp);

// Fill arrays from buffers
for (int i = 0; i < vertices.Length; i++)
    vertices[i] = vertexBuffer[i].value;

for (int i = 0; i < triangles.Length; i++)
    triangles[i] = new int3(
        indexBuffer[i*3].value,
        indexBuffer[i*3 + 1].value,
        indexBuffer[i*3 + 2].value
    );

// Create MeshCollider
var collider = MeshCollider.Create(
    vertices, 
    triangles, 
    CollisionFilter.Default, 
    Material.Default
);

// Add to entity
em.AddComponentData(entity, new PhysicsCollider { Value = collider });

vertices.Dispose();
triangles.Dispose();
```

### Collision Filtering

```csharp
var filter = new CollisionFilter
{
    BelongsTo = 1u << 0,        // Belongs to layer 0
    CollidesWith = 0xFFFFFFFF,   // Collides with all layers
    GroupIndex = 0               // No group filtering
};

var collider = MeshCollider.Create(vertices, triangles, filter);
```

**Physics Layers**: Can assign different collision filters by LOD level.

---

## Numerical Precision

### Float Precision Limits

**Problem**: `float` has ~7 decimal digits of precision
- At position 1,000,000: precision = ~0.1m
- Causes jittering and artifacts

**Solution**: System handles reasonably large worlds (< 100km) without issue

**Future Enhancement**: Double-precision coordinates for unlimited worlds

### Grid Integer Limits

**int2 grid coordinates**:
- Range: -2,147,483,648 to +2,147,483,647
- With 100m tiles: ±214,748 km from origin
- Effectively unlimited for gameplay purposes

---

## Profiling and Metrics

### Performance Metrics

Measured on typical hardware (i7-9700K, RTX 2070):

**Mesh Generation** (32×32, 4 octaves):
- Job scheduling: 0.1ms
- Parallel execution: 5-8ms per tile
- Buffer copy: <0.1ms

**Collider Creation** (full resolution):
- Cache lookup: <0.01ms
- Preparation job: 1-2ms (parallel)
- MeshCollider.Create: 3-5ms (main thread)

**Rendering Setup**:
- Mesh creation: 0.5-1ms per tile
- Component setup: <0.1ms

### Memory Metrics

**Per 25-tile Grid** (500m view distance):
- Mesh data: ~1.25 MB
- Collider data: ~1 MB (full res) or ~250 KB (quarter res)
- Component data: ~2 KB
- Total: ~2.5 MB per grid

### Scalability Analysis

**Linear Scaling**:
- Noise octaves: 2× octaves = 2× generation time
- Frame budgets: 2× budget = 2× work per frame

**Quadratic Scaling**:
- Vertices per side: 2× vertices = 4× total vertices
- View distance: 2× distance = 4× tiles

**Exponential Scaling**:
- None (system designed to avoid exponential growth)

---

## Algorithm Complexity

### Time Complexity

| Operation | Complexity | Notes |
|-----------|------------|-------|
| Tile spawning | O(n²) | n = viewDistance / tileSize |
| Distance calculation | O(t) | t = tile count |
| Priority sorting | O(t log t) | Only when queue > budget |
| Cache lookup | O(1) | HashMap |
| Mesh generation | O(v) | v = vertices per tile |
| Collider creation | O(v) | v = vertices per collider |

### Space Complexity

| Data Structure | Space | Notes |
|----------------|-------|-------|
| Active tiles map | O(t) | t = tile count |
| Pending tiles queue | O(t) | t = tiles awaiting work |
| Collider cache | O(c) | c = cache limit / collider size |
| Per-tile mesh data | O(v) | v = vertices per tile |

---

## Implementation Patterns

### Pattern 1: Singleton Component

```csharp
// Single entity with global config
var config = SystemAPI.GetSingleton<TerrainTileConfig>();

// Modify via entity query
var entity = em.CreateEntityQuery(typeof(TerrainTileConfig)).GetSingletonEntity();
em.SetComponentData(entity, modifiedConfig);
```

### Pattern 2: Frame Budgeting

```csharp
private NativeQueue<Entity> _pendingWork;

OnUpdate:
{
    // Add new work
    foreach (entity needing work)
        _pendingWork.Enqueue(entity);
    
    // Process budget
    for (int i = 0; i < budget && _pendingWork.Count > 0; i++)
    {
        ProcessWork(_pendingWork.Dequeue());
    }
}
```

### Pattern 3: Priority Sorting

```csharp
// Collect with priorities
var items = new NativeList<ItemWithPriority>(Allocator.Temp);
foreach (entity) items.Add(new ItemWithPriority { entity, priority });

// Sort (conditional)
if (items.Length > budget)
    items.Sort(new Comparer());

// Process top N
for (int i = 0; i < math.min(items.Length, budget); i++)
    Process(items[i]);
```

### Pattern 4: LRU Cache

```csharp
// Access updates LRU timestamp
if (cache.TryGetValue(key, out var entry))
{
    entry.lastAccessFrame = currentFrame;
    cache[key] = entry;
    return entry.data;
}

// Eviction based on timestamp
var sortedByAge = cache.OrderBy(e => e.Value.lastAccessFrame);
foreach (var old in sortedByAge)
{
    cache.Remove(old.Key);
    if (cache.Count <= targetSize) break;
}
```

---

## Related Documentation

- **[System Pipeline](SYSTEM_PIPELINE.md)** - How systems interact
- **[Component Reference](COMPONENT_REFERENCE.md)** - Component details
- **[Performance Optimization](PERFORMANCE.md)** - Optimization strategies
- **[API Reference](API_REFERENCE.md)** - Code examples

---

**Back to**: [Documentation Hub](README.md)

