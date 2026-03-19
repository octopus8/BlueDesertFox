# Debug Tools - Diagnostic Utilities

Complete guide to debugging and diagnostic tools for the terrain system.

## Available Debug Tools

The terrain system includes three debug utilities:

1. **TerrainTrackingDebugger** - Runtime diagnostics and status checks
2. **TerrainTileGizmoVisualizer** - Scene view visualization
3. **TerrainRenderingDebugSystem** - Rendering diagnostics (optional)

---

## TerrainTrackingDebugger

**File**: `TerrainTrackingDebugger.cs`  
**Type**: MonoBehaviour  
**Purpose**: Runtime diagnostic tool for tracking and tile status

### Setup

1. Create GameObject in your scene (e.g., "TerrainDebug")
2. Add `TerrainTrackingDebugger` component
3. Configure Inspector settings:
   - Show GUI: ✅ (displays on-screen status)
   - Log Every Frame: ❌ (causes spam)
   - Log Interval: 2.0s

### Context Menu Commands

Right-click the component in Inspector:

#### Check Tracking Status

**Command**: `Check Tracking Status`  
**Purpose**: Verifies player tracking is working

**Output Example (Success)**:
```
=== Terrain Tracking Status ===
🔍 Search Mode: FindAutoHandPlayer
🔍 Search String: ''
🔍 Initialized: True
✅ Tracking: XR Origin Hands (XR Rig)
   GameObject: XR Origin Hands (XR Rig)
   Position: (0.0, 1.5, 0.0)
   Active: True
📦 Active Terrain Tiles: 25
✅ TerrainTileConfig found
   Tile Size: 100m
   View Distance: 500m
   Vertices Per Side: 32
```

**Output Example (Failure)**:
```
=== Terrain Tracking Status ===
❌ No default ECS world found! Is the scene running?
```

Or:
```
⚠️ PlayerTransformReference exists but Transform is null!
   Player search has not completed yet. Wait a frame or check PlayerTrackingInitSystem.
```

#### Force Refresh Player

**Command**: `Force Refresh Player`  
**Purpose**: Manually re-runs player search  
**Use When**: Player spawns late or tracking needs reset

---

### On-Screen GUI

When `Show GUI` enabled, displays:

```
┌────────────────────────────────┐
│ Terrain Tracking Status        │
├────────────────────────────────┤
│ Player: XR Origin Hands        │
│ Position: (0.0, 1.5, 0.0)      │
│ Active Tiles: 25               │
│                                │
│ Tracking Valid: ✅             │
└────────────────────────────────┘
```

**Position**: Top-left corner of Game view  
**Update Rate**: Every frame (low overhead)  
**Color Coding**:
- Green ✅: Tracking valid
- Red ❌: Tracking failed

### Inspector Fields

**Read-Only Status Display**:
- Show GUI - Toggle on-screen display
- Tracking Valid - Is player tracking working?
- Player Position - Current player position
- Player Name - Tracked GameObject name
- Active Tile Count - Number of active tiles

**Debug Settings**:
- Log Every Frame - Logs tracking status every frame (warning: spam!)
- Log Interval - Seconds between automatic status logs

### API Access

```csharp
// Get debugger instance
var debugger = FindFirstObjectByType<TerrainTrackingDebugger>();

// Trigger status check from code
debugger.CheckTrackingStatus();

// Force refresh player
debugger.ForceRefreshPlayer();
```

---

## TerrainTileGizmoVisualizer

**File**: `TerrainTileGizmoVisualizer.cs`  
**Type**: MonoBehaviour  
**Purpose**: Visualizes tiles in Scene view with wireframes and labels

### Setup

1. Create GameObject in scene (e.g., "TileVisualizer")
2. Add `TerrainTileGizmoVisualizer` component
3. Configure visualization:
   - Draw Tile Bounds: ✅
   - Draw Grid Coordinates: ✅
   - Tile Color: Green
   - Tile With Mesh Color: Yellow
   - Tile With Rendering Color: Cyan

