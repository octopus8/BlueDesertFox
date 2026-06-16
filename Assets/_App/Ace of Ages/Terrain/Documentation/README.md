# Infinite Terrain System - Documentation Hub
**Version:** 3.0  
**Last Updated:** May 4, 2026

Welcome to the comprehensive documentation for the DOTS-based infinite terrain system. This system provides high-performance procedural terrain generation with instanced tree rendering optimized for VR applications.

---

## 🚀 Quick Start
**New to the system?** Start here:
1. **[Quick Start Guide](QUICK_START.md)** - Get terrain running in 10 minutes
2. **[System Overview](SYSTEM_OVERVIEW.md)** - Understand how the system works
3. **[Common Issues](TROUBLESHOOTING.md)** - Solve problems quickly

**Complete Document Index:** [Table of Contents](TABLE_OF_CONTENTS.md) *(see below)*

---

## 📚 Documentation Index

### Getting Started
- **[Quick Start Guide](QUICK_START.md)** - Step-by-step setup instructions for beginners
- **[Configuration Reference](CONFIGURATION.md)** - Complete guide to all terrain settings
- **[Player Tracking Setup](PLAYER_TRACKING.md)** - How to configure player tracking for terrain centering

### Understanding the System
- **[System Overview](SYSTEM_OVERVIEW.md)** - High-level architecture and component relationships
- **[System Pipeline](SYSTEM_PIPELINE.md)** - Detailed execution order and data flow
- **[Technical Details](TECHNICAL_DETAILS.md)** - Deep dive into algorithms and implementation

### Feature Documentation
- **[Auto-Scrolling Terrain](AUTO_SCROLLING.md)** - Scroll velocity components and configuration
- **[Physics System](PHYSICS_SYSTEM.md)** - LOD-based physics collider system
- **[Rendering System](RENDERING_SYSTEM.md)** - How mesh rendering works with Entities Graphics
- **[Static Object Rendering System](STATIC_OBJECT_RENDERING.md)** - Instanced rendering with spatial culling and LOD
- **[Terrain Anchor System](TERRAIN_ANCHOR_SYSTEM.md)** - Spawn objects that move with scrolling terrain

### Reference
- **[System Reference](SYSTEM_REFERENCE.md)** - All 20+ systems with APIs
- **[Component Reference](COMPONENT_REFERENCE.md)** - All components with detailed explanations
- **[API Reference](API_REFERENCE.md)** - Complete component and system API documentation

### Troubleshooting & Debugging
- **[Troubleshooting Guide](TROUBLESHOOTING.md)** - Solutions to common problems
- **[Debug Tools](DEBUG_TOOLS.md)** - Using TerrainTrackingDebugger and visualization tools
- **[Performance Optimization](PERFORMANCE.md)** - Tuning for maximum performance

### Advanced Topics
- **[Extension Guide](EXTENSIONS.md)** - How to add custom features (biomes, LOD, modifications)
- **[Integration Guide](INTEGRATION.md)** - Integrating with other systems in your project

---

## ⭐ What is this System?

The Infinite Terrain System v3.0 is a production-ready Unity DOTS implementation that provides:

### Core Features
✅ **Infinite Procedural Terrain** - Tiles spawn/despawn automatically as player moves  
✅ **Auto-Scrolling** - Optional endless runner mode with flexible velocity sources  
✅ **Instanced Tree Rendering** - Thousands of trees with spatial culling (v3.0)  
✅ **Dynamic Tree LOD** - Distance-based mesh switching with hysteresis (v3.0)  
✅ **VR Optimized** - Quest 3 performance targets (<11ms budget)  
✅ **High Performance** - Burst-compiled ECS systems, zero GC allocations  
✅ **Physics Ready** - Automatic mesh collider generation with LOD support  
✅ **Flexible Player Tracking** - Works with any GameObject (VR rig, camera, etc.)

