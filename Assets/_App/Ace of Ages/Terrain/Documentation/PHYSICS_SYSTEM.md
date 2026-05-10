# Physics System - Full-Resolution Collider Generation

Complete guide to the terrain physics collider system with caching and frame budgeting.

## Overview

The physics system automatically generates Unity Physics mesh colliders for terrain tiles with:
- **Full-resolution geometry** matching rendered mesh exactly
- **LRU caching** for collider reuse across tiles with identical parameters
- **Frame budgeting** to prevent creation spikes
- **Camera-aware prioritization** for visible tiles first
- **Distance culling** to remove colliders beyond visibility distance

## Collider Resolution

### Single Resolution Level

All terrain colliders use **full-resolution geometry** - the exact same vertex data used for rendering. This ensures perfect consistency between visual and physical terrain.

**Benefits**:
- Perfect physics accuracy (no mismatch with visuals)
- Simplified caching (all tiles with same noise params share one cached collider)
- Reduced code complexity

**Trade-off**: Higher memory usage for distant tiles compared to LOD decimation

### Distance Culling

**Max Collider Distance**: 450m (default)

Tiles beyond this distance have no physics collider at all. Prevents unnecessary collision checks for terrain far from the player.

Set in TerrainConfigAuthoring:
```
Max Collider Distance: 450m
```

## Collider Caching

### Why Cache?

All tiles with identical noise parameters generate identical collider shapes. Cache once, reuse everywhere.

### Cache Performance

**Cache Hit**: ~0.1ms (instant reuse)  
**Cache Miss**: 3-6ms (create full-resolution collider)  
**Hit Rate**: >95% typical (improved from previous LOD system)

### Cache Efficiency Improvement

With single resolution, cache efficiency is dramatically improved:
- Previously: Cache differentiated by (noise params + LOD level)
- Now: Cache differentiated only by noise params
- Result: All tiles with same noise config share single cache entry

### Memory Management

**Default Limit**: 50MB  
**Eviction**: LRU (Least Recently Used)  
**Tracking**: Per-frame access timestamps

## Frame Budgeting

**Max Colliders Per Frame**: 6 (default for VR, increased from 3)

Prevents frame spikes by spreading collider creation across multiple frames.

**Priority Sorting**: Closer + forward-facing tiles processed first.

**Budget Increase**: Higher budget compensates for full-resolution collider creation time.

## Systems

### TerrainDistanceTrackingSystem
- Calculates distance to player
- Determines if tile needs collider (within max distance)
- Marks tiles for collider preparation or removal

### TerrainColliderPreparationSystem
- Burst-compiled parallel jobs  
- Copies full vertex/triangle data without decimation
- Calculates camera-aware priority

### TerrainPhysicsSystem
- Creates MeshColliders (main thread)
- Manages cache with LRU eviction
- Frame budgeting

## Configuration

**Performance** (VR):
```
Max Colliders Per Frame: 4-6
Max Collider Distance: 400m
Cache Memory: 50MB
```

**Quality** (Desktop):
```
Max Colliders Per Frame: 12-15
Max Collider Distance: 600m
Cache Memory: 100MB
```

## Related Documentation

- **[Configuration Reference](CONFIGURATION.md)** - Physics parameters
- **[Technical Details](TECHNICAL_DETAILS.md)** - LOD algorithms
- **[Performance Optimization](PERFORMANCE.md)** - Optimization strategies

---

**Back to**: [Documentation Hub](README.md)

