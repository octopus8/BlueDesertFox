# Infinite Terrain System - Visual Flow Guide

**Last Updated:** March 14, 2026  
**Format:** ASCII diagrams and flowcharts

---

## System Overview Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    INFINITE TERRAIN SYSTEM                      │
│                    (Unity DOTS Architecture)                    │
└─────────────────────────────────────────────────────────────────┘
                                 │
                ┌────────────────┴────────────────┐
                │                                 │
        ┌───────▼────────┐              ┌────────▼────────┐
        │  CONFIGURATION │              │  PLAYER ENTITY  │
        │   (Singleton)  │              │  (w/ PlayerTag) │
        └───────┬────────┘              └────────┬────────┘
                │                                 │
                │         ┌───────────────────────┘
                │         │
        ┌───────▼─────────▼────────┐
        │   TILE SPAWNING SYSTEM   │
        │  Tracks player position  │
        │  Creates/destroys tiles  │
        └────────┬─────────────────┘
                 │
         ┌───────┴────────┐
         │                │
    ┌────▼────┐      ┌────▼─────┐
    │  TILE   │      │   TILE   │
    │ Entity  │ ... │  Entity   │
    │  (0,0)  │      │  (2,1)   │
    └────┬────┘      └────┬─────┘
         │                │
         └────────┬───────┘
                  │
    ┌─────────────▼──────────────┐
    │  MESH GENERATION SYSTEM    │
    │  Samples noise             │
    │  Creates vertices/normals  │
    └─────────────┬──────────────┘
                  │
         ┌────────┴─────────┐
         │                  │
    ┌────▼──────┐     ┌─────▼────────┐
    │  PHYSICS  │     │   RENDERING  │
    │  SYSTEM   │     │    SYSTEM    │
    │ Colliders │     │ Unity Meshes │
    └───────────┘     └──────────────┘
                            │
                    ┌───────▼────────┐
                    │  VISIBLE       │
                    │  TERRAIN       │
                    └────────────────┘
```

---

## Tile Lifecycle Flowchart

```
┌─────────────┐
│   PLAYER    │
│   MOVES     │
└──────┬──────┘
       │
       ▼
┌─────────────────────────┐
│ TileSpawningSystem      │
│ Calculates player grid  │
│ position: (x, z)        │
└──────┬──────────────────┘
       │
       ▼
┌─────────────────────────────────┐
│ Is tile at (x, z) in HashMap?   │
└──────┬────────────┬──────────────┘
       │ YES        │ NO
       │            │
       ▼            ▼
┌──────────┐  ┌─────────────────┐
│ SKIP     │  │ CREATE ENTITY   │
│ (exists) │  │ via ECB         │
└──────────┘  └────────┬─────────┘
                       │
                       ▼
              ┌────────────────────┐
              │ Add Components:    │
              │ • TerrainTile      │
              │ • LocalTransform   │
              │ • FloatingOrigin.. │
              │ • 4× Buffers       │
              └────────┬───────────┘
                       │
                       ▼ (Next Frame)
              ┌─────────────────────┐
              │ MeshGenerationSys   │
              │ Queries tiles with  │
              │ meshGenerated=false │
              └────────┬────────────┘
                       │
                       ▼
              ┌─────────────────────┐
              │ Generate Mesh Data: │
              │                     │
              │ FOR each vertex:    │
              │   Sample noise      │
              │   Calculate height  │
              │   Store position    │
              │                     │
              │ FOR each vertex:    │
              │   Calculate normal  │
              │                     │
              │ FOR each quad:      │
              │   Create 2 triangles│
              └────────┬────────────┘
                       │
                       ▼
              ┌─────────────────────┐
              │ Set meshGenerated   │
              │ = true              │
              └────────┬────────────┘
                       │
          ┌────────────┴────────────┐
          │                         │
          ▼                         ▼
┌──────────────────┐      ┌──────────────────┐
│ PhysicsSystem    │      │ RenderingSystem  │
│                  │      │                  │
│ Convert buffers  │      │ Create Unity     │
│ to Unity.Physics │      │ Mesh object      │
│ MeshCollider     │      │                  │
│                  │      │ Register with    │
│ Add PhysicsCol.. │      │ EntitiesGraphics │
└──────────────────┘      └────────┬─────────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │ Add Rendering   │
                          │ Components:     │
                          │ • MaterialMesh..│
                          │ • RenderBounds  │
                          │ • RenderFilter..│
                          └────────┬────────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │  TILE ACTIVE    │
                          │  • Visible      │
                          │  • Collidable   │
                          └────────┬────────┘
                                   │
                          (Player moves away)
                                   │
                                   ▼
                          ┌─────────────────┐
                          │ TileSpawning    │
                          │ Detects tile    │
                          │ > viewDistance  │
                          └────────┬────────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │ DestroyEntity   │
                          │ via ECB         │
                          └────────┬────────┘
                                   │
                                   ▼
                          ┌─────────────────┐
                          │ Cleanup:        │
                          │ • Dispose mesh  │
                          │ • Dispose col.. │
                          │ • Remove from   │
                          │   HashMap       │
                          └─────────────────┘
```

---

## Frame-by-Frame Execution

```
FRAME 0 (Player spawns at origin)
├─ SimulationSystemGroup
│  └─ TileSpawningSystem
│     ├─ Player at (0, 0, 0) → grid (0, 0)
│     ├─ Create tiles: (-1,-1), (-1,0), (-1,1)...
│     ├─ Total: 9 tiles in 3×3 grid
│     └─ Add to HashMap
│
└─ Output: 9 tile entities with empty buffers

FRAME 1 (Entities exist)
├─ SimulationSystemGroup
│  ├─ TileSpawningSystem: No new tiles needed
│  │
│  └─ TerrainMeshGenerationSystem
│     ├─ Query: TerrainTile + !meshGenerated
│     ├─ Found: 9 tiles
│     ├─ FOR each tile:
│     │  ├─ Generate 1024 vertices (32×32)
│     │  ├─ Calculate 1024 normals
│     │  ├─ Generate 1024 UVs
│     │  ├─ Generate 5766 indices
│     │  └─ Set meshGenerated=true
│     └─ Time: ~9ms (all tiles)
│
└─ Output: 9 tiles with populated buffers

