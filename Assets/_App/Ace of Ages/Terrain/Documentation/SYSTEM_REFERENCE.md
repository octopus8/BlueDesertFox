# System Reference - All Terrain Systems
**Version:** 3.0  
**Last Updated:** May 4, 2026

Complete reference for all ECS systems in the terrain implementation.

## System Overview

The terrain system consists of **20+ ECS systems** organized in categories:

### Core Terrain Systems (9)
1. **PlayerTrackingInitSystem** - Initialization
2. **ScrollTerrainSystem** - Auto-scrolling
3. **TileSpawningSystem** - Tile lifecycle
4. **TileScrollPositionSystem** - Position updates
5. **TerrainMeshGenerationSystem** - Mesh generation
6. **TerrainDistanceTrackingSystem** - LOD calculation
7. **TerrainColliderPreparationSystem** - Collider preparation
8. **TerrainPhysicsSystem** - Collider creation
9. **TerrainRenderingSystem** - Mesh rendering

### Static Object Management Systems (5)
10. **TerrainStaticObjectSpawningSystemOptimized** - Static object spawning on tiles
11. **StaticObjectSpatialChunkingSystem** - Spatial chunk assignment
12. **StaticObjectPositionUpdateSystem** - Static object position updates
13. **StaticObjectLODUpdateSystem** - Dynamic LOD with hysteresis
14. **StaticObjectLODMeshInfoInitSystem** - BRG mesh info initialization (one-shot)

### Scroll Velocity Systems (2)
15. **PlayerScrollVelocitySystem** - Player rotation-based velocity
16. **ConstantScrollVelocitySystem** - Fixed velocity vector

### Utility Systems (2)
17. **TerrainAnchorSystem** - Anchored entity positioning
18. **WorldOriginTrackingInitSystem** - Optional world origin tracking

### Debug Tools (3)
- **TerrainTrackingDebugger** (MonoBehaviour)
- **TerrainTileGizmoVisualizer** (MonoBehaviour)
- **TerrainRenderingDebugSystem** (ECS system, disabled by default)
- **StaticObjectLODDebugSystem** (ECS system, Editor only)
- **StaticObjectCleanupDebugSystem** (ECS system, disabled by default)

---

## System 1: PlayerTrackingInitSystem

**File**: `PlayerTrackingInitSystem.cs`  
**Update Group**: InitializationSystemGroup  
**Update Order**: Default (first in group)  
**Type**: SystemBase (needs managed component access)

### Purpose
Finds player GameObject at startup and populates `PlayerTransformReference` with the found Transform.

### Requirements
```csharp
RequireForUpdate<PlayerTrackingSearch>()
RequireForUpdate<PlayerTransformReference>()
```

### Execution
- Runs every frame until all `PlayerTrackingSearch` components have `initialized = true`
- Once initialized, effectively stops (early returns)

### Algorithm
```
1. Query entities with PlayerTrackingSearch (uninitialized)
2. For each entity:
   a. Read search parameters (mode, searchString)
   b. Execute search based on mode:
      - FindByName: GameObject.Find(name)
      - FindByTag: GameObject.FindGameObjectWithTag(tag)
      - FindMainCamera: Camera.main
   c. If found:
      - Get PlayerTransformReference component
      - Set playerTransform field
      - Set initialized = true
   d. If not found:
      - Log warning
      - Try fallback (AutoDetect mode only)
```

### Performance
- **First Frame**: ~0.1-1ms (searching for GameObject)
- **Subsequent Frames**: <0.01ms (early return)
- **After Init**: Essentially zero (no processing)

### Code Example
```csharp
protected override void OnUpdate()
{
    foreach (var (search, entity) in SystemAPI.Query<RefRW<PlayerTrackingSearch>>().WithEntityAccess())
    {
        if (search.ValueRO.initialized)
            continue; // Skip already initialized
        
        Transform playerTransform = FindPlayer(search.ValueRO);
        
        if (playerTransform != null)
        {
            var playerRef = EntityManager.GetComponentObject<PlayerTransformReference>(entity);
            playerRef.playerTransform = playerTransform;
            search.ValueRW.initialized = true;
        }
    }
}
```

