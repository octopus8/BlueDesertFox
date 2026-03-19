# Infinite Terrain System - Documentation Hub
Welcome to the comprehensive documentation for the DOTS-based infinite terrain system. This system provides high-performance procedural terrain generation optimized for VR applications.
## ?? Quick Start
**New to the system?** Start here:
1. **[Quick Start Guide](QUICK_START.md)** - Get terrain running in 10 minutes
2. **[System Overview](SYSTEM_OVERVIEW.md)** - Understand how the system works
3. **[Common Issues](TROUBLESHOOTING.md)** - Solve problems quickly
## ?? Documentation Index
### Getting Started
- **[Quick Start Guide](QUICK_START.md)** - Step-by-step setup instructions for beginners
- **[Configuration Reference](CONFIGURATION.md)** - Complete guide to all terrain settings
- **[Player Tracking Setup](PLAYER_TRACKING.md)** - How to configure player tracking for terrain centering
### Understanding the System
- **[System Overview](SYSTEM_OVERVIEW.md)** - High-level architecture and component relationships
- **[System Pipeline](SYSTEM_PIPELINE.md)** - Detailed execution order and data flow
- **[Technical Details](TECHNICAL_DETAILS.md)** - Deep dive into algorithms and implementation
### Feature Documentation
- **[Auto-Scrolling Terrain](AUTO_SCROLLING.md)** - Complete guide to the scrolling terrain feature
- **[Physics System](PHYSICS_SYSTEM.md)** - LOD-based physics collider system
- **[Rendering System](RENDERING_SYSTEM.md)** - How mesh rendering works with Entities Graphics
### Reference
- **[API Reference](API_REFERENCE.md)** - Complete component and system API documentation
- **[Component Reference](COMPONENT_REFERENCE.md)** - All components with detailed explanations
- **[System Reference](SYSTEM_REFERENCE.md)** - All systems with update order and dependencies
### Troubleshooting & Debugging
- **[Troubleshooting Guide](TROUBLESHOOTING.md)** - Solutions to common problems
- **[Debug Tools](DEBUG_TOOLS.md)** - Using TerrainTrackingDebugger and visualization tools
- **[Performance Optimization](PERFORMANCE.md)** - Tuning for maximum performance
### Advanced Topics
- **[Extension Guide](EXTENSIONS.md)** - How to add custom features (biomes, LOD, modifications)
- **[Integration Guide](INTEGRATION.md)** - Integrating with other systems in your project
## ?? What is this System?
The Infinite Terrain System is a production-ready Unity DOTS implementation that provides:
### Core Features
? **Infinite Procedural Terrain** - Tiles spawn/despawn automatically as player moves  
? **Auto-Scrolling** - Optional endless runner mode with directional scrolling  
? **VR Optimized** - Low overhead, no motion sickness from smooth scrolling  
? **High Performance** - Burst-compiled ECS systems, zero GC allocations  
? **Physics Ready** - Automatic mesh collider generation with LOD support  
? **Flexible Player Tracking** - Works with any GameObject (VR rig, camera, etc.)
### Performance Characteristics
- **Tile Generation**: ~5-10ms per tile (configurable budget)
- **Physics Collider Creation**: Budget-limited to prevent frame spikes
- **Memory Usage**: Collider cache with LRU eviction
- **Zero GC Allocations**: No managed memory allocations during runtime
## ??? System Architecture Overview
```
+-------------------------------------------------------------+
¦                    Initialization Phase                      ¦
+-------------------------------------------------------------¦
¦ PlayerTrackingInitSystem                                    ¦
¦ +- Finds player GameObject and stores Transform reference   ¦
+-------------------------------------------------------------+
+-------------------------------------------------------------+
¦                    Simulation Phase (Update Order)           ¦
+-------------------------------------------------------------¦
¦ 1. ScrollTerrainSystem                                      ¦
¦    +- Updates scroll offset for auto-scrolling terrain      ¦
¦                                                              ¦
¦ 2. TileSpawningSystem                                       ¦
¦    +- Creates/destroys tiles based on player distance       ¦
¦                                                              ¦
¦ 3. TileScrollPositionSystem                                 ¦
¦    +- Applies scroll offset to tile positions               ¦
¦                                                              ¦
¦ 4. TerrainMeshGenerationSystem                              ¦
¦    +- Generates procedural meshes with Perlin noise         ¦
¦                                                              ¦
¦ 5. TerrainDistanceTrackingSystem                            ¦
¦    +- Calculates tile distances and LOD levels              ¦
¦                                                              ¦
¦ 6. TerrainColliderPreparationSystem                         ¦
¦    +- Prepares collider data with LOD decimation (Burst)    ¦
¦                                                              ¦
¦ 7. TerrainPhysicsSystem                                     ¦
¦    +- Creates Unity Physics colliders with caching          ¦
+-------------------------------------------------------------+
+-------------------------------------------------------------+
¦                    Presentation Phase                        ¦
+-------------------------------------------------------------¦
¦ TerrainRenderingSystem                                      ¦
¦ +- Creates Unity Meshes and sets up Entities Graphics       ¦
+-------------------------------------------------------------+
```
## ?? Quick Configuration Reference
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
### Physics LOD Settings
```
Max Colliders/Frame:       3      // Budget limit to prevent frame spikes
Full Resolution Distance:  150m   // Use all vertices for collider
Half Resolution Distance:  300m   // Use every 2nd vertex
Quarter Resolution Distance: 450m // Use every 4th vertex
Max Collider Cache:        50MB   // Memory limit for cached colliders
```
## ?? Having Issues?
1. **Terrain not spawning?** ? [Troubleshooting - No Tiles](TROUBLESHOOTING.md#no-tiles-spawning)
2. **Terrain not visible?** ? [Troubleshooting - Not Rendering](TROUBLESHOOTING.md#terrain-not-rendering)
3. **Player tracking fails?** ? [Player Tracking Setup](PLAYER_TRACKING.md)
4. **Performance issues?** ? [Performance Optimization](PERFORMANCE.md)
5. **Physics problems?** ? [Physics System](PHYSICS_SYSTEM.md)
**Debug Tools Available**:
- `TerrainTrackingDebugger` - Player tracking and tile status
- `TerrainTileGizmoVisualizer` - Visual tile debugging in Scene view
## ?? Technical Highlights
### Zero GC Allocation Design
- All systems use `NativeContainer` types (no managed collections)
- Burst-compiled jobs for mesh generation and collider preparation
- `Reinterpret<T>().AsNativeArray()` pattern for zero-copy buffer access
- Entity queries with stack-allocated processing
### Camera-Aware Prioritization
- Tiles in camera view frustum processed first
- Forward-facing tiles prioritized over backward tiles
- Distance-based sorting for generation order
### Physics LOD System
- Three LOD levels based on distance (full, half, quarter resolution)
- Cached collider data with LRU eviction
- Frame budget system prevents spikes
- Optional physics layer separation for distant tiles
### Hybrid MonoBehaviour/ECS Design
- Player tracking via managed `PlayerTransformReference` component
- Compatible with any GameObject (VR rigs, cameras, etc.)
- Runtime initialization system for cross-scene references
- Material management via MonoBehaviour systems
## ?? File Structure
```
Terrain/
+- Documentation/           ? You are here!
¦  +- README.md             (This file)
¦  +- QUICK_START.md
¦  +- SYSTEM_OVERVIEW.md
¦  +- ... (all documentation)
¦
+- TerrainConfigAuthoring.cs       (Main authoring component)
+- TileComponents.cs               (Component definitions)
+- TerrainPhysicsComponents.cs     (Physics-specific components)
¦
+- Systems:
¦  +- PlayerTrackingInitSystem.cs
¦  +- ScrollTerrainSystem.cs
¦  +- TileSpawningSystem.cs
¦  +- TileScrollPositionSystem.cs
¦  +- TerrainMeshGenerationSystem.cs
¦  +- TerrainDistanceTrackingSystem.cs
¦  +- TerrainColliderPreparationSystem.cs
¦  +- TerrainPhysicsSystem.cs
¦  +- TerrainRenderingSystem.cs
¦
+- Debug Tools:
   +- TerrainTrackingDebugger.cs
   +- TerrainTileGizmoVisualizer.cs
   +- TerrainRenderingDebugSystem.cs
```
## ?? Version Information
**Current Version**: 2.0  
**Unity Version**: Unity 6 (6000.3.10f1)  
**Dependencies**:
- Unity.Entities (1.3.0+)
- Unity.Physics
- Unity.Rendering.Hybrid (Entities Graphics)
- Unity.Burst
- Unity.Mathematics
**Last Updated**: March 2026
---
**Ready to get started?** ? [Quick Start Guide](QUICK_START.md)
