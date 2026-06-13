# Physics System — Distance-Based Collider Generation

Complete guide to the terrain physics collider system with adaptive resolution, LRU caching, and frame budgeting.

## Overview

The physics system automatically generates Unity Physics mesh colliders for terrain tiles with:
- **Distance-based resolution** — full resolution close to the player, reduced resolution farther away
- **LRU caching** for collider reuse across tiles with identical parameters
- **Frame budgeting** split across two independent sliders to prevent creation spikes
- **Camera-aware prioritization** for in-view tiles first
- **Distance culling** to remove colliders beyond a configurable maximum distance

## Collider Resolution

### Two Resolution Levels

Collider geometry adapts based on the tile's distance from the player:

| Zone | Distance | Vertex Stride | Triangles (32×32 tile) |
|------|----------|---------------|------------------------|
| Full resolution | ≤ `physicsColliderFullResolutionDistance` (128m) | 1 (every vertex) | ~2000 |
| Reduced resolution | > 128m and ≤ `maxColliderDistance` (450m) | `physicsColliderVertexStride` (default 2) | ~500 |
| No collider | > `maxColliderDistance` | — | none |

**Full resolution** matches the rendered mesh exactly — perfect physics accuracy for nearby tiles.

**Reduced resolution** samples every Nth vertex (stride 2 = every other vertex, ~4× fewer triangles). Distant colliders rarely need per-vertex accuracy and this significantly reduces `MeshCollider.Create` cost.

### Configurable Parameters

Both thresholds are set in `TerrainConfigAuthoring`:

```
physicsColliderFullResolutionDistance:  128m   (full-res zone radius)
physicsColliderVertexStride:            2      (stride beyond full-res; 1=full, 2=half, 4=quarter)
maxColliderDistance:                    450m   (remove colliders beyond this)
```

## Collider Caching

### Why Cache?

All tiles with identical noise parameters and vertex count generate the same collider shape at the same distance band. Caching allows tiles to share a single `BlobAssetReference<TerrainColliderBlob>`.

### Cache Key

```csharp
struct ColliderCacheKey
{
    public int verticesPerSide;
    public uint noiseParamsHash;  // Hash of frequency, amplitude, octaves, lacunarity, persistence
}
```

Note: the cache key does not include the vertex stride. Each collider is stored at its actual decimated resolution. In practice the cache hit rate is very high because most terrain tiles share the same noise configuration.

### Cache Performance

| Scenario | Cost |
|----------|------|
| Cache hit | ~0.1 ms (blob asset reuse) |
| Cache miss, full resolution | 3–8 ms (MeshCollider.Create) |
| Cache miss, stride=2 | 1–3 ms (fewer triangles) |
| Typical hit rate | >90% after initial tile load |

### Memory Management

**Default limit:** 50 MB (`maxColliderCacheMemoryMB`)  
**Eviction policy:** LRU (Least Recently Used) — entries removed when total exceeds limit  
**Tracking:** Per-frame access timestamps in `ColliderCacheEntry`

## Frame Budget

Collider creation is split across two independent budgets to avoid frame spikes:

| Field | Controls | Default |
|-------|----------|---------|
| `maxCollidersCreatedPerFrame` | Burst mesh-prep jobs submitted per frame | 6 |
| `maxPhysicsCollidersCreatedPerFrame` | Main-thread `MeshCollider.Create` calls per frame | 4 |

The effective budget per frame is `min(maxCollidersCreatedPerFrame, maxPhysicsCollidersCreatedPerFrame)`, resolved by `TerrainPhysicsBudget.GetCreationBudget()`. With defaults this is **4** colliders per frame.

**Priority sorting:** Closer tiles and tiles in the camera's forward direction are processed first (combined distance + view-angle score).

## Two-Stage Pipeline

Collider creation is deliberately split across two frames to avoid main-thread stalls:

### Stage 1 — `TerrainColliderPreparationSystem` (Burst, parallel)
1. Queries tiles with `PhysicsColliderNeedsPreparation` enabled
2. Applies vertex stride decimation based on tile distance
3. Writes prepared vertex/triangle data into `ColliderPreparedVertexElement` / `ColliderPreparedTriangleElement` buffers via ECB
4. Calculates camera-aware priority and writes `PhysicsColliderPrepared`