FRAME 2 (Mesh data ready)
├─ SimulationSystemGroup
│  └─ TerrainPhysicsSystem
│     ├─ Query: TerrainTile + !PhysicsCollider
│     ├─ Found: 9 tiles
│     ├─ FOR each tile:
│     │  ├─ Convert buffers to NativeArray
│     │  ├─ Create Unity.Physics.MeshCollider
│     │  └─ Add PhysicsCollider component
│     └─ Time: ~18ms (all tiles)
│
├─ PresentationSystemGroup
│  └─ TerrainRenderingSystem
│     ├─ Query: TerrainTile + !MeshReference
│     ├─ Found: 9 tiles
│     ├─ FOR each tile:
│     │  ├─ Create Unity Mesh object
│     │  ├─ Copy buffer data to mesh
│     │  ├─ mesh.RecalculateBounds()
│     │  ├─ Register with EntitiesGraphicsSystem
│     │  ├─ Add rendering components
│     │  └─ Add MeshReference
│     └─ Time: ~5ms (all tiles)
│
└─ Output: 9 tiles VISIBLE and COLLIDABLE

FRAME 3-49 (Steady state)
├─ TileSpawningSystem: No changes
├─ MeshGenerationSystem: No tiles to process
├─ PhysicsSystem: No tiles to process
└─ RenderingSystem: No tiles to process
    Time per frame: <0.5ms

FRAME 50 (Player moves to (150, 0, 0))
├─ Player position → grid (1, 0)
│
├─ TileSpawningSystem
│  ├─ Player moved to new grid cell
│  ├─ Spawn tiles: (2,-1), (2,0), (2,1) [3 new]
│  ├─ Despawn tiles: (-1,-1), (-1,0), (-1,1) [3 old]
│  └─ Active: 9 tiles (6 existing + 3 new)
│
└─ Next 2 frames: New tiles go through pipeline

FRAME 200 (Player at (2500, 0, 0))
├─ TransformSystemGroup
│  └─ FloatingOriginSystem
│     ├─ Distance from origin: 2500m
│     ├─ Exceeds threshold: 2000m
│     ├─ Trigger world shift!
│     │
│     ├─ Update WorldOriginOffset:
│     │  ├─ Before: (0, 0, 0)
│     │  └─ After: (2500, 0, 0)
│     │
│     └─ ShiftWorldOriginJob (parallel)
│        ├─ FOR each entity with FloatingOriginEnabled:
│        │  └─ Position -= (2500, 0, 0)
│        │
│        ├─ Player: (2500, 0, 0) → (0, 0, 0)
│        ├─ Tile(0,0): (0, 0, 0) → (-2500, 0, 0)
│        └─ All entities shifted!
│
└─ Result: Player back at origin, terrain consistent
```

---

## Data Flow Diagram

```
┌──────────────┐
│ TerrainConfig│ ◄───── TerrainConfigAuthoring (Scene GameObject)
│  (Singleton) │
└──────┬───────┘
       │ Read by ↓
       │
┌──────▼─────────────────────────┐
│   TileSpawningSystem           │
│                                │
│ Input:                         │
│  • Player position (float3)    │
│  • TerrainTileConfig           │
│                                │
│ Process:                       │
│  • Calculate grid coordinate   │
│  • Check active tiles HashMap  │
│  • Determine spawn/despawn     │
│                                │
│ Output:                        │
│  • New tile entities (ECB)     │
│  • Destroyed old entities      │
└──────┬─────────────────────────┘
       │
       │ Tile entities →
       │
┌──────▼─────────────────────────┐
│  TerrainMeshGenerationSystem   │
│                                │
│ Input:                         │
│  • TerrainTile.gridCoordinate  │
│  • WorldOriginOffset           │
│  • TerrainTileConfig           │
│                                │
│ Process:                       │
│  • Calculate true world pos    │
│  • Sample multi-octave noise   │
│  • Generate vertex positions   │
│  • Calculate normals           │
│  • Generate triangle indices   │
│                                │
│ Output:                        │
│  • Populated buffers:          │
│    ├─ VertexElement[]          │
│    ├─ NormalElement[]          │
│    ├─ UVElement[]              │
│    └─ IndexElement[]           │
│  • meshGenerated = true        │
└──────┬─────────────────────────┘
       │
       ├───────────────┬───────────────┐
       │               │               │
┌──────▼────────┐  ┌──▼──────────┐ ┌──▼─────────────┐
│  TerrainPhysics│  │  TerrainRender│ │ FloatingOrigin │
│  System       │  │  System       │ │ System         │
│               │  │               │ │                │
│ Convert to    │  │ Convert to    │ │ Check player   │
│ MeshCollider  │  │ Unity Mesh    │ │ distance       │
│               │  │               │ │                │
│ Add:          │  │ Add:          │ │ If > threshold:│
│ PhysicsCol..  │  │ MeshRef..     │ │  Shift world   │
│               │  │ Material..    │ │  Update offset │
│               │  │ RenderBounds  │ │                │
└───────────────┘  └───────┬───────┘ └────────────────┘
                           │
                           ▼
                   ┌───────────────┐
                   │  ENTITIES     │
                   │  GRAPHICS     │
                   │  (Unity GPU)  │
                   └───────┬───────┘
                           │
                           ▼
                   ┌───────────────┐
                   │  RENDERED     │
                   │  TERRAIN      │
                   └───────────────┘
```

---

## Component Dependency Graph

```
TerrainConfigAuthoring (MonoBehaviour)
    │
    │ Bakes to ↓
    │
    ├──► TerrainTileConfig (Singleton)
    │      │
    │      │ Used by ↓
    │      │
    │      ├──► TileSpawningSystem
    │      ├──► TerrainMeshGenerationSystem
    │      └──► TerrainPhysicsSystem
    │
    ├──► FloatingOriginConfig (Singleton)
    │      │
    │      │ Used by ↓
    │      │
    │      └──► FloatingOriginSystem
    │
    └──► WorldOriginOffset (Singleton)
           │
           │ Read by ↓
           │
           ├──► TerrainMeshGenerationSystem
           │
           │ Modified by ↓
           │
           └──► FloatingOriginSystem


PlayerTagAuthoring (MonoBehaviour)
    │
    │ Bakes to ↓
    │
    └──► PlayerTag (on entity)
           │
           │ Queried by ↓
           │
           ├──► TileSpawningSystem
           └──► FloatingOriginSystem


FloatingOriginEnabledAuthoring (MonoBehaviour)
    │
    │ Bakes to ↓
    │
    └──► FloatingOriginEnabled (on entity)
           │
           │ Filtered by ↓
           │
           └──► ShiftWorldOriginJob