### Visualization Features

#### Tile Bounds Wireframes

Shows color-coded boxes for each tile:

**Green**: Tile exists, no mesh data yet
```
Tile spawned, waiting for mesh generation
```

**Yellow**: Tile has mesh data
```
Mesh generated, waiting for rendering setup
```

**Cyan**: Tile has rendering components
```
Fully functional, visible in Game view
```

#### Grid Coordinate Labels

Shows grid coordinates as text above each tile:
```
    (-1, 1)      (0, 1)       (1, 1)
    
    (-1, 0)    👤 (0, 0)      (1, 0)
    
    (-1, -1)     (0, -1)      (1, -1)
```

**Size**: Scales with Scene view zoom  
**Position**: Top of tile bounds box

### Inspector Info

Real-time statistics:

**Total Tiles**: Count of all tile entities  
**Tiles With Mesh**: Count with mesh data generated  
**Tiles With Rendering**: Count with rendering components

### Scene View Usage

**Workflow**:
1. Enter Play mode
2. Open Scene view (alongside Game view)
3. Navigate around to see tiles
4. Color changes show tile states
5. Grid coordinates help identify specific tiles

**Tips**:
- Use 2D view mode for top-down tile overview
- Frame selection (F key) on tiles to zoom to them
- Pause playmode to freeze tile state

### Example Use Cases

**Use Case 1: Verify Spawning**
```
Problem: No terrain visible
Debug: Add gizmo visualizer
Result: See green wireframes → tiles spawning but not generating
Solution: Check mesh generation system
```

**Use Case 2: Check Generation Progress**
```
Watch colors change: Green → Yellow → Cyan
Shows pipeline progression for each tile
```

**Use Case 3: Debug Scrolling**
```
Enable Draw Grid Coordinates
Watch grid numbers change as terrain scrolls
Verify tiles spawn ahead, despawn behind
```

---

## TerrainRenderingDebugSystem

**File**: `TerrainRenderingDebugSystem.cs`  
**Type**: SystemBase  
**Purpose**: Logs rendering component status periodically  
**Status**: Disabled by default (commented out UpdateInGroup)

### Enabling the System

Uncomment the update group attribute:

```csharp
// Before:
// [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]

// After:
[UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
```

### Console Output

Logs every 10 seconds:

```
[TerrainDebug] ========== Terrain Tile Analysis ==========
[TerrainDebug] Total tiles: 25
[TerrainDebug] Camera position: (0.0, 1.6, 0.0)
[TerrainDebug] Camera culling mask: 1
[TerrainDebug] Camera far clip: 1000
[TerrainDebug] Tiles with mesh data: 25
[TerrainDebug] Tiles with rendering components: 23
[TerrainDebug] Tiles with LocalToWorld: 25
[TerrainDebug] Tiles with RenderBounds: 23
[TerrainDebug] --- First Tile Detail (Entity 123:1) ---
[TerrainDebug]   Grid: (0, 0)
[TerrainDebug]   MeshGenerated: True
[TerrainDebug]   Position: (0.0, 0.0, 0.0)
[TerrainDebug]   Has MaterialMeshInfo: True
```

### When to Enable

**Enable when**:
- Tiles spawning but not rendering
- Need detailed component status
- Debugging rendering pipeline

**Keep disabled when**:
- System working correctly (reduces console spam)
- Building for release (minor performance impact)

---

## Unity Built-in Debug Tools

### Entity Debugger

**Window → Entities → Hierarchy**