### Stage 2 — `TerrainPhysicsSystem` (main thread)
1. Reads tiles with `PhysicsColliderPrepared`, sorted by priority
2. Checks LRU cache — returns existing `BlobAssetReference<Collider>` on hit
3. On miss: calls `MeshCollider.Create()` with prepared buffer data, stores result in cache
4. Writes `PhysicsColliderRegistrationPending` — actual `PhysicsCollider` component added next frame to avoid same-frame physics rebuild

## Systems

### `TerrainDistanceTrackingSystem`
- Runs before `TerrainPhysicsSystem`
- Updates `TerrainTileDistanceToPlayer.distance` for every tile
- Marks tiles within `maxColliderDistance` as needing preparation if not already valid
- Removes collider state from tiles beyond the distance threshold
- Budget-limits how many tiles are marked for preparation per frame

### `CameraDataUpdateSystem`
- Runs before `TerrainColliderPreparationSystem`
- Reads player Transform → writes `CameraDataSingleton` (position, forward)
- Used by the preparation job for camera-aware priority scoring

### `TerrainColliderPreparationSystem`
- Burst-compiled parallel `IJobEntity`
- Reads vertex stride from tile distance and config
- Produces decimated mesh data for `TerrainPhysicsSystem`

### `TerrainPhysicsSystem`
- Namespace: `_App.Ace_of_Ages.Terrain`
- Main-thread `SystemBase` (required for `MeshCollider.Create`)
- Manages `NativeHashMap<ColliderCacheKey, ColliderCacheEntry>` LRU cache
- Evicts cache entries when `maxColliderCacheMemoryMB` is exceeded

## Configuration Reference

### VR (Quest 3 / PC VR, recommended)
```
physicsColliderFullResolutionDistance: 128m
physicsColliderVertexStride:           2
maxColliderDistance:                   450m
maxCollidersCreatedPerFrame:           6
maxPhysicsCollidersCreatedPerFrame:    4
maxColliderCacheMemoryMB:              50
```

### Desktop (high-end, RTX 4080+)
```
physicsColliderFullResolutionDistance: 200m
physicsColliderVertexStride:           2
maxColliderDistance:                   600m
maxCollidersCreatedPerFrame:           12
maxPhysicsCollidersCreatedPerFrame:    8
maxColliderCacheMemoryMB:              100
```

### Mobile / Quest 2
```
physicsColliderFullResolutionDistance: 80m
physicsColliderVertexStride:           4
maxColliderDistance:                   300m
maxCollidersCreatedPerFrame:           4
maxPhysicsCollidersCreatedPerFrame:    2
maxColliderCacheMemoryMB:              25
```

## Profiler Markers

| Marker | System | What it covers |
|--------|--------|----------------|
| `TerrainPhysics.DistanceTracking` | TerrainDistanceTrackingSystem | Distance calc + prep marking |
| `TerrainPhysics.PrepareJob` | TerrainColliderPreparationSystem | Burst decimation job |
| `TerrainPhysics.ColliderCreation` | TerrainPhysicsSystem | MeshCollider.Create + cache |

## Common Issues

**Colliders not generating:**
- Check `enablePhysicsColliders = true` in TerrainConfigAuthoring
- Verify player tracking is working (`[PlayerTrackingInitSystem] ✅ Found player` in console)
- Confirm tiles have finished mesh generation (`meshGenerated = true`)

**Physics spikes:**
- Lower `maxPhysicsCollidersCreatedPerFrame` (try 2–3 for Quest 2/Pro)
- Reduce `physicsColliderFullResolutionDistance` to shrink the full-res zone
- Increase `physicsColliderVertexStride` to 4 for distant tiles

**Distant terrain has no collider:**
- Expected — tiles beyond `maxColliderDistance` have no physics
- Increase `maxColliderDistance` if gameplay requires it (costs more memory and creation time)

## Related Documentation

- **[Configuration Reference](CONFIGURATION.md)** — All `TerrainConfigAuthoring` parameters
- **[Technical Details](TECHNICAL_DETAILS.md)** — Noise algorithms, mesh generation details
- **[Performance Optimization](PERFORMANCE.md)** — Tuning strategies

---

**Back to:** [Documentation Hub](README.md)