---

## System 2: ScrollTerrainSystem

**File**: `ScrollTerrainSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateBefore(typeof(TileSpawningSystem))]`  
**Type**: ISystem (Burst-compilable struct)

### Purpose
Updates terrain scroll offset for auto-scrolling functionality.

### Requirements
```csharp
RequireForUpdate<ScrollConfig>()
RequireForUpdate<ScrollOffset>()
RequireForUpdate<PlayerTransformReference>()
```

### Execution
- Runs every frame if scrolling enabled
- Early returns if `ScrollConfig.enabled = false` or `scrollSpeed = 0`

### Algorithm
```
1. Read ScrollConfig
2. If not enabled, return
3. Read PlayerTransformReference (get player forward direction)
4. Project forward direction onto XZ plane (remove Y)
5. Normalize direction vector
6. Calculate scroll delta: scrollSpeed × deltaTime
7. Accumulate to ScrollOffset: offset += direction × delta
```

### Performance
- **CPU**: <0.01ms per frame
- **Memory**: 12 bytes (one float3)
- **Burst**: Yes (except PlayerTransformReference access)

### Code Example
```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var config = SystemAPI.GetSingleton<ScrollConfig>();
    if (!config.enabled || config.scrollSpeed == 0f)
        return;
    
    var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
    if (playerRef?.playerTransform == null)
        return;
    
    // Get forward projected onto XZ
    Vector3 forward = playerRef.playerTransform.forward;
    float3 scrollDirection = math.normalize(new float3(forward.x, 0, forward.z));
    
    // Update offset
    RefRW<ScrollOffset> scrollOffset = SystemAPI.GetSingletonRW<ScrollOffset>();
    float scrollDelta = config.scrollSpeed * SystemAPI.Time.DeltaTime;
    scrollOffset.ValueRW.accumulatedOffset += scrollDirection * scrollDelta;
}
```

---

## System 3: TileSpawningSystem

**File**: `TileSpawningSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateBefore(typeof(TransformSystemGroup))]`  
**Type**: ISystem (Burst-compilable struct)

### Purpose
Manages tile entity lifecycle - spawns tiles entering view range, destroys tiles exiting view range.

### Requirements
```csharp
RequireForUpdate<PlayerTransformReference>()
RequireForUpdate<TerrainTileConfig>()
RequireForUpdate<ScrollOffset>()
```

### Internal State
```csharp
private NativeParallelHashMap<int2, Entity> _activeTiles;
```

**Persists across frames**: Tracks which grid coordinates have active entities.

### Algorithm
```
1. Read player position from PlayerTransformReference
2. Read scroll offset (for effective position calculation)
3. Calculate effective player position: player + scrollOffset
4. Calculate player grid coordinate from effective position
5. Determine view distance in tiles (ceil(viewDistance / tileSize))
6. For each tile in view range:
   a. Calculate tile center position (with scroll applied)
   b. Check distance to player (circular culling)
   c. If within viewDistance AND not in activeTiles map:
      - Add to tilesToSpawn list
7. For each active tile in activeTiles:
   a. Calculate distance to player (with scroll)
   b. If beyond viewDistance:
      - Add to tilesToDespawn list
8. Create entities for tilesToSpawn via EntityCommandBuffer
9. Destroy entities for tilesToDespawn
10. Update activeTiles map
```

### Performance
- **Typical**: 0.1-0.5ms per frame
- **Spawning 10 tiles**: ~0.3ms
- **Despawning 10 tiles**: ~0.2ms
- **Steady state**: ~0.05ms (no spawning/despawning)

### Code Example
```csharp
public void OnUpdate(ref SystemState state)
{
    var config = SystemAPI.GetSingleton<TerrainTileConfig>();
    var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
    var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
    
    float3 playerPosition = playerRef.playerTransform.position;
    float3 effectivePlayerPosition = playerPosition + scrollOffset.accumulatedOffset;
    
    int2 playerGridCoord = new int2(
        (int)math.floor(effectivePlayerPosition.x / config.tileSize),
        (int)math.floor(effectivePlayerPosition.z / config.tileSize)
    );
    
    // Spawn/despawn logic...
}
```