### Performance Characteristics (v3.0)
- **Tile Generation**: ~5-10ms per tile (configurable budget)
- **Tree Rendering**: <2ms for 1000+ trees (Quest 3)
- **Tree LOD Updates**: <0.5ms (velocity-aware throttling)
- **Physics Collider Creation**: Budget-limited to prevent frame spikes
- **Memory Usage**: Collider cache with LRU eviction (~53MB typical)
- **Zero GC Allocations**: No managed memory allocations during runtime

---

## 🏗️ System Architecture Overview

**20+ Systems** organized in clear execution pipeline. **[Complete Architecture Diagram](ARCHITECTURE.md)**

**System Categories:**
- **Core Terrain (9)** - Tile lifecycle, mesh generation, physics
- **Tree Management (5)** - Spawning, LOD, spatial chunking, instanced rendering
- **Scroll Velocity (2)** - Player-based and constant velocity sources
- **Utilities (4)** - Player tracking, anchors, debug tools

**Execution Flow:** Init → Scroll Velocity → Terrain Core → Physics → Trees → Presentation

---

## 🎯 Quick Configuration Reference
### Basic Terrain Settings
```
Tile Size:           100m      // Size of each terrain chunk
View Distance:       500m      // Render distance
Vertices Per Side:   32        // Mesh resolution (32x32 = 1024 vertices)
```
### Noise Settings
```
Noise Frequency:     0.01f     // Base terrain variation (lower = smoother)
Noise Amplitude:     20f       // Maximum height (in meters)
Noise Octaves:       4         // Detail layers (more = more detail)
Noise Lacunarity:    2.0f      // Frequency multiplier per octave
Noise Persistence:   0.5f      // Amplitude multiplier per octave
```
### Auto-Scrolling Settings
```
Scroll Enabled:      false     // Enable automatic terrain scrolling
Scroll Speed:        5.0f      // Speed in m/s (positive = forward)
```
### Physics Settings
```
// Mesh-prep jobs per frame (Burst)
maxCollidersCreatedPerFrame:          6
// Main-thread MeshCollider.Create calls per frame (keep 3–4 for VR)
maxPhysicsCollidersCreatedPerFrame:   4
// Full-resolution zone radius (beyond this, vertex stride is applied)
physicsColliderFullResolutionDistance: 128m
// Vertex stride beyond full-res zone (2 = every 2nd vertex, ~4x fewer triangles)
physicsColliderVertexStride:          2
// Tiles beyond this distance have no collider at all
maxColliderDistance:                  450m
// Memory cap for collider cache (LRU eviction when exceeded)
maxColliderCacheMemoryMB:             50MB
```

### Tree Rendering Settings (v3.0)
```
Max Render Distance:       200m   // Tree culling distance
LOD 0 Distance:            50m    // High detail
LOD 1 Distance:            100m   // Medium detail
LOD 2 Distance:            200m   // Low detail/billboard
Spatial Grid Cell Size:    100m   // Chunk size for culling
```

---