```

---

## Memory Layout Diagram

```
TERRAIN TILE ENTITY
┌─────────────────────────────────────────────────────────┐
│ Entity ID: 1234                                         │
├─────────────────────────────────────────────────────────┤
│ COMPONENTS (Inline data)                                │
├─────────────────────────────────────────────────────────┤
│ TerrainTile                                             │
│  ├─ gridCoordinate: (2, -1)        [8 bytes]           │
│  ├─ meshGenerated: true            [1 byte]            │
│  └─ needsRegeneration: false       [1 byte]            │
│                                                          │
│ LocalTransform                                          │
│  ├─ Position: (200, 0, -100)       [12 bytes]          │
│  ├─ Rotation: (0,0,0,1)            [16 bytes]          │
│  └─ Scale: 1                       [4 bytes]           │
│                                                          │
│ LocalToWorld                                            │
│  └─ Value: 4×4 matrix              [64 bytes]          │
│                                                          │
│ FloatingOriginEnabled (empty tag)  [0 bytes]           │
│                                                          │
│ PhysicsCollider                                         │
│  └─ Value: BlobAssetReference      [4 bytes]           │
│     └─> Points to BlobAsset in shared memory (~30 KB)  │
│                                                          │
│ MaterialMeshInfo                                        │
│  ├─ Material: 0                    [4 bytes]           │
│  └─ Mesh: 0                        [4 bytes]           │
│                                                          │
│ RenderBounds                                            │
│  └─ Value: AABB                    [24 bytes]          │
│                                                          │
│ WorldRenderBounds                                       │
│  └─ Value: AABB                    [24 bytes]          │
├─────────────────────────────────────────────────────────┤
│ DYNAMIC BUFFERS (Separate allocations)                 │
├─────────────────────────────────────────────────────────┤
│ VertexElement[]                    [12 KB]             │
│  ├─ [0] = (0.0, 5.2, 0.0)                              │
│  ├─ [1] = (3.2, 4.8, 0.0)                              │
│  └─ ... [1024 elements]                                │
│                                                          │
│ NormalElement[]                    [12 KB]             │
│  ├─ [0] = (0.0, 1.0, 0.0)                              │
│  └─ ... [1024 elements]                                │
│                                                          │
│ UVElement[]                        [8 KB]              │
│  ├─ [0] = (0.0, 0.0)                                   │
│  └─ ... [1024 elements]                                │
│                                                          │
│ IndexElement[]                     [23 KB]             │
│  ├─ [0] = 0, [1] = 32, [2] = 1                         │
│  └─ ... [5766 elements]                                │
├─────────────────────────────────────────────────────────┤
│ MANAGED COMPONENTS (Managed heap)                      │
├─────────────────────────────────────────────────────────┤
│ MeshReference                      [4 bytes ref]       │
│  └─ mesh: UnityEngine.Mesh         [~50 KB heap]       │
└─────────────────────────────────────────────────────────┘

TOTAL PER TILE: ~135 KB
```

---

## Noise Generation Flow

```
World Position (double precision)
    (2500.5, 0.0, 1234.7) + accumulatedOffset
                │
                ▼
        ┌───────────────┐
        │ Octave Loop   │
        │ (4 iterations)│
        └───────┬───────┘
                │
    ┌───────────┴───────────┐
    │                       │
OCTAVE 0                OCTAVE 1
freq = 0.01             freq = 0.02
amp = 20.0              amp = 10.0
    │                       │
    ▼                       ▼
┌─────────┐             ┌─────────┐
│ noise() │             │ noise() │
└────┬────┘             └────┬────┘
     │                       │
   value                   value
   [-1,1]                  [-1,1]
     │                       │
     ▼                       ▼
  × 20.0                  × 10.0
  = ±20.0                 = ±10.0
     │                       │
     └───────────┬───────────┘
                 │
                 ▼ (continue for octaves 2,3...)
         ┌───────────────┐
         │  Sum values   │
         │  total = Σ    │
         │  maxVal = Σamp│
         └───────┬───────┘
                 │
                 ▼
         ┌───────────────┐
         │  Normalize:   │
         │  total/maxVal │
         │  × amplitude  │
         └───────┬───────┘
                 │
                 ▼
         ┌───────────────┐
         │ Final Height  │
         │  (5.2 meters) │
         └───────────────┘
```

---

## Floating Origin Shift Diagram

```
BEFORE SHIFT (Player walked to x=2500)
┌───────────────────────────────────────────────────────┐
│ World Space                                           │
│                                                        │
│ Origin                  Player        Tile Grid       │
│   ↓                       ↓             ↓             │
│   (0,0,0)             (2500,0,0)    (2500,0,0)        │
│   ·                       ●             ▓▓▓           │
│   │                       │             ▓▓▓           │
│   └───────2500m───────────┘             ↑             │
│                                      Grid (25,0)      │
│                                                        │
│ accumulatedOffset = (0, 0, 0)                         │
│ Precision: Good at origin, POOR at player (jitter)    │
└───────────────────────────────────────────────────────┘

DURING SHIFT (FloatingOriginSystem executes)
┌───────────────────────────────────────────────────────┐
│ 1. Calculate shift: offset = playerPosition           │
│    offset = (2500, 0, 0)                              │
│                                                        │
│ 2. Update accumulated offset:                         │
│    (0,0,0) + (2500,0,0) = (2500, 0, 0)                │
│                                                        │
│ 3. Schedule parallel job:                             │
│    FOR each FloatingOriginEnabled entity:             │
│       entity.Position -= offset                       │
└───────────────────────────────────────────────────────┘

AFTER SHIFT (All entities moved)
┌───────────────────────────────────────────────────────┐
│ World Space                                           │
│                                                        │
│ Tile Grid    Player      Origin                       │
│   ↓            ↓           ↓                          │
│  (0,0,0)    (0,0,0)     (0,0,0)                       │
│   ▓▓▓         ●           ·                           │
│   ▓▓▓         │           │                           │
│    ↑          │           │                           │
│ Grid (25,0)   └───────────┘                           │
│ in entity     Same position!                          │
│ space                                                  │
│                                                        │
│ accumulatedOffset = (2500, 0, 0)                      │
│ True world position = entity pos + accumulated        │
│ Player true pos: (0,0,0) + (2500,0,0) = (2500,0,0)    │
│ Precision: EXCELLENT everywhere (all near origin)     │
└───────────────────────────────────────────────────────┘