---

## System 4: TileScrollPositionSystem

**File**: `TileScrollPositionSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateAfter(typeof(ScrollTerrainSystem))]`, `[UpdateBefore(typeof(TransformSystemGroup))]`  
**Type**: ISystem (Burst-compilable struct)

### Purpose
Updates tile positions to apply scroll offset for auto-scrolling effect.

### Requirements
```csharp
RequireForUpdate<ScrollConfig>()
RequireForUpdate<ScrollOffset>()
RequireForUpdate<TerrainTileConfig>()
```

### Algorithm
```
1. Read ScrollConfig
2. If not enabled, return
3. Read ScrollOffset and TerrainTileConfig
4. For each terrain tile entity:
   a. Calculate base position from grid coordinates
   b. Subtract scroll offset: position = base - scrollOffset
   c. Update LocalTransform.Position
```

### Performance
- **CPU**: ~0.05ms for 25 tiles
- **Scales**: Linearly with tile count
- **Burst**: Yes (full Burst compilation)

### Code Example
```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var config = SystemAPI.GetSingleton<ScrollConfig>();
    if (!config.enabled) return;
    
    var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
    var tileConfig = SystemAPI.GetSingleton<TerrainTileConfig>();
    
    foreach (var (tile, transform) in SystemAPI.Query<RefRO<TerrainTile>, RefRW<LocalTransform>>())
    {
        float3 basePosition = new float3(
            tile.ValueRO.gridCoordinate.x * tileConfig.tileSize,
            0,
            tile.ValueRO.gridCoordinate.y * tileConfig.tileSize
        );
        
        transform.ValueRW.Position = basePosition - scrollOffset.accumulatedOffset;
    }
}
```

---

## System 5: TerrainMeshGenerationSystem

**File**: `TerrainMeshGenerationSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateAfter(typeof(TileSpawningSystem))]`  
**Type**: ISystem (Burst-compilable struct)

### Purpose
Generates procedural terrain meshes using multi-octave Perlin noise, with camera-aware prioritization and frame budgeting.

### Requirements
```csharp
RequireForUpdate<TerrainTileConfig>()
```

### Internal State
```csharp
private NativeQueue<Entity> _pendingTiles;  // Persistent queue
```

### Algorithm
```
1. Get camera position and forward direction (for prioritization)
2. Query tiles with meshGenerated = false or needsRegeneration = true
3. Add tiles to pending queue
4. Collect pending tiles with priority calculation
5. Sort by priority if queue > budget (camera-aware)
6. Process top N tiles (N = frame budget):
   a. Schedule Burst job for mesh generation
   b. Job fills vertex/normal/UV/index buffers
   c. Job uses Perlin noise for heights
   d. Set meshGenerated = true
7. Remaining tiles stay in queue for next frame
```

### Nested Job
```csharp
[BurstCompile]
partial struct MeshGenerationJob : IJobEntity
{
    // Generates mesh for one tile
    // Parallel execution across multiple tiles
}
```

### Performance
- **Per Tile**: 5-10ms (depends on vertices and octaves)
- **Frame Budgeted**: Processes 3 tiles per frame (default)
- **Parallel**: Multiple tiles generate simultaneously
- **Burst**: Yes (full optimization)

---

## System 6: TerrainDistanceTrackingSystem

**File**: `TerrainDistanceTrackingSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateBefore(typeof(TerrainPhysicsSystem))]`  
**Type**: SystemBase

### Purpose
Calculates distance from each tile to player and determines appropriate physics LOD level.

### Requirements
```csharp
RequireForUpdate<TerrainTileConfig>()
```

### Algorithm
```
1. Read TerrainTileConfig
2. Read PlayerTransformReference
3. For each terrain tile:
   a. Calculate 2D distance (XZ plane) to player
   b. Determine LOD level based on thresholds
   c. Compare to previous LOD level
   d. If LOD changed:
      - Remove PhysicsColliderValid tag
      - Add PhysicsColliderNeedsPreparation
   e. Update TerrainTileDistanceToPlayer component
4. Apply changes via EntityCommandBuffer
```

