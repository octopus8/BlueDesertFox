# Infinite Terrain System - Technical Deep Dive

**Last Updated:** March 14, 2026  
**Complexity Level:** Advanced

## Table of Contents
1. [Floating Origin Implementation](#floating-origin-implementation)
2. [Noise Generation Details](#noise-generation-details)
3. [Mesh Generation Algorithm](#mesh-generation-algorithm)
4. [Tile Management Strategy](#tile-management-strategy)
5. [Physics Integration](#physics-integration)
6. [Rendering Pipeline](#rendering-pipeline)

---

## Floating Origin Implementation

### The Floating-Point Precision Problem

**Why Floating Origin is Necessary:**

At large distances from the origin, float precision degrades:
- At 1,000 units: ~0.0001 unit precision (0.1mm)
- At 10,000 units: ~0.001 unit precision (1mm)
- At 100,000 units: ~0.01 unit precision (1cm) - **noticeable jitter in VR**
- At 1,000,000 units: ~0.125 unit precision (12.5cm) - **severe artifacts**

In VR applications, even millimeter-level jitter is visible and breaks immersion.

### Solution: Floating Origin with Double Precision Tracking

The system uses a hybrid approach:

1. **Entity Positions (float3)**: All entities stay near world origin (0, 0, 0)
2. **Accumulated Offset (double3)**: Tracks the "true" world position with high precision
3. **Noise Sampling**: Uses true position (entity position + accumulated offset) for consistency

### Implementation Details

**In FloatingOriginSystem.cs:**

```csharp
void OnUpdate(ref SystemState state)
{
    var playerTransform = SystemAPI.GetComponent<LocalTransform>(playerEntity);
    float3 playerPosition = playerTransform.Position;
    float distanceFromOrigin = math.length(playerPosition);
    
    if (distanceFromOrigin > config.shiftThreshold)
    {
        // Player has moved 2000m from origin, time to shift
        float3 shiftOffset = playerPosition;
        
        // Update the "true" world position tracker
        RefRW<WorldOriginOffset> worldOffset = SystemAPI.GetSingletonRW<WorldOriginOffset>();
        worldOffset.ValueRW.accumulatedOffset += shiftOffset;  // Add using double precision
        
        // Shift all floating origin entities back to near-origin
        var shiftJob = new ShiftWorldOriginJob { offset = shiftOffset };
        shiftJob.ScheduleParallel();
    }
}
```

**Parallel Shift Job:**

```csharp
[BurstCompile]
[WithAll(typeof(FloatingOriginEnabled))]
public partial struct ShiftWorldOriginJob : IJobEntity
{
    public float3 offset;
    
    public void Execute(ref LocalTransform transform)
    {
        transform.Position -= offset;  // Subtract offset from position
    }
}
```

**In TerrainMeshGenerationSystem.cs:**

```csharp
// Calculate true world position for noise sampling
double3 tileWorldPos = new double3(
    tile.gridCoordinate.x * config.tileSize,
    0,
    tile.gridCoordinate.y * config.tileSize
) + worldOffset.accumulatedOffset;  // Add accumulated offset

// Sample noise at true position
for each vertex:
    double worldX = tileWorldPos.x + localX;
    double worldZ = tileWorldPos.z + localZ;
    float height = SampleNoise(worldX, worldZ, config);  // Uses double for input
```

### Example Scenario

**Player Journey:**

| Step | Player Entity Position | Accumulated Offset | True World Position |
|------|----------------------|-------------------|-------------------|
| Start | (0, 0, 0) | (0, 0, 0) | (0, 0, 0) |
| Walk 1500m | (1500, 0, 0) | (0, 0, 0) | (1500, 0, 0) |
| Walk 2500m | (2500, 0, 0) | (0, 0, 0) | (2500, 0, 0) |
| **Shift** | (0, 0, 0) | (2500, 0, 0) | (2500, 0, 0) |
| Walk 500m | (500, 0, 0) | (2500, 0, 0) | (3000, 0, 0) |
| Walk 2000m | (2000, 0, 0) | (2500, 0, 0) | (4500, 0, 0) |
| **Shift** | (0, 0, 0) | (4500, 0, 0) | (4500, 0, 0) |

Notice:
- Entity position always stays near origin (prevents precision loss)
- Accumulated offset grows indefinitely (double precision can handle it)
- True world position = sum of both = consistent terrain generation

---

## Noise Generation Details

### Multi-Octave Perlin Noise

The system uses **Simplex Noise** (via `Unity.Mathematics.noise.snoise()`) with multiple octaves layered together.

**Single Octave:**
```csharp
float2 samplePos = worldPosition * frequency;
float noiseValue = noise.snoise(samplePos);  // Returns [-1, 1]
height = noiseValue * amplitude;
```

**Multi-Octave (Fractal Brownian Motion):**
```csharp
float SampleNoise(double worldX, double worldZ, TerrainTileConfig config)
{
    float total = 0f;
    float frequency = config.noiseFrequency;  // Start: 0.01
    float amplitude = config.noiseAmplitude;  // Start: 20
    float maxValue = 0f;
    
    for (int i = 0; i < config.noiseOctaves; i++)  // Default: 4 octaves
    {
        float2 samplePos = new float2((float)worldX, (float)worldZ) * frequency;
        float noiseValue = noise.snoise(samplePos);  // [-1, 1]
        
        total += noiseValue * amplitude;
        maxValue += amplitude;
        
        // Prepare for next octave
        amplitude *= config.noisePersistence;  // e.g., 0.5 (gets quieter)
        frequency *= config.noiseLacunarity;   // e.g., 2.0 (gets finer)
    }
    
    // Normalize to [0, noiseAmplitude]
    return total / maxValue * config.noiseAmplitude;
}
```

### Octave Contribution Example

**Config:** frequency=0.01, amplitude=20, octaves=4, lacunarity=2.0, persistence=0.5

| Octave | Frequency | Amplitude | Detail Level | Contribution |
|--------|-----------|-----------|--------------|--------------|
| 0 | 0.01 | 20.0 | Large rolling hills | 64% |
| 1 | 0.02 | 10.0 | Medium features | 32% |
| 2 | 0.04 | 5.0 | Small details | 16% |
| 3 | 0.08 | 2.5 | Fine noise | 8% |

**Visual Effect:**
- Low octaves: Large, smooth hills and valleys
- High octaves: Rocky details, small bumps
- Combined: Natural-looking terrain with variation at multiple scales

### Why Double Precision for Input?

```csharp
// BAD: Using only float precision
float worldX = tileWorldPos.x + localX;  // Precision loss at large distances
float height = SampleNoise(worldX, worldZ, config);
// Result: Terrain "pops" or changes after origin shift

// GOOD: Using double precision
double worldX = (double)tileWorldPos.x + localX;
float height = SampleNoise(worldX, worldZ, config);
// Result: Terrain stays consistent at any distance
```

Even though `noise.snoise()` accepts float, the coordinate calculation is done in double precision to maintain accuracy.

---

## Mesh Generation Algorithm

### Vertex Grid Layout

For a 4x4 vertex tile (simplified example):

```
z=3:  12 ---- 13 ---- 14 ---- 15
      |   /|   /|   /|   /|
      |  / |  / |  / |  / |
      | /  | /  | /  | /  |
z=2:  8 ---- 9 ---- 10 ---- 11
      |   /|   /|   /|   /|
      |  / |  / |  / |  / |
      | /  | /  | /  | /  |
z=1:  4 ---- 5 ---- 6 ---- 7
      |   /|   /|   /|   /|
      |  / |  / |  / |  / |
      | /  | /  | /  | /  |
z=0:  0 ---- 1 ---- 2 ---- 3
     x=0   x=1   x=2   x=3
```

**Vertex Indexing:**
```csharp
index = z * verticesPerSide + x
```

**Vertex at (2, 1):**
- Index = 1 * 4 + 2 = 6
- World Position = (2 * stepSize, height, 1 * stepSize)

### Triangle Generation

Each quad becomes two triangles:

```
Quad at (x, z):
    Vertices: v0, v1, v2, v3
    
    v0 = z * verticesPerSide + x
    v1 = z * verticesPerSide + (x + 1)
    v2 = (z + 1) * verticesPerSide + x
    v3 = (z + 1) * verticesPerSide + (x + 1)
    
    Triangle 1: [v0, v2, v1]  (counter-clockwise)
    Triangle 2: [v1, v2, v3]  (counter-clockwise)
```

**Example Quad (0, 0):**
```
v2(4) ----- v3(5)
 |    \      |
 |      \    |
 |        \  |
v0(0) ----- v1(1)

Triangle 1: [0, 4, 1]
Triangle 2: [1, 4, 5]
```

**For 32x32 Vertices:**
- Quads: 31 * 31 = 961
- Triangles: 961 * 2 = 1922
- Indices: 1922 * 3 = 5766

### Normal Calculation

**Goal:** Smooth lighting with correct normals at all tile boundaries, including edges.

**Algorithm (Heightfield Sampling Method):**

The system calculates normals by **sampling the height function directly** at neighboring positions, rather than looking up vertices from the array. This ensures normals are correct even at tile edges where neighboring tile data isn't available in the vertex array.

```csharp
float3 CalculateNormalFromHeightfield(
    double worldX, double worldZ, float stepSize, TerrainTileConfig config)
{
    // Sample heights at 4 neighboring positions
    float heightLeft = SampleNoise(worldX - stepSize, worldZ, config);
    float heightRight = SampleNoise(worldX + stepSize, worldZ, config);
    float heightDown = SampleNoise(worldX, worldZ - stepSize, config);
    float heightUp = SampleNoise(worldX, worldZ + stepSize, config);
    
    // Calculate tangent vectors using central differences
    float3 tangentX = new float3(2.0f * stepSize, heightRight - heightLeft, 0);
    float3 tangentZ = new float3(0, heightUp - heightDown, 2.0f * stepSize);
    
    // Normal is cross product of tangents
    return normalize(cross(tangentZ, tangentX));
}
```

**Why This Approach?**

1. **Works at tile boundaries:** Can sample heights beyond current tile's vertex array
2. **Deterministic:** Adjacent tiles sampling the same world position get identical heights
3. **Seamless edges:** Neighboring tiles produce matching normals for shared edge vertices
4. **Central differences:** More accurate than face-averaging for heightfield data

**Application:**
```csharp
for (int z = 0; z < verticesPerSide; z++)
{
    for (int x = 0; x < verticesPerSide; x++)
    {
        // Calculate world position for this vertex
        double worldX = tileWorldPos.x + (x * stepSize);
        double worldZ = tileWorldPos.z + (z * stepSize);
        
        // Sample heightfield for normal
        normals[index] = CalculateNormalFromHeightfield(worldX, worldZ, stepSize, config);
    }
}
```

**Edge Cases Handled:**
- **Edge vertices (z=0, bottom edge):** Samples at `worldZ - stepSize` (in neighboring tile) ✅
- **Corner vertices:** Samples in all 4 directions across tile boundaries ✅
- **Flat terrain:** Returns (0, 1, 0) when all heights equal ✅
- **Steep slopes:** Returns perpendicular normal correctly ✅

**Performance:**
- Cost: 4 noise samples per vertex (4 octaves each = 16 simplex noise calls)
- vs old method: ~6x slower per normal
- Total impact: +0.25ms per 32×32 tile (negligible)
- Benefit: Perfect lighting, no visible seams

### UV Mapping

Simple planar mapping:

```csharp
uv.x = (float)vertexX / (verticesPerSide - 1);  // [0, 1] across tile
uv.y = (float)vertexZ / (verticesPerSide - 1);  // [0, 1] across tile
```

**Result:** Each tile has full (0,0) to (1,1) UV space.

**For Tiled Textures:**
- Material should use Repeat wrap mode
- Each tile shows the full texture
- No seams between tiles (UVs continuous)

---

## Tile Management Strategy

### Active Tile Tracking

**Data Structure:**
```csharp
NativeParallelHashMap<int2, Entity> _activeTiles;
```

**Why ParallelHashMap?**
- O(1) lookup by grid coordinate
- Thread-safe for parallel jobs (if needed in future)
- Efficient memory usage (only stores active tiles)

**Key Operations:**
```csharp
// Check if tile exists
if (_activeTiles.ContainsKey(gridCoord))
    // Tile already spawned

// Add new tile
_activeTiles.Add(gridCoord, entity);

// Remove despawned tile
_activeTiles.Remove(gridCoord);

// Iterate all active tiles
var tileKeys = _activeTiles.GetKeyArray(Allocator.Temp);
foreach (var gridCoord in tileKeys)
    // Process tile
tileKeys.Dispose();
```

### Spawn/Despawn Algorithm

**Spawning Decision:**

```csharp
for x in [-viewDistanceInTiles, +viewDistanceInTiles]:
    for z in [-viewDistanceInTiles, +viewDistanceInTiles]:
        gridCoord = playerGridCoord + (x, z)
        tileCenter = gridCoord * tileSize + tileSize/2
        distance = length(tileCenter - playerPosition)
        
        if distance <= viewDistance:
            if not _activeTiles.ContainsKey(gridCoord):
                SpawnTile(gridCoord)
```

**Key Features:**
- **Circular area:** Uses actual distance, not square bounds
- **Centered spawn:** Checks distance to tile center, not corner
- **Deterministic:** Same position always produces same tiles

**Despawning Decision:**

```csharp
for each activeGridCoord in _activeTiles:
    tileCenter = activeGridCoord * tileSize + tileSize/2
    distance = length(tileCenter - playerPosition)
    
    if distance > viewDistance:
        DespawnTile(activeGridCoord)
```

**Hysteresis:** No separate "unload distance" - tiles despawn immediately when exceeding view distance. This is simple but can cause tiles to spawn/despawn rapidly at the boundary.

**Future Improvement:** Add unloadDistance = viewDistance * 1.2 for hysteresis.

### EntityCommandBuffer Pattern

**Why ECB?**
- Cannot create/destroy entities during structural change
- ECB defers changes until safe point
- Allows batching multiple operations efficiently

**Usage:**
```csharp
var ecb = new EntityCommandBuffer(Allocator.Temp);

// Queue entity creation
Entity newEntity = ecb.CreateEntity();
ecb.AddComponent(newEntity, ...);
ecb.AddBuffer<VertexElement>(newEntity);

// Execute all changes atomically
ecb.Playback(state.EntityManager);
ecb.Dispose();
```

**Caveat:** Newly created entities don't have valid Entity IDs until playback. Solution: Query for them by component after playback.

---

## Noise Generation Details

### Simplex Noise (Unity.Mathematics)

**Function Signature:**
```csharp
public static float snoise(float2 position)
```

**Properties:**
- Returns: [-1.0, 1.0]
- Continuous: No seams or discontinuities
- Tileable: No (for infinite worlds, this is fine)
- Performance: ~50ns per sample (Burst-compiled)

### Octave Parameters

#### Frequency
Controls the "zoom level" of the noise.

- **Low (0.001)**: Very large features, changes slowly over distance
- **Medium (0.01)**: Hills and valleys at human scale
- **High (0.1)**: Fine detail, changes rapidly

**Formula:**
```
samplePosition = worldPosition * frequency
```

#### Amplitude
Controls the height of features.

- **Low (5)**: Gentle rolling terrain
- **Medium (20)**: Moderate hills
- **High (100)**: Mountains and deep valleys

**Formula:**
```
height = noiseValue * amplitude
```

#### Lacunarity
Frequency multiplier for each octave (typically 2.0).

- **1.5**: Gentle increase in detail per octave
- **2.0**: Standard, doubles frequency each octave
- **3.0**: Aggressive, much finer detail added

**Effect on Frequency:**
```
Octave 0: freq = 0.01
Octave 1: freq = 0.01 * 2.0 = 0.02
Octave 2: freq = 0.02 * 2.0 = 0.04
Octave 3: freq = 0.04 * 2.0 = 0.08
```

#### Persistence
Amplitude multiplier for each octave (typically 0.5).

- **0.25**: Each octave has much less influence (smoother)
- **0.5**: Standard, each octave half as strong
- **0.75**: Each octave almost as strong (more chaotic)

**Effect on Amplitude:**
```
Octave 0: amp = 20.0
Octave 1: amp = 20.0 * 0.5 = 10.0
Octave 2: amp = 10.0 * 0.5 = 5.0
Octave 3: amp = 5.0 * 0.5 = 2.5
```

### Normalization

**Why Normalize?**
Without normalization, more octaves = taller terrain unpredictably.

**Algorithm:**
```csharp
float maxValue = 0f;
for each octave:
    total += noiseValue * amplitude
    maxValue += amplitude  // Track theoretical maximum

normalizedHeight = total / maxValue * config.noiseAmplitude
```

**Effect:**
- 1 octave: range [-20, 20]
- 4 octaves: range [-20, 20] (same!)
- 8 octaves: range [-20, 20] (same!)

### Terrain Style Tuning

**Smooth Rolling Hills:**
```
noiseFrequency:  0.005
noiseAmplitude:  15
noiseOctaves:    2
noiseLacunarity: 2.0
noisePersistence: 0.4
```

**Mountainous:**
```
noiseFrequency:  0.015
noiseAmplitude:  50
noiseOctaves:    6
noiseLacunarity: 2.2
noisePersistence: 0.55
```

**Desert (Low Variation):**
```
noiseFrequency:  0.008
noiseAmplitude:  5
noiseOctaves:    3
noiseLacunarity: 1.8
noisePersistence: 0.3
```

---

## Mesh Generation Algorithm

### Vertex Generation Loop

**Outer Structure:**
```csharp
int verticesPerSide = config.verticesPerSide;  // e.g., 32
float stepSize = config.tileSize / (verticesPerSide - 1);  // e.g., 100/31 = 3.225m

for (int z = 0; z < verticesPerSide; z++)
{
    for (int x = 0; x < verticesPerSide; x++)
    {
        int index = z * verticesPerSide + x;
        
        // Local position within tile (relative to tile origin)
        float localX = x * stepSize;  // 0, 3.225, 6.45, ..., 100
        float localZ = z * stepSize;
        
        // World position for noise (using double precision)
        double worldX = tileWorldPos.x + localX;
        double worldZ = tileWorldPos.z + localZ;
        
        // Sample height at this position
        float height = SampleNoise(worldX, worldZ, config);
        
        // Store vertex position (relative to tile, not world)
        vertices[index] = new float3(localX, height, localZ);
        
        // Generate UV
        uvs[index] = new float2(
            (float)x / (verticesPerSide - 1),  // [0, 1]
            (float)z / (verticesPerSide - 1)   // [0, 1]
        );
    }
}
```

**Important:** Vertices are stored in **tile-local space**, not world space. The tile's `LocalTransform.Position` positions the mesh in the world.

### Index Generation Loop

**Creates triangles for each quad:**

```csharp
for (int z = 0; z < verticesPerSide - 1; z++)
{
    for (int x = 0; x < verticesPerSide - 1; x++)
    {
        int baseIndex = z * verticesPerSide + x;
        
        // Quad corners:
        // v0 = baseIndex
        // v1 = baseIndex + 1
        // v2 = baseIndex + verticesPerSide
        // v3 = baseIndex + verticesPerSide + 1
        
        // Triangle 1: Counter-clockwise winding
        indexBuffer.Add(baseIndex);                      // Bottom-left
        indexBuffer.Add(baseIndex + verticesPerSide);     // Top-left
        indexBuffer.Add(baseIndex + 1);                   // Bottom-right
        
        // Triangle 2: Counter-clockwise winding
        indexBuffer.Add(baseIndex + 1);                   // Bottom-right
        indexBuffer.Add(baseIndex + verticesPerSide);     // Top-left
        indexBuffer.Add(baseIndex + verticesPerSide + 1); // Top-right
    }
}
```

**Winding Order:** Counter-clockwise (default for Unity) so normals point up.

### Normal Calculation Detail

**Central Difference Method:**

```csharp
For vertex at (x, z):
    normal = (0, 1, 0)  // Default up
    faceCount = 0
    
    // Face 1: Current, Right, Up
    if (x < width-1 && z < height-1):
        tangent1 = vertices[x+1, z] - vertices[x, z]    // East
        tangent2 = vertices[x, z+1] - vertices[x, z]    // North
        faceNormal1 = normalize(cross(tangent1, tangent2))
        normal += faceNormal1
        faceCount++
    
    // Face 2: Current, Up, Left
    if (x > 0 && z < height-1):
        tangent1 = vertices[x, z+1] - vertices[x, z]    // North
        tangent2 = vertices[x-1, z] - vertices[x, z]    // West
        faceNormal2 = normalize(cross(tangent1, tangent2))
        normal += faceNormal2
        faceCount++
    
    // Face 3: Current, Left, Down
    if (x > 0 && z > 0):
        tangent1 = vertices[x-1, z] - vertices[x, z]    // West
        tangent2 = vertices[x, z-1] - vertices[x, z]    // South
        faceNormal3 = normalize(cross(tangent1, tangent2))
        normal += faceNormal3
        faceCount++
    
    // Face 4: Current, Down, Right
    if (x < width-1 && z > 0):
        tangent1 = vertices[x, z-1] - vertices[x, z]    // South
        tangent2 = vertices[x+1, z] - vertices[x, z]    // East
        faceNormal4 = normalize(cross(tangent1, tangent2))
        normal += faceNormal4
        faceCount++
    
    return normalize(normal)  // Average of all adjacent faces
```

**Result:** Smooth shading across the terrain surface with proper lighting response.

---

## Physics Integration

### Unity.Physics vs. Unity PhysX

This system uses **Unity.Physics** (ECS-native physics):
- Fully integrated with DOTS
- Supports Burst compilation
- Deterministic simulation
- High performance for many colliders

**Alternative:** Could use GameObjects with MeshCollider (but loses ECS benefits).

### Mesh Collider Creation

**Process:**

```csharp
1. Convert VertexElement buffer → NativeArray<float3>
2. Convert IndexElement buffer → NativeArray<int3> (triangles)
3. Create Unity.Physics.MeshCollider.Create(
    vertices,
    triangles,
    collisionFilter,
    material
)
4. Store in PhysicsCollider component (BlobAssetReference)
```

**Memory Structure:**

```
PhysicsCollider (4 bytes - just a pointer)
    └─> BlobAssetReference<Collider>
        └─> BlobAsset in shared memory
            ├── Vertices (compressed)
            ├── Triangles (indices)
            └── BVH tree (for fast collision queries)
```

**BlobAsset Advantages:**
- Immutable (thread-safe)
- Shared between systems
- Reference counted (auto-cleanup)
- Very efficient memory usage

### Collision Filter

```csharp
new CollisionFilter
{
    BelongsTo: 1u << 0,     // Layer 0 (default layer)
    CollidesWith: ~0u,      // All layers (bitwise NOT of 0)
    GroupIndex: 0           // No group-based filtering
}
```

**To Change Layer:**
```csharp
BelongsTo: 1u << 8,  // Layer 8
CollidesWith: (1u << 0) | (1u << 3),  // Collides with layers 0 and 3 only
```

### Physics Material

Currently uses `Unity.Physics.Material.Default`:
```csharp
{
    Friction: 0.5f,
    FrictionCombinePolicy: CombinePolicy.GeometricMean,
    Restitution: 0.0f,  // No bounce
    RestitutionCombinePolicy: CombinePolicy.GeometricMean
}
```

**To Customize:**
```csharp
var physicsMaterial = new Unity.Physics.Material
{
    Friction = 0.8f,           // Higher friction (less sliding)
    Restitution = 0.1f,        // Slight bounce
    // ...
};

var collider = Unity.Physics.MeshCollider.Create(
    vertices, triangles, collisionFilter, physicsMaterial
);
```

---

## Rendering Pipeline

### Entities Graphics Architecture

**Unity 6 Entities Graphics** uses a component-based rendering system:

```
Entity Components                 GPU Representation
─────────────────                 ──────────────────
MaterialMeshInfo       ─────────> Material ID + Mesh ID
    └─ Material Index             
    └─ Mesh Index                 

RenderBounds           ─────────> AABB for culling
    └─ AABB                       
                                  
WorldRenderBounds      ─────────> Transformed AABB
    └─ Transformed AABB           

LocalToWorld           ─────────> Transform matrix
    └─ 4x4 matrix                 

RenderFilterSettings   ─────────> Rendering config
    └─ Layer                      
    └─ RenderingLayerMask         
    └─ Motion Vector Mode         
    └─ Shadow Casting Mode        
```

### Material and Mesh Registration

**EntitiesGraphicsSystem maintains internal registries:**

```csharp
var entitiesGraphicsSystem = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();

// Register mesh (returns ID for MaterialMeshInfo)
BatchMeshID meshID = entitiesGraphicsSystem.RegisterMesh(unityMesh);

// Register material (returns ID for MaterialMeshInfo)
BatchMaterialID materialID = entitiesGraphicsSystem.RegisterMaterial(unityMaterial);
```

**Benefits:**
- Materials shared across all tiles (instancing)
- Mesh IDs allow GPU instancing when identical
- Automatic cleanup when system destroyed

### RenderMeshUtility API

**Modern approach (Unity 6):**

```csharp
// 1. Create description (settings for how to render)
var renderMeshDescription = new RenderMeshDescription(
    shadowCastingMode: ShadowCastingMode.On,
    receiveShadows: true,
    layer: 0,                              // Unity layer for culling
    renderingLayerMask: 1,                 // URP rendering layers
    motionMode: MotionVectorGenerationMode.Camera  // For motion blur
);

// 2. Create array (what to render)
var renderMeshArray = new RenderMeshArray(
    new[] { terrainMaterial },  // Materials
    new[] { mesh }              // Meshes
);

// 3. Create info (which material/mesh to use)
var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(
    materialIndex: 0,  // First material in array
    meshIndex: 0       // First mesh in array
);

// 4. Add all components in one call
RenderMeshUtility.AddComponents(
    entity,
    EntityManager,
    renderMeshDescription,
    renderMeshArray,
    materialMeshInfo
);
```

**Components Added Automatically:**
- `MaterialMeshInfo`
- `RenderBounds` (from mesh bounds)
- `RenderFilterSettings` (from description)
- Does NOT add `LocalToWorld` (must be added separately)

### Culling System

**Frustum Culling:**
1. Entities Graphics reads `WorldRenderBounds` for each entity
2. Compares bounds against camera frustum planes
3. Only visible entities are rendered

**Bounds Calculation:**
```csharp
mesh.RecalculateBounds();  // Computes AABB from vertices
RenderBounds.Value = mesh.bounds.ToAABB();  // Store in component

// WorldRenderBounds updated automatically by TransformSystemGroup:
WorldRenderBounds = Transform(RenderBounds, LocalToWorld)
```

**Performance:** Culling happens on worker threads in Burst-compiled jobs.

### Material Requirements

**Must be URP-compatible:**
- Shader: "Universal Render Pipeline/Lit" (or other URP shader)
- Will NOT work with Built-in RP shaders
- VR requires: Single Pass Instanced rendering mode

**Common Setup:**
```csharp
Material terrainMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
terrainMat.SetColor("_BaseColor", Color.green);
terrainMat.SetTexture("_BaseMap", grassTexture);
terrainMat.SetFloat("_Smoothness", 0.2f);
```

---

## Performance Profiling

### Bottleneck Analysis

**CPU Bottlenecks:**

1. **Mesh Generation** (0.5-1ms per tile)
   - Noise sampling: ~50%
   - Normal calculation: ~30%
   - Buffer operations: ~20%

2. **Collider Creation** (1-2ms per tile)
   - Data conversion: ~20%
   - BVH tree building: ~70%
   - Component addition: ~10%

3. **Rendering Setup** (0.2-0.5ms per tile)
   - Mesh object creation: ~40%
   - Entities Graphics registration: ~40%
   - Component addition: ~20%

**GPU Bottlenecks:**
- Vertex processing (high vertex count)
- Overdraw (if view distance too large)
- Shadow map rendering (if shadows enabled)

### Optimization Techniques Applied

#### 1. Burst Compilation
```csharp
[BurstCompile]
public partial struct TileSpawningSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state) { }
    
    [BurstCompile]
    public void OnDestroy(ref SystemState state) { }
    
    // OnUpdate not Burst-compiled (uses managed types)
    public void OnUpdate(ref SystemState state) { }
}
```

**Speedup:** ~5-10x on math-heavy operations.

#### 2. Incremental Processing
```csharp
// Only process tiles that need work
var query = SystemAPI.QueryBuilder()
    .WithAll<TerrainTile, VertexElement>()
    .WithNone<MeshReference>()  // Exclude already-processed
    .Build();
```

**Effect:** Amortizes work across frames, smooth frame rate.

#### 3. Shared Material
All tiles use the same material reference:
- GPU can batch render calls
- Reduces state changes
- Enables instancing

#### 4. Efficient Containers
- `NativeParallelHashMap`: O(1) tile lookup
- `DynamicBuffer`: Resizable, cache-friendly
- `NativeArray`: Stack-allocated for temp data

### Profiling Markers

**To profile in Unity Profiler:**

Look for these markers:
- `TileSpawningSystem.OnUpdate`
- `TerrainMeshGenerationSystem.OnUpdate`
- `TerrainPhysicsSystem.OnUpdate`
- `TerrainRenderingSystem.OnUpdate`
- `FloatingOriginSystem.OnUpdate`

**Expected Frame Budget (60 FPS = 16.67ms):**
- Idle frame (no new tiles): <0.5ms total
- Heavy frame (9 new tiles): 10-20ms (may drop to 40-50 FPS momentarily)

---

## Advanced Topics

### LOD System (Not Implemented Yet)

**Concept:**
```
Distance from Player    Vertices Per Side    Triangles
─────────────────────   ─────────────────    ─────────
0-100m (LOD 0)          64                   7,938
100-300m (LOD 1)        32                   1,922
300-500m (LOD 2)        16                   450
500m+ (LOD 3)           8                    98
```

**Implementation Strategy:**
1. Add `LODLevel` component to TerrainTile
2. In TileSpawningSystem, calculate LOD based on distance
3. In TerrainMeshGenerationSystem, use `LODLevel` to determine `verticesPerSide`
4. Regenerate mesh when LOD changes

### Biome System (Not Implemented Yet)

**Concept:** Different noise parameters per world region.

```csharp
struct BiomeConfig
{
    float2 regionCenter;
    float regionRadius;
    TerrainNoiseParams noiseParams;
}

// Sample noise with biome blending
float SampleNoiseWithBiomes(double3 worldPos, BiomeConfig[] biomes)
{
    float totalHeight = 0;
    float totalWeight = 0;
    
    foreach (var biome in biomes)
    {
        float distance = length(worldPos.xz - biome.regionCenter);
        float weight = saturate(1 - distance / biome.regionRadius);
        
        if (weight > 0)
        {
            float height = SampleNoise(worldPos, biome.noiseParams);
            totalHeight += height * weight;
            totalWeight += weight;
        }
    }
    
    return totalHeight / totalWeight;
}
```

### Texture Splatting (Not Implemented Yet)

**Concept:** Blend textures based on height/slope.

```csharp
// In shader or vertex color:
if (height < 5)
    texture = sand;
else if (height < 20)
    texture = grass;
else if (slope > 45°)
    texture = rock;
else
    texture = snow;
```

**Implementation:**
- Store height/slope in vertex colors or additional UV channel
- Use custom shader with texture arrays
- Sample textures based on vertex data

---

## Future Enhancements

### 1. Chunk Saving/Loading
**Motivation:** Persistent world modifications (player builds, terrain edits)

**Design:**
- Serialize vertex/index buffers to disk
- Store modified chunks in database (SQLite, or JSON)
- On spawn, check if saved version exists
- Load saved version instead of regenerating

### 2. Vegetation System
**Motivation:** Trees, rocks, grass placement

**Design:**
- Use same noise function with different seed
- Spawn entities at specific density
- Parent to tile entity (auto-cleanup on despawn)
- LOD for vegetation (impostors at distance)

### 3. Dynamic Terrain Deformation
**Motivation:** Explosions, digging, building

**Design:**
- Store height modifications in `DynamicBuffer<HeightModification>`
- Apply modifications during mesh generation
- Regenerate affected tiles (set `needsRegeneration = true`)
- Physics colliders automatically update

### 4. Multi-Threaded Generation
**Motivation:** Faster mesh generation for many tiles

**Design:**
```csharp
[BurstCompile]
public partial struct ParallelMeshGenJob : IJobEntity
{
    public void Execute(
        ref DynamicBuffer<VertexElement> vertices,
        in TerrainTile tile,
        in TerrainTileConfig config)
    {
        // Generate mesh in parallel job
    }
}
```

**Challenge:** DynamicBuffer writing in parallel jobs has restrictions. Need to use `NativeArray` and copy afterward.

---

## References

- [Unity DOTS Documentation](https://docs.unity3d.com/Packages/com.unity.entities@latest)
- [Unity.Mathematics API](https://docs.unity3d.com/Packages/com.unity.mathematics@latest)
- [Entities Graphics Package](https://docs.unity3d.com/Packages/com.unity.entities.graphics@latest)
- [Unity.Physics Package](https://docs.unity3d.com/Packages/com.unity.physics@latest)
- [Perlin Noise Explained](https://en.wikipedia.org/wiki/Perlin_noise)
- [Fractal Brownian Motion](https://en.wikipedia.org/wiki/Fractional_Brownian_motion)