TERRAIN GENERATION CONSISTENCY
┌───────────────────────────────────────────────────────┐
│ When generating tile at grid (25, 0):                 │
│                                                        │
│ Entity space position:   (0, 0, 0)                    │
│ + Accumulated offset:    (2500, 0, 0)                 │
│ ─────────────────────────────────────                 │
│ = True world position:   (2500, 0, 0)                 │
│                                                        │
│ Noise sampled at (2500, 0, 0) → Same result always!   │
│                                                        │
│ Result: Terrain looks identical before/after shift    │
└───────────────────────────────────────────────────────┘
```

---

## Tile Spawning Pattern

```
View Distance = 300m, Tile Size = 100m
View Distance in Tiles = ceil(300/100) = 3

Player at grid (0, 0):
┌───────────────────────────────────────────┐
│             TILE SPAWNING                 │
│                                           │
│    Player Grid: (0, 0)                    │
│    Radius: 3 tiles                        │
│                                           │
│  ╔═══╦═══╦═══╦═══╦═══╦═══╦═══╗           │
│  ║   ║   ║ X ║ X ║ X ║   ║   ║  X = Too  │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣    far   │
│  ║   ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║   ║           │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣  ✓ = Active│
│  ║ X ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ X ║           │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣  P = Player│
│  ║ X ║ ✓ ║ ✓ ║ P ║ ✓ ║ ✓ ║ X ║           │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣           │
│  ║ X ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ X ║           │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣           │
│  ║   ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║   ║           │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣           │
│  ║   ║   ║ X ║ X ║ X ║   ║   ║           │
│  ╚═══╩═══╩═══╩═══╩═══╩═══╩═══╝           │
│                                           │
│  Active tiles: 21                         │
│  (Circular pattern, not square)           │
└───────────────────────────────────────────┘

Player moves to grid (1, 0):
┌───────────────────────────────────────────┐
│             TILE CHANGES                  │
│                                           │
│  ╔═══╦═══╦═══╦═══╦═══╦═══╦═══╦═══╗       │
│  ║   ║   ║ - ║ - ║ - ║ + ║ + ║   ║  - = Despawn│
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╬═══╣  + = Spawn  │
│  ║   ║ - ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ + ║   ║  ✓ = Remain │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╬═══╣            │
│  ║   ║ - ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ + ║   ║            │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╬═══╣            │
│  ║   ║ - ║ ✓ ║ ✓ ║P→║ ✓ ║ + ║   ║            │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╬═══╣            │
│  ║   ║ - ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ + ║   ║            │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╬═══╣            │
│  ║   ║ - ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ + ║   ║            │
│  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╬═══╣            │
│  ║   ║   ║ - ║ - ║ - ║ + ║ + ║   ║            │
│  ╚═══╩═══╩═══╩═══╩═══╩═══╩═══╩═══╝            │
│                                           │
│  Despawned: 6 tiles                       │
│  Spawned: 6 tiles                         │
│  Active: 21 tiles (same total)            │
└───────────────────────────────────────────┘
```

---

## Mesh Vertex Grid

```
32×32 Vertex Grid (verticesPerSide = 32)
Tile Size = 100m
Step Size = 100 / 31 = 3.226m

Z=31: [992]──[993]──[994]─ ... ─[1023]   ← Top edge
      │ \  │ \  │ \       │ \  │
      │  \ │  \ │  \      │  \ │
      │   \│   \│   \     │   \│
Z=30: [960]──[961]──[962]─ ... ─[991]
      │ \  │ \  │ \       │ \  │
      :    :    :           :    :
      
Z=1:  [32]───[33]───[34]── ... ─[63]
      │ \  │ \  │ \       │ \  │
      │  \ │  \ │  \      │  \ │
      │   \│   \│   \     │   \│
Z=0:  [0]────[1]────[2]─── ... ─[31]      ← Bottom edge
      X=0   X=1   X=2        X=31
      └──────────────────────────┘
      Left edge            Right edge

Vertex[0]:    Position = (0, h0, 0)         UV = (0.0, 0.0)
Vertex[1]:    Position = (3.226, h1, 0)     UV = (0.032, 0.0)
Vertex[31]:   Position = (100, h31, 0)      UV = (1.0, 0.0)
Vertex[32]:   Position = (0, h32, 3.226)    UV = (0.0, 0.032)
Vertex[1023]: Position = (100, h1023, 100)  UV = (1.0, 1.0)

Triangle Formation:
Quad at (0,0):
   [32]─────[33]
    │ \      │
    │  \     │        Triangle 1: [0, 32, 1]
    │   \    │        Triangle 2: [1, 32, 33]
    │    \   │
   [0]──────[1]
```

---

## System Update Timeline

```
UNITY FRAME START
    │
    ├─── FixedUpdate (Physics timestep)
    │
    ├─── SimulationSystemGroup ────────────────┐
    │    │                                     │ START
    │    ├─ TileSpawningSystem                │
    │    │  └─ Time: 0.1-2ms                  │ These run
    │    │                                     │ in sequence
    │    ├─ TerrainMeshGenerationSystem       │
    │    │  └─ Time: 0-10ms (depends on tiles)│
    │    │                                     │
    │    └─ TerrainPhysicsSystem              │
    │       └─ Time: 0-20ms (depends on tiles)│ END
    │                                          │
    ├─── TransformSystemGroup ─────────────────┘
    │    │
    │    ├─ LocalToWorldSystem
    │    │  └─ Updates all transform matrices
    │    │
    │    └─ FloatingOriginSystem
    │       └─ Time: 0.05-0.5ms
    │
    ├─── PresentationSystemGroup
    │    │
    │    └─ TerrainRenderingSystem
    │       └─ Time: 0-5ms (depends on tiles)
    │
    ├─── Render (Unity GPU)
    │    ├─ Culling
    │    ├─ Shadow maps
    │    └─ Main camera render
    │
    └─── FRAME END

Timing Example (typical frame):
├─ TileSpawning:      0.1ms
├─ MeshGeneration:    0.0ms (no new tiles)
├─ Physics:           0.0ms (no new tiles)
├─ FloatingOrigin:    0.05ms
├─ Rendering:         0.0ms (no new tiles)
├─ Other Unity:       3ms
├─ GPU Rendering:     8ms
└─ Total Frame:       11.15ms (89 FPS) ✓

Timing Example (heavy spawning frame):
├─ TileSpawning:      1.5ms (9 new tiles)
├─ MeshGeneration:    9.0ms (9 tiles × 1ms)
├─ Physics:           18.0ms (9 tiles × 2ms)
├─ FloatingOrigin:    0.05ms
├─ Rendering:         4.5ms (9 tiles × 0.5ms)
├─ Other Unity:       3ms
├─ GPU Rendering:     12ms
└─ Total Frame:       48.05ms (20 FPS) ⚠️
    Spread across 3-5 frames: smooth 60 FPS ✓
