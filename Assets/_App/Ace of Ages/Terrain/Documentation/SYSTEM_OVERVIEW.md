# System Overview - Infinite Terrain System

High-level architecture guide for understanding how the terrain system works.

## What is This System?

The Infinite Terrain System generates procedural terrain tiles around a player in VR/desktop applications using Unity DOTS.

### Key Concepts

**Tile-Based**: World divided into square tiles (e.g., 100m × 100m)  
**Dynamic Spawning**: Tiles spawn as player moves, despawn when far away  
**ECS Architecture**: Built with Unity DOTS for maximum performance  
**Hybrid Design**: Tracks MonoBehaviour GameObjects from pure ECS subscenes

## System Components

### Configuration Layer
- `TerrainConfigAuthoring` - Main authoring component
- Bakes into singleton components (`TerrainTileConfig`, `ScrollConfig`, etc.)

### Initialization Layer
- `PlayerTrackingInitSystem` - Finds player GameObject at startup
- Populates `PlayerTransformReference` with Transform

### Simulation Layer
1. `ScrollTerrainSystem` - Updates scroll offset
2. `TileSpawningSystem` - Creates/destroys tiles
3. `TileScrollPositionSystem` - Applies scroll to positions
4. `TerrainMeshGenerationSystem` - Generates procedural meshes
5. `TerrainDistanceTrackingSystem` - Calculates LOD levels
6. `TerrainColliderPreparationSystem` - Prepares collider data
7. `TerrainPhysicsSystem` - Creates physics colliders

### Presentation Layer
- `TerrainRenderingSystem` - Creates Unity Meshes, sets up rendering

### Diagnostics
- `StaticObjectCleanupDebugSystem` - Orphan static-object warnings at runtime
- `TerrainStatusInspector` - Editor window for material, URP, and play-mode status

## Data Flow

```
Frame N:
  1. ScrollTerrainSystem → updates ScrollOffset
  2. TileSpawningSystem → creates/destroys tiles
  3. TileScrollPositionSystem → positions tiles
  4. TerrainMeshGenerationSystem → fills mesh buffers
  5. TerrainDistanceTrackingSystem → determines LOD
  6. TerrainColliderPreparationSystem → prepares collider data
  7. TerrainPhysicsSystem → creates colliders
  8. TerrainRenderingSystem → renders tiles
```

## Key Design Patterns

### Hybrid MonoBehaviour/ECS
- Player is GameObject (outside SubScene)
- Terrain is ECS (inside SubScene)
- `PlayerTransformReference` bridges the gap

### Frame Budgeting
- Process N items per frame (prevents spikes)
- Priority queue for important work first
- Smooth frame times for VR

### Camera-Aware Prioritization
- Visible tiles processed first
- Forward-facing tiles before backward
- Distance-based sorting

### Full-Resolution Physics
- All in-range tiles use full mesh resolution for colliders
- Distance culling via `maxColliderDistance`
- Cross-frame async BVH construction with frame budgeting

### Zero GC Allocation
- Use only NativeContainers
- Reinterpret buffers for zero-copy
- No managed allocations at runtime

## Performance Characteristics

**Memory**: ~50KB per tile (32×32 mesh)  
**CPU**: 5-10ms mesh generation per tile (budgeted)  
**Physics**: 2-5ms collider creation per tile (budgeted, full resolution)  
**Scalability**: Handles 25-100 tiles efficiently

## Related Documentation

- **[System Pipeline](SYSTEM_PIPELINE.md)** - Detailed execution flow
- **[Component Reference](COMPONENT_REFERENCE.md)** - All components
- **[System Reference](SYSTEM_REFERENCE.md)** - All systems
- **[Technical Details](TECHNICAL_DETAILS.md)** - Algorithm deep dive

---

**Back to**: [Documentation Hub](README.md)