Shows all ECS entities in world:
```
World (Default)
├─ Systems
│  ├─ InitializationSystemGroup
│  │  └─ PlayerTrackingInitSystem
│  ├─ SimulationSystemGroup
│  │  ├─ ScrollTerrainSystem
│  │  ├─ TileSpawningSystem
│  │  └─ ...
│  └─ PresentationSystemGroup
│     └─ TerrainRenderingSystem
│
└─ Entities
   ├─ Entity 0 (Singletons)
   │  ├─ TerrainTileConfig
   │  ├─ ScrollConfig
   │  └─ ...
   ├─ Entity 1 (Terrain Tile)
   │  ├─ TerrainTile
   │  ├─ LocalTransform
   │  ├─ VertexElement [buffer]
   │  └─ ...
   └─ Entity 2 (Terrain Tile)
      └─ ...
```

**Usage**:
1. Select entity to inspect components
2. View component values in real-time
3. Verify component presence/absence

---

### Systems Window

**Window → Entities → Systems**

Shows system execution timing:
```
SimulationSystemGroup (11.2ms)
├─ ScrollTerrainSystem (0.01ms)
├─ TileSpawningSystem (0.3ms)
├─ TileScrollPositionSystem (0.05ms)
├─ TerrainMeshGenerationSystem (8.5ms) ← Bottleneck!
├─ TerrainDistanceTrackingSystem (0.1ms)
├─ TerrainColliderPreparationSystem (1.2ms)
└─ TerrainPhysicsSystem (5.8ms)
```

**Usage**:
1. Identify slow systems (red bars)
2. Click system to see details
3. Profile marker breakdown available

---

### Profiler

**Window → Analysis → Profiler**

**Key Markers** to watch:
- `TerrainMesh.Generation` - Overall mesh generation time
- `TerrainMesh.JobSchedule` - Job overhead
- `TerrainPhysics.ColliderCreation` - Main thread collider creation
- `TerrainPhysics.CacheLookup` - Cache access time
- `TerrainPhysics.LRUEviction` - Cache eviction time

**Profile Workflow**:
1. Enable "Deep Profile"
2. Play scene
3. Move player to trigger tile spawning
4. Look for terrain markers in CPU timeline
5. Check frame spikes align with terrain work

---

## Custom Debug Extensions

### Add Custom Gizmo Colors

Extend `TerrainTileGizmoVisualizer`:

```csharp
// In TerrainTileGizmoVisualizer.cs, add field:
public Color tileWithPhysicsColor = Color.blue;

// In OnDrawGizmos():
if (EntityManager.HasComponent<PhysicsCollider>(entity))
{
    Gizmos.color = tileWithPhysicsColor;
}
```

**Result**: Blue wireframes show tiles with colliders.

---

### Add Distance Display

Show distance to each tile:

```csharp
void OnDrawGizmos()
{
    // ... existing gizmo code ...
    
    if (EntityManager.HasComponent<TerrainTileDistanceToPlayer>(entity))
    {
        var distInfo = EntityManager.GetComponentData<TerrainTileDistanceToPlayer>(entity);
        
        // Draw distance as text
        var labelPos = tileCenterWorld + Vector3.up * 5f;
        Handles.Label(labelPos, $"{distInfo.distance:F0}m\nLOD: {distInfo.lodLevel}");
    }
}
```

---

### Add Performance Overlay

Create custom performance monitor:

```csharp
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

public class TerrainPerformanceMonitor : MonoBehaviour
{
    private ProfilerRecorder _meshGenRecorder;
    private ProfilerRecorder _physicsRecorder;
    
    void OnEnable()
    {
        _meshGenRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Scripts, 
            "TerrainMesh.Generation"
        );
        _physicsRecorder = ProfilerRecorder.StartNew(
            ProfilerCategory.Scripts, 
            "TerrainPhysics.ColliderCreation"
        );
    }
    
    void OnDisable()
    {
        _meshGenRecorder.Dispose();
        _physicsRecorder.Dispose();
    }
    
    void OnGUI()
    {
        GUILayout.Label($"Mesh Gen: {_meshGenRecorder.LastValue / 1e6:F2}ms");
        GUILayout.Label($"Physics: {_physicsRecorder.LastValue / 1e6:F2}ms");
    }
}
```

---