### Performance
- **CPU**: ~0.1ms for 25 tiles, ~0.5ms for 100 tiles
- **Scales**: Linearly with tile count
- **Main Thread**: Yes (accesses PlayerTransformReference)

---

## System 7: TerrainColliderPreparationSystem

**File**: `TerrainColliderPreparationSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateAfter(typeof(TerrainMeshGenerationSystem))]`  
**Type**: ISystem

### Purpose
Prepares collider data using Burst-compiled parallel jobs, applies LOD decimation.

### Requirements
```csharp
RequireForUpdate<TerrainTileConfig>()
```

### Nested Job
```csharp
[BurstCompile]
[WithAll(typeof(PhysicsColliderNeedsPreparation))]
partial struct PrepareColliderDataJob : IJobEntity
{
    // Decimates vertices and regenerates triangles
    // Runs in parallel for multiple tiles
}
```

### Algorithm
```
1. Get camera position/forward (for priority)
2. Schedule PrepareColliderDataJob with .ScheduleParallel()
3. Job for each tile with PhysicsColliderNeedsPreparation:
   a. Read target LOD level
   b. Decimate source vertices (skip by stride)
   c. Regenerate triangles for decimated vertices
   d. Fill ColliderPreparedVertex/Triangle buffers
   e. Calculate priority
   f. Add PhysicsColliderPrepared component
4. Complete job handle
```

### Performance
- **Per Tile**: 0.5-2ms (parallel execution)
- **Multiple Tiles**: Concurrent (limited by core count)
- **Burst**: Yes (full Burst compilation)

---

## System 8: TerrainPhysicsSystem

**File**: `TerrainPhysicsSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateAfter(typeof(TerrainColliderPreparationSystem))]`  
**Type**: SystemBase

### Purpose
Creates Unity Physics MeshCollider instances with caching, LRU eviction, and frame budgeting.

### Requirements
```csharp
RequireForUpdate<TerrainTileConfig>()
```

### Internal State
```csharp
private NativeHashMap<ColliderCacheKey, ColliderCacheEntry> _colliderCache;
private long _totalCacheMemoryBytes;
private long _currentFrameNumber;
```

### Three-Phase Architecture

**Phase 1: Cache Lookup and Sorting**
```
1. Query tiles with PhysicsColliderPrepared
2. Collect into NativeList
3. Sort by priority (lower = process first)
```

**Phase 2: Collider Creation (Frame Budgeted)**
```
1. For each sorted tile (while under budget):
   a. Calculate cache key from config hash
   b. Check cache for existing collider
   c. If cache hit:
      - Reuse BlobAsset (~0.1ms)
      - Update last access frame (LRU)
   d. If cache miss:
      - Create MeshCollider (~3-5ms)
      - Store BlobAsset in cache
      - Track memory usage
   e. Add PhysicsCollider component to entity
   f. Remove prepared buffers
2. Stop when budget exhausted
```

**Phase 3: LRU Eviction**
```
If cache memory > limit:
  1. Sort cache entries by lastAccessFrame (oldest first)
  2. Evict entries until under limit
  3. Dispose BlobAssets
  4. Update memory tracking
```

### Performance
- **Cache Hit**: ~0.1ms per collider (near-instant)
- **Cache Miss**: 2-5ms per collider (expensive)
- **Frame Budgeted**: Max 3 colliders (default) = ~9-15ms worst case
- **Cache Hit Rate**: >90% typical after warm-up

---

## System 9: TerrainRenderingSystem

**File**: `TerrainRenderingSystem.cs`  
**Update Group**: PresentationSystemGroup  
**Update Order**: Default  
**Type**: SystemBase

### Purpose
Creates Unity Mesh objects from buffer data and configures Entities Graphics rendering.

### Requirements
```csharp
RequireForUpdate<TerrainTileConfig>()
```

### Internal State
```csharp
private Material _terrainMaterial;
private EntityQuery _newTilesQuery;
```

