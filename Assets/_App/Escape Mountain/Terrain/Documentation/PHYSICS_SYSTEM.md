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

For a 48×48 tile (Escape Mountain default), expect ~4600 triangles per collider.

## Frame Budget

Collider work uses **split stage budgets** via `TerrainPhysicsBudget`:

| Field | Controls | Default |
|-------|----------|---------|
| `maxCollidersCreatedPerFrame` | Mesh generation + prep marking per frame | 6 |
| `maxPhysicsCollidersCreatedPerFrame` | BVH `BuildTerrainMeshColliderJob` batch size per cross-frame cycle | 1 (Quest), up to 4 desktop |
| `maxColliderCacheMemoryMB` | LRU grid-coordinate blob cache size | 53 MB |

| Helper | Returns |
|--------|---------|
| `GetPrepMarkBudget()` | `maxCollidersCreatedPerFrame` |
| `GetBvhCreationBudget()` | `maxPhysicsCollidersCreatedPerFrame` (clamped to 2 on mobile) |
| `GetRegistrationBudget()` | Same as BVH budget |

**Priority sorting:** Closer tiles and tiles in the camera's forward direction are processed first (combined distance + view-angle score).

## Pipeline (2-stage cross-frame)

### Stage 1 — `TerrainPhysicsScheduleSystem` / `TerrainPhysicsCompleteSystem` (Burst BVH, cross-frame)
1. Queries tiles with `PhysicsColliderNeedsPreparation` enabled and mesh buffers ready
2. Checks grid-coordinate blob cache first (via `TerrainDistanceTrackingSystem` on mark)
3. Copies `VertexElement` / `IndexElement` into Persistent NativeArrays
4. Runs `BuildTerrainMeshColliderJob` (`MeshCollider.Create`) on worker threads (budget: `GetBvhCreationBudget()`)
5. Writes `PhysicsColliderRegistrationPending`

### Stage 2 — `TerrainPhysicsSystem` (main thread, lightweight)
1. Attaches `PhysicsCollider` from pending blob
2. Adds `PhysicsColliderValid` tag

When tiles leave `maxColliderDistance`, collider blobs are **retired to an LRU cache** keyed by `TerrainTile.gridCoordinate` instead of being destroyed. Re-entering tiles reuse cached blobs with no BVH rebuild.

## Systems

### `TerrainDistanceTrackingSystem`
- Runs before `TerrainPhysicsSystem`
- Updates `TerrainTileDistanceToPlayer.distance` for every tile
- Checks grid-coordinate blob cache before marking tiles for BVH work
- Marks tiles within `maxColliderDistance` with `PhysicsColliderNeedsPreparation` (budget: `GetPrepMarkBudget()`)
- Retires collider blobs to cache when tiles exceed distance threshold

### `TerrainColliderBlobCacheSystem`
- LRU cache of `BlobAssetReference<Collider>` keyed by `int2` grid coordinate
- Evicts oldest entries when `maxColliderCacheMemoryMB` exceeded

### `CameraDataUpdateSystem`
- Runs at start of `PresentationSystemGroup`
- Reads player Transform → writes `CameraDataSingleton` (position, forward)
- Used for camera-aware BVH priority scoring

### `TerrainPhysicsScheduleSystem` / `TerrainPhysicsCompleteSystem`
- Burst-compiled `BuildTerrainMeshColliderJob` for async BVH construction
- Logs warning when `BvhComplete` waits > 8ms (Quest 90fps threshold)

### `TerrainPhysicsSystem`
- Namespace: `_App.Ace_of_Ages.Terrain`
- Lightweight registration step — attaches prepared collider blobs to entities

## Configuration Reference

### VR (Quest 2 / Quest 3, recommended)
```
maxColliderDistance:                   220-450m
maxCollidersCreatedPerFrame:           6
maxPhysicsCollidersCreatedPerFrame:    1-2
maxColliderCacheMemoryMB:              53
verticesPerSide:                       48 (Escape Mountain — keep full-res)
```

### Desktop (high-end, RTX 4080+)
```
maxColliderDistance:                   600m
maxCollidersCreatedPerFrame:           12
maxPhysicsCollidersCreatedPerFrame:    6-8
maxColliderCacheMemoryMB:              53
```

### Mobile / Quest 2 (conservative)
```
maxColliderDistance:                   300m
maxCollidersCreatedPerFrame:           4
maxPhysicsCollidersCreatedPerFrame:    1
maxColliderCacheMemoryMB:              32
```

## Profiler Markers

| Marker | System | What it covers |
|--------|--------|----------------|
| `TerrainPhysics.DistanceTracking` | TerrainDistanceTrackingSystem | Distance calc + cache lookup + prep marking |
| `TerrainPhysics.BvhSchedule` | TerrainPhysicsScheduleSystem | BVH job schedule + ECS buffer copy |
| `TerrainPhysics.BvhComplete` | TerrainPhysicsCompleteSystem | Harvest BVH results (warns if > 8ms) |
| `BuildTerrainMeshColliderJob` | Worker threads | Per-tile MeshCollider.Create (Burst) |
| `TerrainPhysics.RegisterCollider` | TerrainPhysicsSystem | Attach PhysicsCollider |

## Common Issues

**Colliders not generating:**
- Check `enablePhysicsColliders = true` in TerrainConfigAuthoring
- Verify player tracking is working (`[PlayerTrackingInitSystem] ✅ Found player` in console)
- Confirm tiles have finished mesh generation (`meshGenerated = true`)

**Physics spikes:**
- Lower `maxPhysicsCollidersCreatedPerFrame` to **1–2** on Quest (most effective for `BuildTerrainMeshColliderJob` tail latency)
- Keep `maxCollidersCreatedPerFrame` at 6 for mesh throughput — BVH budget is independent
- Ensure grid cache is enabled (`maxColliderCacheMemoryMB > 0`) to avoid rebuilds when revisiting tiles during scroll

**Distant terrain has no collider:**
- Expected — tiles beyond `maxColliderDistance` have no physics
- Increase `maxColliderDistance` if gameplay requires it (costs more memory and creation time)

## Related Documentation

- **[Configuration Reference](CONFIGURATION.md)** — All `TerrainConfigAuthoring` parameters
- **[Technical Details](TECHNICAL_DETAILS.md)** — Noise algorithms, mesh generation details
- **[Performance Optimization](PERFORMANCE.md)** — Tuning strategies

---

**Back to:** [Documentation Hub](README.md)
