# Infinite Terrain System
**Version:** 3.0  
**Last Updated:** May 4, 2026

Production-ready Unity DOTS infinite terrain system with tree rendering, auto-scrolling, and VR optimization.

## Quick Links

📚 **[Complete Documentation](Documentation/README.md)** - Full documentation hub  
📖 **[Table of Contents](Documentation/TABLE_OF_CONTENTS.md)** - Organized document index  
🚀 **[Quick Start Guide](Documentation/QUICK_START.md)** - Get started in 10 minutes  
🌿 **[Static Object Spawning System](STATIC_OBJECT_SPAWNING_SYSTEM.md)** - Procedural object placement guide (trees, turrets, decorations)

---

## Features at a Glance

✅ **Infinite Procedural Terrain** - Tiles spawn/despawn as player moves  
✅ **Auto-Scrolling** - Optional endless runner mode  
✅ **Instanced Tree Rendering** - Thousands of trees with <2ms frame time  
✅ **VR Optimized** - Quest 3 performance targets (<11ms budget)  
✅ **LOD Physics** - Distance-based collider detail  
✅ **Zero GC** - No garbage collection during runtime  
✅ **Hybrid Architecture** - Works with GameObject players

---

## System Architecture (v3.0)

**20+ ECS Systems** organized in categories:

### Core Terrain (9 systems)
Tile lifecycle, mesh generation, physics, rendering

### Static Object Management (5 systems)
- Procedural spawning with bilinear interpolation
- BRG-instanced rendering with spatial culling
- Dynamic LOD with hysteresis
- 30-40% culling improvement (v3.0)

### Scroll Velocity (2 systems)
- Player rotation-based velocity
- Constant velocity vector
- Flexible scroll sources for gameplay

### Utilities (4 systems)
- Player tracking initialization
- Terrain anchor for moving entities
- World origin tracking (optional)
- Debug visualization tools

---

## Performance Targets

| Platform | Frame Budget | Trees | Draw Calls |
|----------|--------------|-------|------------|
| **Quest 3 VR** | <8ms | 1000+ | 2-5 |
| **Desktop RTX 3070** | <12ms | 2000+ | 5-10 |
| **Desktop RTX 4080** | <10ms | 5000+ | 10-20 |

**Optimization Highlights:**
- Burst-compiled parallel jobs for mesh generation
- Camera-aware prioritization
- Spatial grid culling (v3.0)
- Velocity-aware frame skipping (v3.0)
- LRU collider caching

---

## Quick Start

```
1. Add TerrainConfigAuthoring component to GameObject
2. Convert to SubScene (New SubScene From Selection)
3. Configure player tracking mode (AutoDetect recommended)
4. Press Play - terrain generates around player!
```

**Detailed Instructions:** [Quick Start Guide](Documentation/QUICK_START.md)

---

## Documentation Structure

```
Documentation/
├─ Getting Started
│  ├─ Quick Start, Configuration, Player Tracking
│
├─ Architecture
│  ├─ Overview, Pipeline, Technical Details, Architecture Diagrams
│
├─ System Reference
│  ├─ All 20+ systems with APIs and examples
│
├─ Features
│  ├─ Auto-Scrolling, Physics, Rendering, Tree System, Terrain Anchors
│
├─ Performance
│  ├─ Optimization Guide, History, Code Review
│
└─ Troubleshooting
   ├─ Common Issues, Debug Tools
```

**Full Index:** [Table of Contents](Documentation/TABLE_OF_CONTENTS.md)

---

## What's New in v3.0 (May 2026)

🔄 **Global Static Object Instanced Rendering**
- Spatial grid culling for 30-40% performance improvement
- Distance culling, frustum culling pipeline via BRG (Entities Graphics)
- Dynamic LOD with hysteresis

🔄 **Static Object LOD System**
- Dynamic mesh LOD with hysteresis (prevents flickering)
- Velocity-aware frame skipping for VR
- Spatial chunking for efficient batching

🔄 **Scroll Velocity Components**
- `PlayerScrollVelocitySystem` - Rotation-based scrolling
- `ConstantScrollVelocitySystem` - Fixed velocity vector
- Flexible architecture for gameplay variety

🔄 **Documentation Overhaul**
- Complete architecture diagrams (Mermaid)
- Consolidated optimization history
- Code quality review and standards

---

## Dependencies

- **Unity 6** (6000.3.10f1 or later)
- **Unity.Entities** 1.3.0+
- **Unity.Physics**
- **Unity.Rendering.Hybrid** (Entities Graphics)
- **Unity.Burst**
- **Unity.Mathematics**

---

## Common Use Cases

**Endless Runner Games**
- Enable auto-scrolling terrain
- Player stays stationary, world moves
- Perfect for VR (no motion sickness)

**Open World Exploration**
- Disable auto-scrolling
- Player moves freely
- Infinite procedural landscape

**Racing Games**
- Use `ConstantScrollVelocitySystem`
- Fixed scroll speed
- Spawn obstacles with `TerrainAnchorSystem`

**Flight Simulators**
- High scroll speeds (20-40 m/s)
- Distant terrain tiles
- LOD physics for performance

---

## Troubleshooting

**Terrain not spawning?**
→ Check player tracking in console: `[PlayerTrackingInitSystem] ✅ Found player`

**Terrain not visible?**
→ Verify material assigned in `TerrainConfigAuthoring.terrainMaterial`

**Performance issues?**
→ See [Performance Guide](Documentation/PERFORMANCE.md) for optimization checklist

**Trees not rendering?**
→ Check static object prefabs assigned in `StaticObjectSpawnerConfigAuthoring`

**Complete Guide:** [Troubleshooting](Documentation/TROUBLESHOOTING.md)

---

## Support

📚 **Documentation:** Start with [Documentation Hub](Documentation/README.md)  
🔍 **Search:** Use [Table of Contents](Documentation/TABLE_OF_CONTENTS.md)  
⚡ **Performance:** See [Performance Guide](Documentation/PERFORMANCE.md)  
🐛 **Debugging:** Check [Debug Tools](Documentation/DEBUG_TOOLS.md)

---

## License & Attribution

Part of the BlueDesertFox Unity VR project.  
Built with Unity DOTS for maximum performance.

**Author:** O8C Development Team  
**Maintained:** Active development (May 2026)