### Algorithm
```
1. Load terrain material (OnStartRunning, once)
2. Query tiles with mesh data but no MeshReference
3. For each tile:
   a. Create Unity Mesh object
   b. Copy buffers to mesh (zero-copy via Reinterpret)
   c. Calculate bounds from vertices
   d. Add MeshReference component (managed)
   e. Add Entities Graphics components:
      - MaterialMeshInfo
      - RenderBounds
      - RenderFilterSettings
```

### Performance
- **Per Tile**: 0.5-1ms (main thread)
- **Bottleneck**: Unity Mesh API (main-thread only)
- **Future**: Could add frame budgeting

### Code Example
```csharp
protected override void OnUpdate()
{
    if (_terrainMaterial == null) return;
    
    foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
        .WithAll<VertexElement>()
        .WithNone<MeshReference>()
        .WithEntityAccess())
    {
        if (tile.ValueRO.meshGenerated)
        {
            var vertices = EntityManager.GetBuffer<VertexElement>(entity);
            // ... get other buffers ...
            
            CreateAndAssignMesh(entity, vertices, normals, uvs, indices);
        }
    }
}
```

---

## Systems 10-18: Tree & Scroll Velocity Systems

**Detailed Documentation:** See [Tree Rendering System](TREE_RENDERING_SYSTEM.md) for comprehensive coverage of:
- **System 10:** TerrainStaticObjectSpawningSystemOptimized
- **System 11:** StaticObjectSpatialChunkingSystem
- **System 12:** StaticObjectPositionUpdateSystem
- **System 13:** StaticObjectLODUpdateSystem
- **System 14:** StaticObjectLODMeshInfoInitSystem
- **System 15:** PlayerScrollVelocitySystem
- **System 16:** ConstantScrollVelocitySystem
- **System 17:** TerrainAnchorSystem
- **System 18:** WorldOriginTrackingInitSystem

### Quick Reference

**Tree Management Systems** (10-14):
- Spawning, spatial chunking, position updates, LOD, instanced rendering
- Performance: <2ms for 1000+ trees (Quest 3)
- v3.0 optimizations: Spatial grid culling (30-40% improvement)

**Scroll Velocity Systems** (15-16):
- Flexible scroll sources (player rotation or constant vector)
- Integrate with ScrollTerrainSystem via ScrollVelocity singleton
- Enable gameplay variety (rotation-based, fixed-speed, etc.)

**Utility Systems** (17-18):
- TerrainAnchorSystem: Update entities moving with terrain
- WorldOriginTrackingInitSystem: Optional world origin management

---

## System Update Order Diagram (Complete v3.0)

```
Frame Start
    │
    ┌─────────────────────────────────────┐
    │  InitializationSystemGroup          │
    ├─────────────────────────────────────┤
    │  PlayerTrackingInitSystem           │
    └─────────────────────────────────────┘
    │
    ┌─────────────────────────────────────┐
    │  SimulationSystemGroup              │
    ├─────────────────────────────────────┤
    │                                     │
    │  1. ScrollTerrainSystem             │
    │     ↓ (ScrollOffset updated)        │
    │  2. TileSpawningSystem              │
    │     ↓ (Entities created/destroyed)  │
    │  3. TileScrollPositionSystem        │
    │     ↓ (Positions updated)           │
    │  4. TerrainMeshGenerationSystem     │
    │     ↓ (Mesh buffers filled)         │
    │  5. TerrainDistanceTrackingSystem   │
    │     ↓ (LOD levels determined)       │
    │  6. TerrainColliderPreparationSystem│
    │     ↓ (Collider data prepared)      │
    │  7. TerrainPhysicsSystem            │
    │     ↓ (PhysicsColliders created)    │
    │                                     │
    │  TransformSystemGroup               │
    │  (Updates LocalToWorld)             │
    └─────────────────────────────────────┘
    │
    ┌─────────────────────────────────────┐
    │  PresentationSystemGroup            │
    ├─────────────────────────────────────┤
    │  TerrainRenderingSystem             │
    │     (Meshes created, rendering set up)│
    └─────────────────────────────────────┘
    │
Frame End
```

---

## System Dependencies

### Singleton Dependencies

