# Physics System — Full-Resolution Collider Generation

Complete guide to the terrain physics collider system with distance culling and frame budgeting.

## Overview

The physics system automatically generates Unity Physics mesh colliders for terrain tiles with:
- **Full-resolution geometry** matching the rendered mesh exactly
- **Distance culling** to remove colliders beyond a configurable maximum distance
- **Frame budgeting** split across two independent sliders to prevent creation spikes
- **Camera-aware prioritization** for in-view tiles first
- **Cross-frame async pipeline** so BVH construction runs on worker threads during XRUpdate

## Collider Resolution

All tiles within `maxColliderDistance` use **full-resolution** collider geometry — the same vertex and triangle data as the rendered mesh. There is no vertex-stride decimation or LOD tiering.

| Zone | Distance | Triangles (32×32 tile) |
|------|----------|------------------------|
| Full resolution | ≤ `maxColliderDistance` | ~2000 |
| No collider | > `maxColliderDistance` | none |

For a 48×48 tile (Ace of Ages default), expect ~4600 triangles per collider.

## Frame Budget

Collider creation is split across two independent budgets to avoid frame spikes:

| Field | Controls | Default |
|-------|----------|---------|
| `maxCollidersCreatedPerFrame` | Burst mesh-prep jobs submitted per frame | 6 |
| `maxPhysicsCollidersCreatedPerFrame` | BVH `MeshCollider.Create` calls per frame | 4 |

The effective budget per frame is `min(maxCollidersCreatedPerFrame, maxPhysicsCollidersCreatedPerFrame)`, resolved by `TerrainPhysicsBudget.GetCreationBudget()`. With defaults this is **4** colliders per frame.

**Priority sorting:** Closer tiles and tiles in the camera's forward direction are processed first (combined distance + view-angle score).

## Multi-Stage Pipeline

Collider creation is split across frames to avoid main-thread stalls:

### Stage 1 — `TerrainColliderScheduleSystem` / `TerrainColliderCompleteSystem` (Burst, cross-frame)
1. Queries tiles with `PhysicsColliderNeedsPreparation` enabled
2. Copies full-resolution vertex/index data into prepared buffers
3. Calculates camera-aware priority and writes `PhysicsColliderPrepared`

### Stage 2 — `TerrainPhysicsScheduleSystem` / `TerrainPhysicsCompleteSystem` (Burst BVH, cross-frame)
1. Reads tiles with `PhysicsColliderPrepared`, sorted by priority
2. Calls `MeshCollider.Create()` on worker threads
3. Writes `PhysicsColliderRegistrationPending`

### Stage 3 — `TerrainPhysicsSystem` (main thread, lightweight)
1. Attaches `PhysicsCollider` from pending blob
2. Adds `PhysicsColliderValid` tag

## Systems

### `TerrainDistanceTrackingSystem`
- Runs before `TerrainPhysicsSystem`
- Updates `TerrainTileDistanceToPlayer.distance` for every tile
- Marks tiles within `maxColliderDistance` as needing preparation if not already valid
- Removes collider state from tiles beyond the distance threshold
- Budget-limits how many tiles are marked for preparation per frame

### `CameraDataUpdateSystem`
- Runs at start of `PresentationSystemGroup`
- Reads player Transform → writes `CameraDataSingleton` (position, forward)
- Used by the preparation job for camera-aware priority scoring

### `TerrainColliderScheduleSystem` / `TerrainColliderCompleteSystem`
- Burst-compiled parallel `PrepareColliderDataJob`
- Copies full mesh data into `ColliderPreparedVertexElement` / `ColliderPreparedTriangleElement` buffers

### `TerrainPhysicsScheduleSystem` / `TerrainPhysicsCompleteSystem`
- Burst-compiled `CreateMeshCollidersJob` for async BVH construction

### `TerrainPhysicsSystem`
- Namespace: `_App.Ace_of_Ages.Terrain`
- Lightweight registration step — attaches prepared collider blobs to entities

## Configuration Reference

### VR (Quest 3 / PC VR, recommended)
```
maxColliderDistance:                   450m
maxCollidersCreatedPerFrame:           6
maxPhysicsCollidersCreatedPerFrame:    4
```

### Desktop (high-end, RTX 4080+)
```
maxColliderDistance:                   600m
maxCollidersCreatedPerFrame:           12
maxPhysicsCollidersCreatedPerFrame:    8
```

### Mobile / Quest 2
```
maxColliderDistance:                   300m
maxCollidersCreatedPerFrame:           4
maxPhysicsCollidersCreatedPerFrame:    2
```

## Profiler Markers

| Marker | System | What it covers |
|--------|--------|----------------|
| `TerrainPhysics.DistanceTracking` | TerrainDistanceTrackingSystem | Distance calc + prep marking |
| `TerrainPhysics.ColliderSchedule` | TerrainColliderScheduleSystem | Burst mesh copy job schedule |
| `TerrainPhysics.ColliderComplete` | TerrainColliderCompleteSystem | Harvest prepared buffers |
| `TerrainPhysics.BvhSchedule` | TerrainPhysicsScheduleSystem | BVH job schedule |
| `TerrainPhysics.BvhComplete` | TerrainPhysicsCompleteSystem | Harvest BVH results |
| `TerrainPhysics.RegisterCollider` | TerrainPhysicsSystem | Attach PhysicsCollider |

## Common Issues

**Colliders not generating:**
- Check `enablePhysicsColliders = true` in TerrainConfigAuthoring
- Verify player tracking is working (`[PlayerTrackingInitSystem] ✅ Found player` in console)
- Confirm tiles have finished mesh generation (`meshGenerated = true`)

**Physics spikes:**
- Lower `maxPhysicsCollidersCreatedPerFrame` (try 2–3 for Quest 2/Pro)
- Reduce `verticesPerSide` if collider triangle count is too high

**Distant terrain has no collider:**
- Expected — tiles beyond `maxColliderDistance` have no physics
- Increase `maxColliderDistance` if gameplay requires it (costs more memory and creation time)

## Related Documentation

- **[Configuration Reference](CONFIGURATION.md)** — All `TerrainConfigAuthoring` parameters
- **[Technical Details](TECHNICAL_DETAILS.md)** — Noise algorithms, mesh generation details
- **[Performance Optimization](PERFORMANCE.md)** — Tuning strategies

---

**Back to:** [Documentation Hub](README.md)
