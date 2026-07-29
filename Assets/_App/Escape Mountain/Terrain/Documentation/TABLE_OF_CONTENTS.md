# Terrain System — Table of Contents
**Version:** 3.0  
**Last Updated:** June 2026

Complete index of all Terrain system documentation.

---

## Getting Started

| Document | Description |
|----------|-------------|
| [README.md](README.md) | Documentation hub — start here |
| [QUICK_START.md](QUICK_START.md) | 10-minute setup guide |
| [CONFIGURATION.md](CONFIGURATION.md) | All `TerrainConfigAuthoring` parameters |
| [PLAYER_TRACKING.md](PLAYER_TRACKING.md) | Configuring player tracking modes |

---

## Architecture & Reference

| Document | Description |
|----------|-------------|
| [SYSTEM_OVERVIEW.md](SYSTEM_OVERVIEW.md) | High-level architecture and component relationships |
| [SYSTEM_REFERENCE.md](SYSTEM_REFERENCE.md) | Complete reference for all 20+ ECS systems |
| [SYSTEM_PIPELINE.md](SYSTEM_PIPELINE.md) | Frame-by-frame execution order and data flow |
| [COMPONENT_REFERENCE.md](COMPONENT_REFERENCE.md) | All ECS components with field descriptions |
| [API_REFERENCE.md](API_REFERENCE.md) | Public API, code examples, and usage patterns |
| [TECHNICAL_DETAILS.md](TECHNICAL_DETAILS.md) | Perlin noise, decimation algorithms, caching |

---

## Feature Documentation

| Document | Description |
|----------|-------------|
| [AUTO_SCROLLING.md](AUTO_SCROLLING.md) | Endless runner scroll velocity system |
| [PHYSICS_SYSTEM.md](PHYSICS_SYSTEM.md) | Distance-based collider generation with LRU cache |
| [RENDERING_SYSTEM.md](RENDERING_SYSTEM.md) | Tile mesh rendering with Entities Graphics |
| [STATIC_OBJECT_RENDERING.md](STATIC_OBJECT_RENDERING.md) | Static object instanced rendering, LOD, spatial culling |
| [TERRAIN_ANCHOR_SYSTEM.md](TERRAIN_ANCHOR_SYSTEM.md) | Scroll-anchored entity placement |

---

## Spawning & Objects

| Document | Description |
|----------|-------------|
| [../STATIC_OBJECT_SPAWNING_SYSTEM.md](../STATIC_OBJECT_SPAWNING_SYSTEM.md) | Procedural placement of trees, turrets, and decorations |
| [../TURRET_SYSTEM.md](../TURRET_SYSTEM.md) | Turret authoring, aiming, barrel, and shooter systems |
| [../PLAYER_SCROLL_VELOCITY.md](../PLAYER_SCROLL_VELOCITY.md) | Player-driven and constant scroll velocity providers |

---

## Debugging & Optimization

| Document | Description |
|----------|-------------|
| [DEBUG_TOOLS.md](DEBUG_TOOLS.md) | Terrain Status Inspector, profiler markers, console diagnostics |
| [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Solutions to common issues |
| [PERFORMANCE.md](PERFORMANCE.md) | Optimization guide and platform presets |
| [EXTENSIONS.md](EXTENSIONS.md) | Adding biomes, custom LOD, water, minimap |
| [INTEGRATION.md](INTEGRATION.md) | Integrating with player, AI, UI, save systems |

---

## Source Files Quick Reference

### Core Terrain
| File | Purpose |
|------|---------|
| `TerrainConfigAuthoring.cs` | Main authoring/baker — all config |
| `TileComponents.cs` | All terrain + static object ECS types |
| `TerrainPhysicsComponents.cs` | Physics collider types + budget helper |
| `PlayerTrackingInitSystem.cs` | Runtime player GO search |
| `ScrollTerrainSystem.cs` | Accumulates `ScrollOffset` |
| `TileSpawningSystem.cs` | Ring spawn/despawn, static object cleanup |
| `TileScrollPositionSystem.cs` | Parallel tile position update (Burst) |
| `TerrainMeshGenerationSystem.cs` | Burst parallel Perlin mesh generation |
| `TerrainDistanceTrackingSystem.cs` | Distance tracking + collider prep marking |
| `TerrainColliderPreparationSystem.cs` | Burst vertex decimation job |
| `TerrainPhysicsSystem.cs` | Main-thread MeshCollider.Create + LRU cache |
| `TerrainRenderingSystem.cs` | ECS buffers → Unity Mesh + Entities Graphics |

### Static Objects
| File | Purpose |
|------|---------|
| `StaticObjectSpawnerConfigAuthoring.cs` | Static object spawn config baker |
| `TerrainStaticObjectSpawningSystemOptimized.cs` | Burst static object spawner (active) |
| `StaticObjectPositionUpdateSystem.cs` | Position update when tiles scroll |
| `StaticObjectSpatialChunkingSystem.cs` | 100m chunk assignment for culling |
| `StaticObjectLODUpdateSystem.cs` | Distance LOD with hysteresis + VR frame skip |
| `StaticObjectLODMeshInfoInitSystem.cs` | One-shot BRG MaterialMeshInfo init |
| `StaticObjectLinkedRendererStripSystem.cs` | Post-instantiate hierarchy flatten |

### Turrets & Scroll
| File | Purpose |
|------|---------|
| `TurretDomeAuthoring.cs` | Turret dome config baker |
| `TurretBarrelAuthoring.cs` | Barrel pitch config baker |
| `TurretShooterAuthoring.cs` | Burst fire + LOS config baker |
| `TurretAimingSystem.cs` | Ballistic intercept, dome Y rotation |
| `TurretBarrelSystem.cs` | Barrel pitch toward intercept |
| `TurretShooterSystem.cs` | Burst fire with LOS raycast |
| `PlayerTargetVelocityEstimateSystem.cs` | Smoothed player velocity for intercept |
| `PlayerScrollVelocitySystem.cs` | Player rotation → scroll velocity |
| `PlayerScrollVelocityAuthoring.cs` | Player scroll config baker |
| `ConstantScrollVelocitySystem.cs` | Fixed scroll for testing |
| `WorldOriginTrackingInitSystem.cs` | Optional world origin GO tracking |

### Diagnostics & Editor
| File | Purpose |
|------|---------|
| `StaticObjectCleanupDebugSystem.cs` | Orphan static-object detection (`LogWarning` every 2s) |
| `Editor/TerrainStatusInspector.cs` | Window → Terrain → Status Inspector |
| `Editor/TerrainMaterialCreator.cs` | Auto-creates Resources/TerrainMaterial |
| `Editor/SetupTerrainPhysicsLayers.cs` | Tools → Terrain → Setup Physics Layer |

---

**Back to:** [Documentation Hub](README.md)