## 🔥 Having Issues?
1. **Terrain not spawning?** → [Troubleshooting - No Tiles](TROUBLESHOOTING.md#no-tiles-spawning)
2. **Terrain not visible?** → [Troubleshooting - Not Rendering](TROUBLESHOOTING.md#terrain-not-rendering)
3. **Player tracking fails?** → [Player Tracking Setup](PLAYER_TRACKING.md)
4. **Performance issues?** → [Performance Optimization](PERFORMANCE.md)
5. **Physics problems?** → [Physics System](PHYSICS_SYSTEM.md)
6. **Static objects not rendering?** → [Static Object Rendering System](STATIC_OBJECT_RENDERING.md)

**Debug Tools Available**:
- `TerrainTrackingDebugger` - Player tracking and tile status
- `TerrainTileGizmoVisualizer` - Visual tile debugging in Scene view
- `TreeLODDebugSystem` - LOD level visualization
- `TreeCleanupDebugSystem` - Tree lifecycle validation

---

## 💡 Technical Highlights

### Zero GC Allocation Design
- All systems use `NativeContainer` types (no managed collections)
- Burst-compiled jobs for mesh generation and collider preparation
- `Reinterpret<T>().AsNativeArray()` pattern for zero-copy buffer access
- Entity queries with stack-allocated processing

### Camera-Aware Prioritization
- Tiles in camera view frustum processed first
- Forward-facing tiles prioritized over backward tiles
- Distance-based sorting for generation order

### Physics System
- Full resolution within configurable distance (128m default), reduced resolution beyond
- Configurable vertex stride (default 2 = every 2nd vertex) for distant tiles
- Cached collider data (BlobAsset) with LRU eviction
- Split two-stage budget: Burst prep jobs + main-thread MeshCollider.Create caps
- Optional physics layer separation for terrain tiles

### Hybrid MonoBehaviour/ECS Design
- Player tracking via managed `PlayerTransformReference` component
- Compatible with any GameObject (VR rigs, cameras, etc.)
- Runtime initialization system for cross-scene references
- Material management via MonoBehaviour systems

### Static Object Rendering Architecture (v3.0)
- Entities Graphics (BatchRendererGroup) for maximum batching
- Three-stage culling (spatial → distance → frustum)
- Dynamic LOD with hysteresis to prevent flickering
- Hierarchy flattening post-instantiate for optimal ECS layout

---

## 📁 File Structure
```
Terrain/
├─ Documentation/           ← You are here!
│  ├─ README.md                     (This file - documentation hub)
│  ├─ TABLE_OF_CONTENTS.md          (Complete document index)
│  ├─ QUICK_START.md
│  ├─ SYSTEM_OVERVIEW.md
│  ├─ STATIC_OBJECT_RENDERING.md    (v3.0 - instanced rendering + LOD)
│  ├─ TERRAIN_ANCHOR_SYSTEM.md      (v3.0 - scroll-anchored entities)
│  └─ ... (all documentation)
│
├─ README.md                (Quick reference)
├─ STATIC_OBJECT_SPAWNING_SYSTEM.md  (Static object spawning guide)
│
├─ Systems (C# files):
│  ├─ TerrainConfigAuthoring.cs       (Main authoring component)
│  ├─ TileComponents.cs               (Component definitions)
│  ├─ TerrainPhysicsComponents.cs     (Physics-specific components)
│  │
│  ├─ Core Systems:
│  │  ├─ PlayerTrackingInitSystem.cs
│  │  ├─ ScrollTerrainSystem.cs
│  │  ├─ TileSpawningSystem.cs
│  │  ├─ TileScrollPositionSystem.cs
│  │  ├─ TerrainMeshGenerationSystem.cs
│  │  ├─ TerrainDistanceTrackingSystem.cs
│  │  ├─ TerrainColliderPreparationSystem.cs
│  │  ├─ TerrainPhysicsSystem.cs
│  │  └─ TerrainRenderingSystem.cs
│  │
│  ├─ Static Object Systems:
│  │  ├─ TerrainStaticObjectSpawningSystemOptimized.cs
│  │  ├─ StaticObjectSpatialChunkingSystem.cs
│  │  ├─ StaticObjectPositionUpdateSystem.cs
│  │  ├─ StaticObjectLODUpdateSystem.cs
│  │  └─ StaticObjectLODMeshInfoInitSystem.cs
│  │
│  ├─ Scroll Velocity:
│  │  ├─ PlayerScrollVelocitySystem.cs
│  │  └─ ConstantScrollVelocitySystem.cs
│  │
│  └─ Debug Tools:
│     ├─ TerrainTrackingDebugger.cs
│     ├─ TerrainTileGizmoVisualizer.cs
│     └─ TerrainRenderingDebugSystem.cs
```

---

---

**Ready to get started?** → [Quick Start Guide](QUICK_START.md)