## Diagnostic Workflows

### Workflow 1: "Terrain Not Spawning"

```
Step 1: Add TerrainTrackingDebugger
Step 2: Right-click → Check Tracking Status
Step 3: Review console output

If "Transform is null":
  → Player tracking failed
  → See PLAYER_TRACKING.md

If "Active Terrain Tiles: 0":
  → Spawning system not running
  → Check SubScene baked correctly
  → Check TerrainTileConfig exists

If "Active Terrain Tiles: 25":
  → Tiles spawning, but not visible
  → Continue to Workflow 2
```

### Workflow 2: "Terrain Not Visible"

```
Step 1: Add TerrainTileGizmoVisualizer
Step 2: Open Scene view while in Play mode
Step 3: Look for wireframes

If GREEN wireframes:
  → Tiles spawned, no mesh data
  → Check TerrainMeshGenerationSystem
  → Check console for generation errors

If YELLOW wireframes:
  → Mesh data exists, not rendering
  → Check TerrainRenderingSystem
  → Check material exists
  → Check camera settings

If CYAN wireframes:
  → Rendering components added, still not visible
  → Check camera culling mask
  → Check camera far clip plane
  → Check RenderBounds values
```

### Workflow 3: "Performance Issues"

```
Step 1: Open Profiler (Window → Analysis → Profiler)
Step 2: Enable "Deep Profile"
Step 3: Play and move player
Step 4: Identify bottleneck system

If TerrainMesh.Generation high:
  → Reduce Vertices Per Side
  → Reduce Noise Octaves
  → Increase frame budget

If TerrainPhysics.ColliderCreation high:
  → Reduce Max Colliders Per Frame
  → Increase cache size
  → Optimize LOD distances
```

### Workflow 4: "Scrolling Not Working"

```
Step 1: Add TerrainTrackingDebugger with Show GUI enabled
Step 2: Watch tile count while scrolling
Step 3: Add TerrainTileGizmoVisualizer with Draw Grid Coordinates
Step 4: Watch grid numbers change

If tile count stable:
  → Tiles not spawning/despawning
  → Check Scroll Enabled = true
  → Check Scroll Speed ≠ 0

If grid numbers not changing:
  → TileScrollPositionSystem not running
  → Check ScrollConfig.enabled
  → Check console for errors

If visual terrain not moving but numbers change:
  → Rendering using cached positions
  → Restart scene
```

---

## Logging Strategies

### Strategic Logging

Add logs at key points:

#### In PlayerTrackingInitSystem

Already logs by default:
```csharp
Debug.Log("[PlayerTrackingInitSystem] Attempting to find player GameObject...");
Debug.Log($"[PlayerTrackingInitSystem] ✅ Found player: {playerTransform.name}");
```

#### In ScrollTerrainSystem

Uncomment to log scroll progress:
```csharp
#if UNITY_EDITOR
float totalDistance = math.length(scrollOffset.ValueRO.accumulatedOffset);
if (totalDistance % 100f < config.scrollSpeed * SystemAPI.Time.DeltaTime)
{
    UnityEngine.Debug.Log($"ScrollTerrainSystem: Scrolled {totalDistance:F1}m");
}
#endif
```

Logs every 100m scrolled.

#### Custom Tile Spawning Logs

Add to TileSpawningSystem:
```csharp
// After spawning tiles
#if UNITY_EDITOR
UnityEngine.Debug.Log($"[TileSpawning] Spawned {tilesToSpawn.Length} tiles, " +
                      $"despawned {tilesToDespawn.Length} tiles");
#endif
```

### Conditional Compilation

Use `#if UNITY_EDITOR` to exclude logs from builds:

```csharp
#if UNITY_EDITOR
Debug.Log("Debug info");
#endif
```

**Benefits**:
- Zero cost in builds
- No string allocations in release
- Easier debugging in editor

---

## Performance Profiling

### Using Profiler Markers