| System | Required Singletons |
|--------|---------------------|
| PlayerTrackingInitSystem | PlayerTrackingSearch, PlayerTransformReference |
| ScrollTerrainSystem | ScrollConfig, ScrollOffset, PlayerTransformReference |
| TileSpawningSystem | PlayerTransformReference, TerrainTileConfig, ScrollOffset |
| TileScrollPositionSystem | ScrollConfig, ScrollOffset, TerrainTileConfig |
| TerrainMeshGenerationSystem | TerrainTileConfig |
| TerrainDistanceTrackingSystem | TerrainTileConfig, PlayerTransformReference |
| TerrainColliderPreparationSystem | TerrainTileConfig |
| TerrainPhysicsSystem | TerrainTileConfig |
| TerrainRenderingSystem | TerrainTileConfig |

### Entity Dependencies

| System | Requires | Adds | Removes |
|--------|----------|------|---------|
| TileSpawningSystem | - | TerrainTile, LocalTransform, Buffers | Entity (when far) |
| TerrainMeshGenerationSystem | TerrainTile, Buffers | - | - (sets meshGenerated) |
| TerrainRenderingSystem | TerrainTile, Buffers | MeshReference, MaterialMeshInfo | - |
| TerrainDistanceTrackingSystem | TerrainTile | TerrainTileDistanceToPlayer, PhysicsColliderNeedsPreparation | PhysicsColliderValid |
| TerrainColliderPreparationSystem | PhysicsColliderNeedsPreparation | PhysicsColliderPrepared, Prepared buffers | PhysicsColliderNeedsPreparation |
| TerrainPhysicsSystem | PhysicsColliderPrepared | PhysicsCollider, PhysicsColliderValid | PhysicsColliderPrepared, Prepared buffers |

---

## System 17: TerrainAnchorSystem

**File**: `TerrainAnchorSystem.cs`  
**Update Group**: SimulationSystemGroup  
**Update Order**: `[UpdateAfter(typeof(ScrollTerrainSystem))]`, `[UpdateBefore(typeof(TransformSystemGroup))]`  
**Type**: ISystem (Burst-compilable struct)

### Purpose
Updates positions of entities marked as terrain anchors to keep them synchronized with scrolling terrain. Allows spawned obstacles, decorations, and other non-tile entities to move with the terrain scroll offset while maintaining their base position.

### Requirements
```csharp
state.RequireForUpdate<ScrollOffset>();
state.RequireForUpdate<TerrainAnchorTag>();
```

**Note**: System only runs when entities with `TerrainAnchorTag` exist (zero-cost when no anchors).

### Algorithm
```
1. Read scroll offset from ScrollOffset singleton
2. Schedule parallel job across CPU cores:
   a. For each entity with TerrainAnchorTag:
      - Read base position from anchor component
      - Calculate new position: basePosition - scrollOffset
      - Update LocalTransform.Position
3. Chain job dependency for TransformSystemGroup
```

### Performance
- **100 anchors**: 0.05-0.1ms (Quest 3)
- **500 anchors**: 0.2-0.4ms (Quest 3)
- **1000 anchors**: 0.4-0.6ms (Quest 3)
- **Optimization**: Parallel Burst-compiled IJobEntity (3-5x faster than sequential)
- **Scalability**: Distributes across all CPU cores (8 cores on Quest 3)

### Code Example
```csharp
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var scrollOffset = SystemAPI.GetSingleton<ScrollOffset>();
    
    // Schedule parallel job to update anchor positions
    var updateJob = new TerrainAnchorUpdateJob
    {
        scrollOffset = scrollOffset.accumulatedOffset
    };
    
    state.Dependency = updateJob.ScheduleParallel(state.Dependency);
}

[BurstCompile]
private partial struct TerrainAnchorUpdateJob : IJobEntity
{
    [ReadOnly] public float3 scrollOffset;
    
    private void Execute(in TerrainAnchorTag anchor, ref LocalTransform transform)
    {
        // Subtract to make anchor move opposite to scroll direction
        transform.Position = anchor.basePosition - scrollOffset;
    }
}
```

### Usage Guidelines

**Use TerrainAnchorTag for**:
- Spawned obstacles/pickups that should scroll with terrain
- Environmental decorations (rocks, bushes) placed on tiles
- Non-tile entities that need to move with scroll offset