```

---

## HashMap Lookup Visualization

```
NativeParallelHashMap<int2, Entity> _activeTiles
┌─────────────────────────────────────────────────┐
│ Bucket Array (capacity: 256)                   │
├─────────────────────────────────────────────────┤
│ [0]   → null                                    │
│ [1]   → null                                    │
│ [2]   → null                                    │
│ [3]   → {(-1,-1), Entity:123} → null            │
│ [4]   → null                                    │
│ [5]   → {(0,0), Entity:124} → {(3,1), Ent:130} │
│ [6]   → null                                    │
│ ...                                             │
│ [47]  → {(1,0), Entity:125} → null              │
│ ...                                             │
│ [255] → null                                    │
└─────────────────────────────────────────────────┘

Lookup: _activeTiles.ContainsKey((1,0))
    │
    ├─ Hash (1,0) → 47
    ├─ Check bucket[47]
    ├─ Compare: (1,0) == (1,0) ✓
    └─ Return true

Lookup time: O(1) - constant time
Insert time: O(1) - constant time
Remove time: O(1) - constant time
Memory: ~16 bytes per entry + overhead
```

---

## Normal Calculation Diagram

```
Vertex Grid (showing vertex at (2,2) and neighbors)

    (1,3)     (2,3)     (3,3)
      ●─────────●─────────●
      │\   2   /│\   3   /│
      │ \     / │ \     / │
      │  \   /  │  \   /  │
      │ 1 \ / 2 │ 3 \ / 4 │
      │    ●    │    ●    │    ← Center vertex (2,2)
      │ 4 / \ 1 │ 2 / \ 3 │
      │  /   \  │  /   \  │
      │ /     \ │ /     \ │
      │/   3   \│/   4   \│
      ●─────────●─────────●
    (1,1)     (2,1)     (3,1)

Normal Calculation for Center Vertex (2,2):
├─ Face 1 (top-right):
│  ├─ V0 = (2,2), V1 = (3,2), V2 = (2,3)
│  ├─ Tangent1 = V1-V0 = (1,h,0)
│  ├─ Tangent2 = V2-V0 = (0,h,1)
│  └─ Normal1 = cross(T1, T2) = normalize(h,-1,h)
│
├─ Face 2 (top-left):
│  ├─ V0 = (2,2), V1 = (2,3), V2 = (1,2)
│  └─ Normal2 = ...
│
├─ Face 3 (bottom-left):
│  └─ Normal3 = ...
│
└─ Face 4 (bottom-right):
   └─ Normal4 = ...

Final Normal = normalize(Normal1 + Normal2 + Normal3 + Normal4)

Result: Smooth average of adjacent face normals
        Creates smooth lighting across surface
```

---

## EntityCommandBuffer Flow

```
TileSpawningSystem.OnUpdate()
    │
    ├─ Create ECB
    │     var ecb = new EntityCommandBuffer(Allocator.Temp);
    │
    ├─ Queue Operations
    │     foreach (tile in tilesToSpawn)
    │         ecb.CreateEntity()
    │         ecb.AddComponent(...)
    │         ecb.AddBuffer(...)
    │
    ├─ Playback (Apply changes)
    │     ecb.Playback(state.EntityManager)
    │     │
    │     └─► EntityManager performs operations:
    │           ├─ Allocate entity memory
    │           ├─ Add components to chunks
    │           ├─ Create buffer allocations
    │           └─ Update archetypes
    │
    ├─ Dispose ECB
    │     ecb.Dispose()
    │
    └─ Query New Entities
          var newEntities = query.ToEntityArray()
          foreach (entity in newEntities)
              _activeTiles.Add(gridCoord, entity)

Why ECB?
├─ Can't modify structure during iteration
├─ Batches operations for efficiency
├─ Thread-safe recording (can schedule jobs)
└─ Defers actual changes to safe point
```

---

## Physics Collider Structure

```
Entity with PhysicsCollider
    │
    ├─ PhysicsCollider Component (4 bytes)
    │  └─ Value: BlobAssetReference<Collider>
    │            │
    │            └─► BlobAsset in Shared Memory
    │                ┌─────────────────────────────┐
    │                │ MeshCollider BlobAsset      │
    │                ├─────────────────────────────┤
    │                │ Vertices (compressed)       │
    │                │  └─ float3[1024]            │
    │                │                             │
    │                │ Triangles                   │
    │                │  └─ int3[1922]              │
    │                │                             │
    │                │ BVH Tree (for fast queries) │
    │                │  ├─ Root Node               │
    │                │  ├─── Child Nodes           │
    │                │  └───── Leaf Nodes          │
    │                │                             │
    │                │ Collision Filter            │
    │                │  ├─ BelongsTo: Layer 0      │
    │                │  ├─ CollidesWith: All       │
    │                │  └─ GroupIndex: 0           │
    │                │                             │
    │                │ Material                    │
    │                │  ├─ Friction: 0.5           │
    │                │  └─ Restitution: 0.0        │
    │                └─────────────────────────────┘
    │
    └─ PhysicsWorldIndex Component
       └─ Value: 0 (default physics world)

Collision Query:
Player → Physics System → BVH Tree → Find intersecting triangles → Resolve collision
```

---

## Rendering Component Flow

```
TerrainRenderingSystem.CreateAndAssignMesh()
    │
    ├─ 1. Create Unity Mesh
    │     mesh = new Mesh()
    │     mesh.vertices = vertexArray
    │     mesh.normals = normalArray
    │     mesh.uv = uvArray
    │     mesh.triangles = indexArray
    │     mesh.RecalculateBounds()
    │
    ├─ 2. Register with EntitiesGraphicsSystem
    │     meshID = entitiesGraphicsSystem.RegisterMesh(mesh)
    │     matID = entitiesGraphicsSystem.RegisterMaterial(material)
    │     │
    │     └─► Internal Registries:
    │          ├─ _registeredMeshes[meshID] = mesh
    │          └─ _registeredMaterials[matID] = material
    │
    ├─ 3. Create Render Description
    │     renderMeshDescription = new RenderMeshDescription(
    │         shadowCastingMode: On,
    │         receiveShadows: true,
    │         layer: 0,
    │         renderingLayerMask: 1
    │     )
    │
    ├─ 4. Create Render Arrays
    │     renderMeshArray = new RenderMeshArray(
    │         materials: [material],
    │         meshes: [mesh]
    │     )
    │     materialMeshInfo = FromIndices(0, 0)
    │
    ├─ 5. Add Components
    │     RenderMeshUtility.AddComponents(
    │         entity, EntityManager,
    │         renderMeshDescription,
    │         renderMeshArray,
    │         materialMeshInfo
    │     )
    │     │
    │     └─► Adds to entity:
    │          ├─ MaterialMeshInfo (material/mesh IDs)
    │          ├─ RenderBounds (from mesh.bounds)
    │          ├─ RenderFilterSettings (layer, shadows)
    │          └─ (WorldRenderBounds calculated by Transform)
    │
    └─ 6. Store Reference
          EntityManager.AddComponentData(
              entity,
              new MeshReference { mesh = mesh }
          )