**View Markers**:
1. Open Profiler (Window → Analysis → Profiler)
2. Enable "Deep Profile"
3. Look for markers starting with "Terrain" or "TerrainPhysics"

**Available Markers**:
- `TerrainMesh.Generation`
- `TerrainMesh.JobSchedule`
- `TerrainMesh.BufferCopy`
- `TerrainMesh.PrioritySort`
- `TerrainPhysics.PrepareJob`
- `TerrainPhysics.DistanceTracking`
- `TerrainPhysics.CacheLookup`
- `TerrainPhysics.ColliderCreation`
- `TerrainPhysics.LRUEviction`

**Add Custom Markers**:
```csharp
#if UNITY_EDITOR
using Unity.Profiling;

private static readonly ProfilerMarker s_MyMarker = 
    new ProfilerMarker("MyCustomMarker");

void MyMethod()
{
    using (s_MyMarker.Auto())
    {
        // Code to profile
    }
}
#endif
```

---

## Entity Inspection

### Entity Debugger Window

**Window → Entities → Hierarchy**

**Features**:
- Browse all entities by world
- Select entity to see components
- View component values in real-time
- Filter by component type

**Usage Example**:
```
1. Find "World (Default)" → "Entities"
2. Search filter: "TerrainTile"
3. Select first entity
4. Inspector shows all components:
   - TerrainTile: gridCoordinate (0, 0), meshGenerated: true
   - LocalTransform: Position (0, 0, 0)
   - VertexElement: [1024 elements]
   - ...
```

### Query Debugger

**Window → Entities → Query**

**Features**:
- View all queries in systems
- See entity counts per query
- Debug query performance

**Usage**:
```
1. Find "TileSpawningSystem"
2. View query: [TerrainTile]
3. Shows: 25 entities matched
```

---

## Common Debug Scenarios

### Scenario 1: New System Not Running

**Symptoms**: System code doesn't execute

**Debug Steps**:
```
1. Window → Entities → Systems
2. Find your system in hierarchy
3. Check if listed and enabled
4. Check RequireForUpdate dependencies
```

**Common Causes**:
- Required singleton doesn't exist
- System not in update group
- System disabled via [DisableAutoCreation]

---

### Scenario 2: Component Not Added

**Symptoms**: HasComponent returns false

**Debug Steps**:
```
1. Window → Entities → Hierarchy
2. Select entity
3. Check component list in Inspector
4. Verify component added via ECB
```

**Common Causes**:
- ECB not played back
- Structural change during query iteration
- Entity destroyed before component added

---

### Scenario 3: Buffer Empty

**Symptoms**: Buffer.Length = 0 after population

**Debug Steps**:
```
1. Add breakpoint in system populating buffer
2. Step through code
3. Verify buffer.Add() calls execute
4. Check buffer capacity
```

**Common Causes**:
- Job didn't complete (dependency not completed)
- Buffer cleared after population
- Wrong entity queried

---

### Scenario 4: Cache Not Working

**Symptoms**: Low cache hit rate, high collider creation time

**Debug Steps**:
```
Add logging in TerrainPhysicsSystem:

if (_colliderCache.TryGetValue(cacheKey, out var entry))
{
    Debug.Log($"Cache HIT for LOD {lodLevel}");
}
else
{
    Debug.Log($"Cache MISS for LOD {lodLevel}, creating new collider");
}

// Periodically log cache stats
Debug.Log($"Cache size: {_colliderCache.Count()}, Memory: {_totalCacheMemoryBytes/1024/1024}MB");
```

**Common Causes**:
- Config changes invalidate cache
- Cache memory limit too low
- Hash collision (rare)

---

## Advanced Debugging Techniques

### Technique 1: Entity Snapshot

Capture entity state for analysis:

