# Static Object Rendering System (v3.0)

Complete guide to how static objects (trees, turrets, decorations) are rendered with Entities Graphics (BatchRendererGroup), spatial culling, and distance LOD.

## Overview

Static objects spawned onto terrain tiles are rendered using Unity's **Entities Graphics** package (BatchRendererGroup / BRG). This avoids per-object draw calls and achieves thousands of objects at <2ms on Quest 3.

The rendering pipeline consists of four cooperating systems:

```
StaticObjectLODMeshInfoInitSystem   (Initialization — one-shot)
       ↓
StaticObjectSpatialChunkingSystem   (Simulation — updates chunk membership)
       ↓
TreeLODUpdateSystem                 (Simulation — distance LOD with hysteresis)
       ↓
[Entities.Graphics / BRG]          (Presentation — automatic, reads MaterialMeshInfo)
```

Note: the class names inside `StaticObjectSpatialChunkingSystem.cs` and `StaticObjectLODUpdateSystem.cs` are still `TreeSpatialChunkingSystem` and `TreeLODUpdateSystem` respectively — the rename was applied to the files only.

---

## System 1: `StaticObjectLODMeshInfoInitSystem`

**File:** `StaticObjectLODMeshInfoInitSystem.cs`  
**Group:** `InitializationSystemGroup`  
**Type:** `SystemBase` (managed — reads baked prefab components)  
**Runs:** Every frame until ready, then self-disables (`Enabled = false`)

### Purpose

Entities.Graphics requires a `MaterialMeshInfo` component on each entity to know which BRG-registered mesh and material to render. The spawning system needs to set the correct `MaterialMeshInfo` for the LOD level chosen at spawn time.

This system runs once at startup to build a lookup buffer: `StaticObjectLODMaterialMeshInfoElement` on the config entity. The buffer maps `objectTypeIndex × lodsPerObjectType + lodLevel` to the correct `MaterialMeshInfo`.

### Algorithm

1. Query the config entity (has `StaticObjectSpawnerConfig` + `StaticObjectPrefabElement` buffer)
2. For each prefab entity in the buffer: read its `MaterialMeshInfo` (baked by Entities.Graphics)
3. Write into `StaticObjectLODMaterialMeshInfoElement` buffer
4. Add `StaticObjectLODMeshInfoReady` tag → subsequent systems can proceed
5. Set `Enabled = false` to stop running

If any prefab entity is missing `MaterialMeshInfo` (not yet baked), the system retries next frame and logs a warning.

---

## System 2: `StaticObjectSpatialChunkingSystem` (class: `TreeSpatialChunkingSystem`)

**File:** `StaticObjectSpatialChunkingSystem.cs`  
**Group:** `SimulationSystemGroup`  
**Order:** Before `TreeLODUpdateSystem`  
**Type:** `ISystem`, Burst-compiled  

### Purpose

Assigns each static object entity to a 100×100m spatial chunk (`StaticObjectChunkMembership`). The LOD update system uses these chunk coordinates to skip distant objects without iterating all entities.

### Algorithm

Two parallel jobs per frame:

**`AssignChunkJob`** — runs on entities with `GlobalStaticObjectInstance` but WITHOUT `StaticObjectChunkMembership`:
1. Calculate `chunkCoord = floor(position / 100)` for x and z
2. Add `StaticObjectChunkMembership { chunkCoord }` via ECB

**`UpdateChunkJob`** — runs on entities WITH `StaticObjectChunkMembership`:
1. Recalculate chunk from current `LocalTransform.Position`
2. Write to `chunkMembership.chunkCoord` only if changed (cache-friendly)

### Performance

- Fully Burst-compiled, parallel across CPU cores
- Only writes when chunk changes — near-zero cost when objects are stationary
- `ChunkSize = 100f` (must match `TreeLODUpdateSystem.ChunkSize`)

---

## System 3: `TreeLODUpdateSystem` (class: `TreeLODUpdateSystem`, file: `StaticObjectLODUpdateSystem.cs`)

**Group:** `SimulationSystemGroup`  
**Order:** After `TerrainDistanceTrackingSystem`, before `TreeSpatialChunkingSystem`  
**Type:** `ISystem`, Burst-compiled job (`ScheduleParallel`)

### Purpose

Updates `MaterialMeshInfo` on each static object based on distance to the player, switching the BRG mesh/material for the appropriate LOD level. Applies hysteresis to prevent LOD flickering at distance boundaries.

### LOD Levels

| Level | Component | Typical range | Update frequency |
|-------|-----------|---------------|-----------------|
| LOD0 | High-detail mesh | < `lod0Distance` (50m) | Every frame |
| LOD1 | Medium-detail mesh | `lod0Distance` → `lod1Distance` (100m) | Every 2 frames |
| LOD2 | Low-detail mesh | `lod1Distance` → `lod2Distance` (200m) | Every 4 frames |
| (beyond LOD2) | Stays at LOD2 | > `lod2Distance` | Every 8 frames |

### Hysteresis

Prevents objects flickering between LOD levels when the player is near a boundary:

```
Transition UP   (higher detail): requires distance < boundary - hysteresis
Transition DOWN (lower detail):  requires distance > boundary + hysteresis
```

Default `hysteresisDelta = 5f` (5 meters of dead zone at each boundary).

### Spatial Chunk Filtering

Rather than testing every object each frame, the system:
1. Identifies the player's chunk and the 8 surrounding chunks (9 total, always processed)
2. Adds additional chunks via rotating frame counter to ensure full coverage over time
3. Skips objects not in the active chunk set (O(1) `NativeHashSet.Contains` check)

### Velocity-Aware Frame Skipping