GPU Rendering:
    EntitiesGraphicsSystem →
    Culling (RenderBounds vs Frustum) →
    Batching (by Material) →
    GPU Draw Calls
```

---

## Error Diagnostic Flow

```
┌──────────────────────┐
│ TERRAIN NOT VISIBLE  │
└──────────┬───────────┘
           │
    ┌──────▼───────┐
    │ Check Console│
    └──────┬───────┘
           │
    ┌──────▼──────────────────────────┐
    │ Any errors?                     │
    └──┬────────────┬─────────────────┘
       │ YES        │ NO
       │            │
       ▼            ▼
┌──────────────┐  ┌───────────────────┐
│ Fix errors   │  │ Check Systems     │
│ Recompile    │  │ Window→DOTS→Sys   │
└──────────────┘  └────┬──────────────┘
                       │
                ┌──────▼───────────────┐
                │ Systems running?     │
                └──┬────────────┬──────┘
                   │ NO         │ YES
                   │            │
                   ▼            ▼
            ┌──────────────┐  ┌──────────────┐
            │Check require-│  │Check console │
            │ments met:    │  │for logs      │
            │• PlayerTag   │  └──┬───────────┘
            │• Config      │     │
            └──────────────┘     │
                            ┌────▼──────────┐
                            │ Tiles spawning?│
                            └──┬────────┬───┘
                               │ NO     │ YES
                               │        │
                               ▼        ▼
                        ┌──────────┐  ┌──────────┐
                        │Check     │  │Check mesh│
                        │player    │  │generation│
                        │position  │  └────┬─────┘
                        └──────────┘       │
                                      ┌────▼──────┐
                                      │Mesh data  │
                                      │populated? │
                                      └──┬────┬───┘
                                         │ NO │ YES
                                         │    │
                                         ▼    ▼
                                    ┌─────┐ ┌──────┐
                                    │Wait │ │Check │
                                    │next │ │rende-│
                                    │frame│ │ring  │
                                    └─────┘ └───┬──┘
                                                │
                                           ┌────▼─────┐
                                           │Material  │
                                           │valid?    │
                                           │Shader OK?│
                                           └────┬─────┘
                                                │
                                        ┌───────▼────────┐
                                        │ CHECK CAMERA   │
                                        │ • Position OK? │
                                        │ • Culling mask?│
                                        │ • Frustum?     │
                                        └────────────────┘
```

---

## Configuration Impact Diagram

```
VERTICES PER SIDE (Resolution)
    16          32          64          128
    ↓           ↓           ↓           ↓
   256        1,024       4,096      16,384  vertices
   450        1,922       7,938      32,258  triangles
   ~20KB      ~55KB      ~200KB     ~800KB  memory/tile
   0.3ms      0.8ms      3.0ms      12ms    generation time
    ▓          ▓▓         ▓▓▓        ▓▓▓▓   visual quality
    
Recommendation: 16-32 for VR, 32-64 for desktop


VIEW DISTANCE (Streaming)
   200m        400m        800m        1600m
    ↓           ↓           ↓           ↓
   ~12         ~50         ~200        ~800   active tiles
   ~1.5MB      ~6MB        ~24MB       ~96MB  memory
   Frequent    Balanced    Rare        Very rare  tile changes
    ▓          ▓▓▓         ▓▓▓▓        ▓▓▓▓▓  immersion
    
Recommendation: 200-300m for VR, 400-600m for desktop


NOISE OCTAVES (Detail)
    1           2           4           8
    ↓           ↓           ↓           ↓
  Simple      Smooth      Natural     Chaotic
   0.1ms      0.2ms       0.4ms       0.8ms   per tile
    ▓          ▓▓         ▓▓▓▓        ▓▓▓▓▓  visual detail
    
Recommendation: 2-3 for performance, 4-6 for quality


TILE SIZE (Granularity)
   50m         100m        200m        400m
    ↓           ↓           ↓           ↓
  4× tiles    Baseline    0.25× tiles  0.0625× tiles
  Fine LOD    Balanced    Coarse LOD   Very coarse
  More        Moderate    Less         Minimal
  loading     loading     loading      loading
    
Recommendation: 100m for most cases, 50m for high detail
```

---

## Query Performance Comparison

```
APPROACH 1: Iterate All Entities
────────────────────────────────
Entities.ForEach((Entity e, in TerrainTile tile) => { ... })
    │
    ├─ Iterates: ALL tile entities
    ├─ Checks: meshGenerated flag inside loop
    ├─ Processed: 50 entities
    ├─ Work done: 5 entities
    └─ Efficiency: 10% (wasteful)
    
    Time: 0.5ms (45 entities skipped)


APPROACH 2: Query with Exclusion (USED)
───────────────────────────────────────
query = WithAll<TerrainTile>().WithNone<MeshReference>()
    │
    ├─ Iterates: Only matching entities
    ├─ Checks: None (pre-filtered)
    ├─ Processed: 5 entities
    ├─ Work done: 5 entities
    └─ Efficiency: 100% (optimal)
    
    Time: 0.05ms (no wasted iteration)

SPEEDUP: 10× faster
```

---

## Circular vs Square View Distance

```
SQUARE VIEW DISTANCE (Not Used)
────────────────────────────────
viewDistanceInTiles = 3
Active area = (2*3+1)² = 49 tiles

  ╔═══╦═══╦═══╦═══╦═══╦═══╦═══╗
  ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ ✓ ║ ✓ ║ ✓ ║ P ║ ✓ ║ ✓ ║ ✓ ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║
  ╚═══╩═══╩═══╩═══╩═══╩═══╩═══╝

Corners are visible but far from player (wasteful)