```csharp
public class EntitySnapshot
{
    public Entity entity;
    public int2 gridCoordinate;
    public bool meshGenerated;
    public float3 position;
    public int vertexCount;
    public bool hasPhysics;
    public bool hasRendering;
}

public EntitySnapshot[] CaptureAllTiles()
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    var query = em.CreateEntityQuery(typeof(TerrainTile));
    var entities = query.ToEntityArray(Allocator.Temp);
    
    var snapshots = new EntitySnapshot[entities.Length];
    
    for (int i = 0; i < entities.Length; i++)
    {
        var entity = entities[i];
        var tile = em.GetComponentData<TerrainTile>(entity);
        var transform = em.GetComponentData<LocalTransform>(entity);
        var vertices = em.GetBuffer<VertexElement>(entity);
        
        snapshots[i] = new EntitySnapshot
        {
            entity = entity,
            gridCoordinate = tile.gridCoordinate,
            meshGenerated = tile.meshGenerated,
            position = transform.Position,
            vertexCount = vertices.Length,
            hasPhysics = em.HasComponent<Unity.Physics.PhysicsCollider>(entity),
            hasRendering = em.HasComponent<MeshReference>(entity)
        };
    }
    
    entities.Dispose();
    query.Dispose();
    
    return snapshots;
}
```

---

### Technique 2: Tile State Machine Tracer

Track tile state transitions:

```csharp
public class TileStateTracer : MonoBehaviour
{
    private Dictionary<Entity, string> _tileStates = new();
    
    void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile));
        var entities = query.ToEntityArray(Allocator.Temp);
        
        foreach (var entity in entities)
        {
            string state = DetermineState(em, entity);
            
            if (!_tileStates.ContainsKey(entity))
            {
                _tileStates[entity] = state;
                Debug.Log($"Tile {entity.Index} created: {state}");
            }
            else if (_tileStates[entity] != state)
            {
                Debug.Log($"Tile {entity.Index} transition: {_tileStates[entity]} → {state}");
                _tileStates[entity] = state;
            }
        }
        
        entities.Dispose();
        query.Dispose();
    }
    
    string DetermineState(EntityManager em, Entity entity)
    {
        var tile = em.GetComponentData<TerrainTile>(entity);
        
        if (!tile.meshGenerated)
            return "SPAWNED";
        else if (!em.HasComponent<MeshReference>(entity))
            return "MESH_GENERATED";
        else if (!em.HasComponent<Unity.Physics.PhysicsCollider>(entity))
            return "RENDERING";
        else
            return "COMPLETE";
    }
}
```

---

### Technique 3: Visualize Priorities

Show priority values in Scene view:

```csharp
// Add to TerrainTileGizmoVisualizer
void OnDrawGizmos()
{
    // ... existing code ...
    
    // Show priority for collider preparation
    if (EntityManager.HasComponent<PhysicsColliderPrepared>(entity))
    {
        var prepared = EntityManager.GetComponentData<PhysicsColliderPrepared>(entity);
        
        var labelPos = tileCenterWorld + Vector3.up * 10f;
        Handles.Label(labelPos, $"Priority: {prepared.priority}");
    }
}
```

---

## Console Filtering

### Filter Terrain Logs

In Console window:
```
Search: [Terrain
Shows: [TerrainMesh], [TerrainPhysics], [PlayerTracking] logs
```

### Log Categories

Use consistent prefixes:
- `[PlayerTracking]` - Player tracking messages
- `[TileSpawning]` - Tile creation/destruction
- `[TerrainMesh]` - Mesh generation
- `[TerrainPhysics]` - Collider creation
- `[TerrainDebug]` - Debug system messages

---

## Related Documentation

- **[Troubleshooting Guide](TROUBLESHOOTING.md)** - Problem solving with debug tools
- **[Performance Optimization](PERFORMANCE.md)** - Using profiling for optimization
- **[System Pipeline](SYSTEM_PIPELINE.md)** - Understanding system execution
- **[API Reference](API_REFERENCE.md)** - Programmatic debugging

---

**Back to**: [Documentation Hub](README.md)