**Do NOT use for**:
- ❌ **Static Objects** - Use `StaticObjectTileOwnership` + `StaticObjectPositionUpdateSystem` instead
- ❌ **Terrain Tiles** - Handled by `TileScrollPositionSystem` automatically
- ❌ **Player/Camera** - Should remain stationary while terrain scrolls

### Authoring Component

Attach `TerrainAnchorTagAuthoring` to GameObjects in SubScenes:

```csharp
// In Inspector:
TerrainAnchorTagAuthoring
├─ Use Custom Base Position: false (default uses GameObject position)
└─ Custom Base Position: (0, 0, 0) (only if custom enabled)
```

**Important**: GameObject must be in a SubScene to be converted to entity at runtime.

### Integration Notes

**System Update Order**:
```
SimulationSystemGroup
  ├─ ScrollTerrainSystem (updates ScrollOffset)
  ├─ TileSpawningSystem
  ├─ TileScrollPositionSystem (updates tile positions)
  ├─ TerrainAnchorSystem (updates anchor positions) ← HERE
  └─ TransformSystemGroup (propagates LocalTransform to LocalToWorld)
```

**Dependency Chain**:
- Waits for `ScrollTerrainSystem` to update `ScrollOffset`
- Runs before `TransformSystemGroup` to ensure transforms propagate
- No conflicts with other terrain systems (independent data)

### Optimization History

**v1.0** (May 2026): Parallel IJobEntity optimization
- Converted from sequential `foreach` to parallel job execution
- Added Burst compilation for SIMD optimization
- Result: 3-5x speedup with 1000+ entities on Quest 3
- Pattern matches: `StaticObjectPositionUpdateSystem`, `TileScrollPositionSystem`

---

## System Performance Summary

| System | Typical Time | Burst | Parallel | Main Thread |
|--------|--------------|-------|----------|-------------|
| PlayerTrackingInitSystem | <0.01ms | ❌ | ❌ | ✅ |
| ScrollTerrainSystem | <0.01ms | Partial | ❌ | ✅ |
| TileSpawningSystem | 0.1-0.5ms | Partial | ❌ | ✅ |
| TileScrollPositionSystem | ~0.05ms | ✅ | ❌ | ❌ |
| TerrainMeshGenerationSystem | 5-10ms | ✅ | ✅ | ❌ |
| TerrainDistanceTrackingSystem | 0.1-0.5ms | ❌ | ❌ | ✅ |
| TerrainColliderPreparationSystem | 1-2ms | ✅ | ✅ | ❌ |
| TerrainPhysicsSystem | 5-15ms | ❌ | ❌ | ✅ |
| TerrainRenderingSystem | 1-3ms | ❌ | ❌ | ✅ |
| TerrainAnchorSystem | 0.05-0.6ms | ✅ | ✅ | ❌ |

**Total Typical Frame**: 8-15ms (with budgeting)
**Note**: TerrainAnchorSystem time scales with anchor count (100-1000 entities)

---

## System Enable/Disable

### Runtime Disable

Systems can be disabled dynamically:

```csharp
var world = World.DefaultGameObjectInjectionWorld;

// Disable scrolling (stops ScrollTerrainSystem)
var scrollQuery = world.EntityManager.CreateEntityQuery(typeof(ScrollConfig));
var entity = scrollQuery.GetSingletonEntity();
var config = world.EntityManager.GetComponentData<ScrollConfig>(entity);
config.enabled = false;
world.EntityManager.SetComponentData(entity, config);
scrollQuery.Dispose();
```

### System Groups

All systems are in standard Unity system groups:
- Easy to find in Entity Debugger
- Standard update order
- Compatible with other ECS systems

---

## Related Documentation

- **[System Pipeline](SYSTEM_PIPELINE.md)** - Detailed execution flow
- **[Component Reference](COMPONENT_REFERENCE.md)** - Components used by systems
- **[API Reference](API_REFERENCE.md)** - Code examples
- **[Performance Optimization](PERFORMANCE.md)** - Optimizing system performance

---

**Back to**: [Documentation Hub](README.md)