CIRCULAR VIEW DISTANCE (Used)
─────────────────────────────
viewDistance = 300m, tileSize = 100m
Active area = π * 3² ≈ 28 tiles

  ╔═══╦═══╦═══╦═══╦═══╦═══╦═══╗
  ║   ║   ║ X ║ X ║ X ║   ║   ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║   ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║   ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ X ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ X ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ X ║ ✓ ║ ✓ ║ P ║ ✓ ║ ✓ ║ X ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║ X ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ X ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║   ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║ ✓ ║   ║
  ╠═══╬═══╬═══╬═══╬═══╬═══╬═══╣
  ║   ║   ║ X ║ X ║ X ║   ║   ║
  ╚═══╩═══╩═══╩═══╩═══╩═══╩═══╝

  X = Outside view distance (not spawned)
  ✓ = Inside view distance (active)

SAVINGS: 49 → 28 tiles (-43% memory, -43% generation time)
```

---

## Precision Visualization

```
FLOAT PRECISION DEGRADATION
───────────────────────────

Distance from Origin    Precision       Effect in VR
────────────────────────────────────────────────────────
        0-10m           0.000001m       Perfect
       10-100m          0.00001m        Perfect
      100-1,000m        0.0001m         Imperceptible
    1,000-10,000m       0.001m          Slight jitter
   10,000-100,000m      0.01m           Noticeable jitter ⚠️
  100,000-1,000,000m    0.1m            Severe artifacts ❌
    >1,000,000m          1m+             Unusable ❌


DOUBLE PRECISION (for accumulated offset)
──────────────────────────────────────────

Distance                Precision       Effect
────────────────────────────────────────────────────────
        0-10m           10⁻¹⁵m          Perfect
       10-100m          10⁻¹⁴m          Perfect
      100-1,000m        10⁻¹³m          Perfect
    1,000-10,000m       10⁻¹²m          Perfect
   10,000-100,000m      10⁻¹¹m          Perfect
  100,000-1,000,000m    10⁻¹⁰m          Perfect
  ... up to 10¹⁵m       Depends         Still excellent ✓


HYBRID APPROACH (Implemented)
──────────────────────────────

Entity Positions:     float3  (always near origin)
Accumulated Offset:   double3 (tracks true position)
True World Position:  entity + offset

Result: Perfect precision at ANY distance ✓
```

---

## Memory Map (Typical Configuration)

```
32×32 vertices, 28 active tiles, 400m view distance

┌─────────────────────────────────────────────────┐
│ TOTAL MEMORY: ~4.2 MB                           │
├─────────────────────────────────────────────────┤
│                                                  │
│ ECS Entity Storage           ~500 KB             │
│ ├─ Entities (28 tiles)      ~10 KB              │
│ ├─ Components               ~50 KB              │
│ └─ Archetypes               ~440 KB             │
│                                                  │
│ Mesh Buffers (ECS)           ~1.54 MB            │
│ ├─ VertexElement            ~336 KB (28×12KB)   │
│ ├─ NormalElement            ~336 KB              │
│ ├─ UVElement                ~224 KB              │
│ └─ IndexElement             ~644 KB              │
│                                                  │
│ Unity Meshes (Managed)       ~1.4 MB             │
│ └─ 28 meshes × 50 KB        ~1,400 KB           │
│                                                  │
│ Physics Colliders (BlobAsset) ~840 KB            │
│ └─ 28 colliders × 30 KB     ~840 KB             │
│                                                  │
│ Systems & Overhead           ~100 KB             │
│ ├─ NativeParallelHashMap    ~10 KB              │
│ ├─ System state             ~50 KB              │
│ └─ Temp allocations         ~40 KB              │
│                                                  │
└─────────────────────────────────────────────────┘

Scaling:
├─ 2× view distance → 4× tiles → 4× memory
├─ 2× vertices/side → 4× data per tile → 4× memory
└─ Total memory = tiles × bytesPerTile
```

---

## Threading Diagram

```
┌─────────────────────────────────────────────────┐
│ MAIN THREAD                                     │
├─────────────────────────────────────────────────┤
│                                                  │
│ TileSpawningSystem.OnUpdate()                   │
│  ├─ Create ECB                [main]            │
│  ├─ Queue entity creation     [main]            │
│  ├─ Playback ECB              [main]            │
│  └─ Update HashMap            [main]            │
│                                                  │
│ TerrainMeshGenerationSystem.OnUpdate()          │
│  ├─ Foreach tile              [main]            │
│  ├─── SampleNoise()           [Burst ✓]         │
│  ├─── CalculateNormal()       [Burst ✓]         │
│  └─ Write to buffers          [main]            │
│                                                  │
│ TerrainPhysicsSystem.OnUpdate()                 │
│  └─ Create colliders          [main]            │
│                                                  │
│ TerrainRenderingSystem.OnUpdate()               │
│  └─ Create Unity Meshes       [main]            │
│                                                  │
└─────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────┐
│ WORKER THREADS (Job System)                     │
├─────────────────────────────────────────────────┤
│                                                  │
│ FloatingOriginSystem.OnUpdate()                 │
│  ├─ Check distance            [Burst ✓]         │
│  └─ Schedule parallel job:                      │
│      │                                          │
│      └─► ShiftWorldOriginJob.Execute()          │
│          ├─ Thread 0: Entities 0-24    [Burst ✓]│
│          ├─ Thread 1: Entities 25-49   [Burst ✓]│
│          ├─ Thread 2: Entities 50-74   [Burst ✓]│
│          └─ Thread 3: Entities 75-99   [Burst ✓]│
│                                                  │
│ (Noise sampling within mesh generation)         │
│  └─► SampleNoise()              [Burst ✓]       │
│      └─ Can run on any thread                   │
│                                                  │
└─────────────────────────────────────────────────┘

Burst Compiled Functions:
✓ = 5-20× faster than C#
✓ = SIMD vectorization
✓ = Aggressive optimizations
```

---

## Coordinate System Reference

```
UNITY WORLD SPACE (Left-handed, Y-up)
────────────────────────────────────────

         Y (Up)
         │
         │
         │
         └─────── X (Right)
        /
       /
      Z (Forward)


TILE GRID SPACE (2D, integer coordinates)
──────────────────────────────────────────

  Z
  ↑
  │ (-1,2) (0,2) (1,2)
  │ (-1,1) (0,1) (1,1)
  │ (-1,0) (0,0) (1,0) ← Grid coordinates
  │ (-1,-1)(0,-1)(1,-1)
  └────────────────────→ X