- When player velocity < `playerVelocityThreshold`: skip every `VRFrameSkip` (2) frames
- When player velocity ≥ threshold (fast scrolling): increase to `vrFrameSkipScrolling` for higher LOD responsiveness
- Near objects (0–100m) always update; far objects (300m+) update every 8 frames

### LOD Change Mechanism

When LOD level changes:
```csharp
int newMeshIndex = (instanceData.objectTypeIndex * lodsPerObjectType) + newLOD;
materialMeshInfo = lodMeshInfos[newMeshIndex]; // From the init-time buffer
instanceData.currentLODLevel = newLOD;
```

Entities.Graphics (BRG) reads `MaterialMeshInfo` each frame and switches meshes/materials automatically — no Unity Object access, no managed allocations.

---

## System 4: `StaticObjectLinkedRendererStripSystem`

**Group:** `SimulationSystemGroup`  
**Order:** After `EndSimulationEntityCommandBufferSystem`  
**Type:** `ISystem`

### Purpose

When a prefab with child GameObjects is instantiated, Entities.Graphics bakes a `LinkedEntityGroup` containing child entities. This hierarchy can interfere with the rendering pipeline.

After instantiation, this system iterates entities with `PendingStaticObjectRendererStrip` and calls `StaticObjectHierarchyFlattenUtility.FlattenSpawnHierarchy()` to remove the hierarchy, leaving each child entity with its own `MaterialMeshInfo` and `LocalTransform` at the correct world position.

---

## Components

### `GlobalStaticObjectInstance` (Tag)
Marks entities that participate in BRG rendering. Required by `StaticObjectSpatialChunkingSystem` and `TreeLODUpdateSystem` to scope their queries.

### `GlobalStaticObjectInstanceData`
```csharp
struct GlobalStaticObjectInstanceData : IComponentData
{
    public byte currentLODLevel;       // 0=high, 1=medium, 2=low
    public float lastDistanceToPlayer; // For hysteresis calculation
    public int objectTypeIndex;        // Which prefab type (for mesh lookup)
}
```

### `StaticObjectChunkMembership`
```csharp
struct StaticObjectChunkMembership : IComponentData
{
    public int2 chunkCoord; // 100m grid cell this object belongs to
}
```

### `StaticObjectLODMaterialMeshInfoElement` (Buffer)
On the config entity. Maps `objectTypeIndex * lodsPerObjectType + lodLevel` → `MaterialMeshInfo`.

### `StaticObjectLODMeshInfoReady` (Tag)
On the config entity. Present when `StaticObjectLODMeshInfoInitSystem` has finished populating the buffer. `TreeLODUpdateSystem` requires this to run.

### `PendingStaticObjectRendererStrip` (Tag)
Added to entities just after instantiation. Triggers hierarchy flattening in `StaticObjectLinkedRendererStripSystem`.

---

## Configuration (`StaticObjectLODConfig`)

Set via `StaticObjectSpawnerConfigAuthoring`:

| Field | Default | Description |
|-------|---------|-------------|
| `lod0Distance` | 50m | Within this distance: LOD0 (highest detail) |
| `lod1Distance` | 100m | Within this: LOD1 (medium detail) |
| `lod2Distance` | 200m | Within this: LOD2 (low detail) |
| `hysteresisDelta` | 5m | Dead zone width at LOD boundaries |
| `maxChunksUpdatedPerFrame` | 20 | Spatial chunks processed per LOD update pass |
| `vrFrameSkipScrolling` | 1 | Frame skip during fast scrolling (less = more responsive) |
| `playerVelocityThreshold` | 10 m/s | Velocity above which scrolling frame skip applies |
| `enableObjectLODDebug` | false | Log LOD stats every 120 frames |
| `lodsPerObjectType` | 3 | Number of LOD levels per object type (LOD0/1/2) |

---

## Profiler Markers

| Marker | System |
|--------|--------|
| `TreeLOD.Update` | Main LOD update pass |
| `TreeLOD.VelocityCalc` | Player velocity calculation |
| `TreeLOD.ChunkFilter` | Active chunk set construction |

---

## Troubleshooting

**Objects not rendering:**
- Check `StaticObjectLODMeshInfoReady` tag exists on config entity (Entity Debugger)
- Verify prefab has `MaterialMeshInfo` (added by Entities.Graphics baker for prefabs with renderers)
- Console: `[StaticObjectLODMeshInfoInit] Populated N LOD MaterialMeshInfo slots.` should appear at startup

**LOD flickering:**
- Increase `hysteresisDelta` (try 8–10m)
- Reduce scroll speed to reduce velocity-driven frame skip mismatch

**Trees flash in front of camera with distance culling enabled:**
- Caused by stale LOD2 billboard mesh when `DisableRendering` is removed before LOD updates run
- Fixed by running `StaticObjectLODUpdateSystem` after EndSimulation ECB and syncing pose/LOD in `UnCullNearObjectsJob`
- If flashes persist, verify `enableDistanceCulling` is on and check Entity Debugger: un-culled trees should not show LOD2 when within `lod0Distance`

**Objects disappear at distance:**
- Check `lod2Distance` setting — beyond this objects stay at LOD2 (not culled)
- If culling is desired, add distance culling using `StaticObjectLODConfig.maxObjectRenderDistance`

**Performance spikes on LOD transitions:**
- Reduce `maxChunksUpdatedPerFrame`
- Increase `vrFrameSkipScrolling` to 2–3

---

## Related Documentation

- **[Static Object Spawning](../STATIC_OBJECT_SPAWNING_SYSTEM.md)** — How objects are placed on tiles
- **[Configuration Reference](CONFIGURATION.md)** — `StaticObjectSpawnerConfigAuthoring` fields
- **[Performance Guide](PERFORMANCE.md)** — Optimization for VR platforms

---

**Back to:** [Documentation Hub](README.md)
