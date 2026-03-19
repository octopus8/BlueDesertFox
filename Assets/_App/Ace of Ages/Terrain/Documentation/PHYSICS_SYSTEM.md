# Physics System - LOD-Based Collider Generation

Complete guide to the terrain physics collider system with LOD, caching, and frame budgeting.

## Overview

The physics system automatically generates Unity Physics mesh colliders for terrain tiles with:
- **3-level LOD system** based on distance to player
- **LRU caching** for collider reuse across tiles
- **Frame budgeting** to prevent creation spikes
- **Camera-aware prioritization** for visible tiles first

## LOD System

### Three LOD Levels

**Full Resolution** (0 - 150m): Use all vertices (100%)  
**Half Resolution** (150m - 300m): Use every 2nd vertex (25%)  
**Quarter Resolution** (300m - 450m): Use every 4th vertex (6.25%)  
**No Collider** (> 450m): No physics

### Configuration

Set in TerrainConfigAuthoring:
```
LOD Full Resolution Distance:  150m
LOD Half Resolution Distance:  300m
LOD Quarter Resolution Distance: 450m
```

## Collider Caching

### Why Cache?

All tiles with same configuration generate identical collider shapes. Cache once, reuse everywhere.

### Cache Performance

**Cache Hit**: ~0.1ms (instant reuse)  
**Cache Miss**: 2-5ms (create new collider)  
**Hit Rate**: >90% typical

### Memory Management

**Default Limit**: 50MB  
**Eviction**: LRU (Least Recently Used)  
**Tracking**: Per-frame access timestamps

## Frame Budgeting

**Max Colliders Per Frame**: 3 (default for VR)

Prevents frame spikes by spreading collider creation across multiple frames.

**Priority Sorting**: Closer + forward-facing tiles processed first.

## Systems

### TerrainDistanceTrackingSystem
- Calculates distance to player
- Determines LOD level
- Marks tiles for collider preparation

### TerrainColliderPreparationSystem
- Burst-compiled parallel jobs
- Decimates vertices by LOD level
- Fills prepared buffers

### TerrainPhysicsSystem
- Creates MeshColliders (main thread)
- Manages cache with LRU eviction
- Frame budgeting

## Configuration

**Performance** (VR):
```
Max Colliders Per Frame: 2-3
Full Res Distance: 100m
Cache Memory: 25-50MB
```

**Quality** (Desktop):
```
Max Colliders Per Frame: 10
Full Res Distance: 200m
Cache Memory: 100MB
```

## Related Documentation

- **[Configuration Reference](CONFIGURATION.md)** - Physics parameters
- **[Technical Details](TECHNICAL_DETAILS.md)** - LOD algorithms
- **[Performance Optimization](PERFORMANCE.md)** - Optimization strategies

---

**Back to**: [Documentation Hub](README.md)