CONVERSION: Grid → World
─────────────────────────
worldX = gridX × tileSize
worldZ = gridZ × tileSize
worldY = 0 (tile origin, vertices have height)

Example:
Grid (2, -1) with tileSize=100m
→ World (200, 0, -100)


TILE LOCAL SPACE (Within single tile)
──────────────────────────────────────
Origin at tile corner, coordinates relative

  Z ↑
    │  (0, h, 100)  ────── (100, h, 100)
    │       │                    │
    │       │                    │
    │       │                    │
    │  (0, h, 0)   ────── (100, h, 0)
    └──────────────────────────────→ X

All vertices in [0, tileSize] range
Height (Y) varies based on noise
Transform.Position offsets tile to world space
```

---

## Performance Scaling Chart

```
VERTEX COUNT vs GENERATION TIME
────────────────────────────────

Generation    │                               
Time (ms)     │                              ●
   10│        │                         ●
     │        │                    ●
    5│        │               ●
     │        │          ●
    2│        │      ●
     │        │   ●
    1│        │ ●
     │        ●
   0.5│      ●
     └────────┬────────┬────────┬────────┬────
              16      32      48      64   Vertices/Side
              
Relationship: Time ≈ O(n²) where n = verticesPerSide


ACTIVE TILES vs MEMORY
──────────────────────

Memory (MB)   │                           ●
    8│        │                      ●
     │        │                 ●
    4│        │            ●
     │        │       ●
    2│        │   ●
     │        │ ●
    1│       ●
     └────────┬─────┬─────┬─────┬─────┬────
             10    20    30    40    50   Active Tiles

Linear relationship: Memory = tiles × ~150KB


VIEW DISTANCE vs TILES
──────────────────────

Active      │                              ●
Tiles       │                          ●
  200│      │                      ●
     │      │                  ●
  100│      │              ●
     │      │          ●
   50│      │      ●
     │      │  ●
   10│     ●
     └──────┬──────┬──────┬──────┬──────┬────
           200   400   600   800  1000  View Dist (m)

Relationship: Tiles ≈ π × (viewDistance/tileSize)²
```

---

## Quick Reference: Visual Symbols

```
COMPONENT TYPES
───────────────
◆ IComponentData (struct)        - Regular component
■ IComponentData (class)          - Managed component
▲ IBufferElementData              - Dynamic buffer
● Tag Component (empty struct)    - Marker
⬢ Singleton                       - One instance per world

SYSTEM STATES
─────────────
✓ System running
✗ System stopped
⚠ System error
◐ System paused

EXECUTION
─────────
→  Data flow
↓  Sequence
├─ Branch/child
└─ End of branch
═  Important boundary
```

---

## State Machine (Tile Entity)

```
┌──────────────┐
│  SPAWNED     │
│ meshGen=false│
└──────┬───────┘
       │
       │ MeshGenerationSystem
       ▼
┌──────────────┐
│  GENERATED   │
│ meshGen=true │
│ no rendering │
└──────┬───────┘
       │
       │ RenderingSystem + PhysicsSystem (parallel)
       ▼
┌──────────────────┐
│  ACTIVE          │
│ • Visible        │
│ • Collidable     │
│ • Stable         │
└──────┬───────────┘
       │
       ├─ Player moves away ──→ DESPAWN
       │
       ├─ Terrain modified ──┐
       │                     ▼
       │            ┌─────────────────┐
       │            │  NEEDS REGEN    │
       │            │ needsRegen=true │
       │            └────────┬────────┘
       │                     │
       │                     │ MeshGenerationSystem
       │                     ▼
       │            ┌─────────────────┐
       │            │  REGENERATING   │
       │            │ buffers cleared │
       │            └────────┬────────┘
       │                     │
       └─────────────────────┴─────→ ACTIVE (loop back)


┌──────────────┐
│  DESPAWN     │
│ Entity       │
│ destroyed    │
└──────┬───────┘
       │
       ├─ RenderingSystem.OnDestroy → Destroy(mesh)
       ├─ PhysicsSystem.OnDestroy → collider.Dispose()
       └─ TileSpawningSystem → _activeTiles.Remove(gridCoord)
```

---

## Debugging Visualization

```
GIZMO VISUALIZATION (Scene View)
────────────────────────────────

Select TerrainConfig GameObject:
    
    ╭──────────────────╮
   ╱                    ╲     ← Yellow sphere
  │   Shift Threshold   │      (shiftThreshold)
  │      2000m          │
   ╲                    ╱
    ╰─────╭────╮───────╯
          │    │              ← Green sphere
         ╱      ╲              (viewDistance)
        │  View  │
        │  300m  │
         ╲      ╱
          ╰────╯
            │ ← Player
            ● 
            
         ┌──┐         ← Cyan box
         │  │           (current tile)
         └──┘


HIERARCHY VIEW
──────────────
Scene
├─ Main Camera
├─ XR Origin (Player)
│  └─ PlayerTagAuthoring ✓
│  └─ FloatingOriginEnabledAuthoring ✓
│
└─ TerrainSubScene [Closed]
   └─ TerrainConfig
      └─ TerrainConfigAuthoring ✓


ENTITIES HIERARCHY (During Play)
─────────────────────────────────
World: DefaultGameObjectInjectionWorld
├─ Entity: Player [PlayerTag]
│  ├─ LocalTransform
│  └─ FloatingOriginEnabled
│
├─ Entity: TerrainConfig [All singletons]
│  ├─ TerrainTileConfig
│  ├─ FloatingOriginConfig
│  └─ WorldOriginOffset
│
├─ Entity: Tile_0_0 [TerrainTile]
│  ├─ TerrainTile (gridCoord: 0,0)
│  ├─ LocalTransform
│  ├─ FloatingOriginEnabled
│  ├─ VertexElement (buffer: 1024)
│  ├─ NormalElement (buffer: 1024)
│  ├─ UVElement (buffer: 1024)
│  ├─ IndexElement (buffer: 5766)
│  ├─ MeshReference
│  ├─ PhysicsCollider
│  └─ MaterialMeshInfo
│
├─ Entity: Tile_1_0 [TerrainTile]
│  └─ ... (same structure)
│
└─ ... (25 more tiles)
```

---

## See Also

- [SYSTEM_ARCHITECTURE.md](SYSTEM_ARCHITECTURE.md) - Text descriptions of these flows
- [TECHNICAL_DETAILS.md](TECHNICAL_DETAILS.md) - Algorithm implementations
- [API_REFERENCE.md](API_REFERENCE.md) - Component/system details

