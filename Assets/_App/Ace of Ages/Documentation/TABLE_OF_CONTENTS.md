# Ace of Ages — Documentation Table of Contents
**Last Updated:** June 2026

Complete index of all documentation across the Ace of Ages project.

---

## Project Overview

| Document | Description |
|----------|-------------|
| [Scene Overview](SCENE_OVERVIEW.md) | Scene architecture, execution order, singletons, and entry point |

---

## Terrain System

**Hub:** [Documentation Hub](../Terrain/Documentation/README.md) · [Table of Contents](../Terrain/Documentation/TABLE_OF_CONTENTS.md) · [Quick Reference](../Terrain/README.md)

### Getting Started
| Document | Description |
|----------|-------------|
| [Quick Start Guide](../Terrain/Documentation/QUICK_START.md) | Get terrain running in 10 minutes |
| [Configuration Reference](../Terrain/Documentation/CONFIGURATION.md) | All `TerrainConfigAuthoring` parameters |
| [Player Tracking Setup](../Terrain/Documentation/PLAYER_TRACKING.md) | Configure player tracking modes |

### Architecture & Reference
| Document | Description |
|----------|-------------|
| [System Overview](../Terrain/Documentation/SYSTEM_OVERVIEW.md) | High-level architecture and component relationships |
| [System Reference](../Terrain/Documentation/SYSTEM_REFERENCE.md) | All 20+ ECS systems with APIs |
| [System Pipeline](../Terrain/Documentation/SYSTEM_PIPELINE.md) | Frame-by-frame execution order and data flow |
| [Component Reference](../Terrain/Documentation/COMPONENT_REFERENCE.md) | All ECS components with field descriptions |
| [API Reference](../Terrain/Documentation/API_REFERENCE.md) | Public API, code examples, and usage patterns |
| [Technical Details](../Terrain/Documentation/TECHNICAL_DETAILS.md) | Perlin noise, decimation algorithms, caching |

### Features
| Document | Description |
|----------|-------------|
| [Auto-Scrolling Terrain](../Terrain/Documentation/AUTO_SCROLLING.md) | Endless runner scroll velocity system |
| [Physics System](../Terrain/Documentation/PHYSICS_SYSTEM.md) | Distance-based collider generation with LRU cache |
| [Rendering System](../Terrain/Documentation/RENDERING_SYSTEM.md) | Tile mesh rendering with Entities Graphics |
| [Static Object Rendering](../Terrain/Documentation/STATIC_OBJECT_RENDERING.md) | Instanced rendering with spatial culling and LOD |
| [Terrain Anchor System](../Terrain/Documentation/TERRAIN_ANCHOR_SYSTEM.md) | Spawn objects that move with scrolling terrain |
| [Static Object Spawning](../Terrain/STATIC_OBJECT_SPAWNING_SYSTEM.md) | Procedural placement of trees, turrets, and decorations |
| [Turret System](../Terrain/TURRET_SYSTEM.md) | Turret authoring, aiming, barrel, and shooter systems |
| [Player Scroll Velocity](../Terrain/PLAYER_SCROLL_VELOCITY.md) | Player-driven and constant scroll velocity providers |

### Debugging & Optimization
| Document | Description |
|----------|-------------|
| [Debug Tools](../Terrain/Documentation/DEBUG_TOOLS.md) | TerrainTrackingDebugger, gizmos, profiler markers |
| [Troubleshooting Guide](../Terrain/Documentation/TROUBLESHOOTING.md) | Solutions to common issues |
| [Performance Optimization](../Terrain/Documentation/PERFORMANCE.md) | Tuning guide and platform presets |
| [Extension Guide](../Terrain/Documentation/EXTENSIONS.md) | Adding biomes, custom LOD, water, minimap |
| [Integration Guide](../Terrain/Documentation/INTEGRATION.md) | Integrating with player, AI, UI, and save systems |

---

## TransformFollower System

**Hub:** [README / Start Here](../TransformFollower/Documentation/README_START_HERE.md)

| Document | Description |
|----------|-------------|
| [Quick Start Guide](../TransformFollower/Documentation/QUICKSTART.md) | 5-minute setup |
| [Testing Guide](../TransformFollower/Documentation/TESTING_GUIDE.md) | Test scenarios and debugging checklist |
| [Architecture Diagrams](../TransformFollower/Documentation/ARCHITECTURE.md) | Visual component relationships and data flow |
| [Full Technical Reference](../TransformFollower/Documentation/TransformFollowerREADME.md) | Fundamental limitation, approaches, alternatives |
| [Implementation Summary](../TransformFollower/Documentation/IMPLEMENTATION_SUMMARY.md) | Performance characteristics and alternative approaches |

---

## Enemy Spawner System

**Hub:** [README](../EnemySpawner/README.md)

| Document | Description |
|----------|-------------|
| [Quick Setup Guide](../EnemySpawner/QUICK_SETUP_GUIDE.md) | Step-by-step setup instructions |
| [Formation Approach System](../EnemySpawner/FORMATION_APPROACH_SYSTEM.md) | Full state machine reference — components, config, debugging |
| [Bowling Pin Formation](../EnemySpawner/BOWLING_PIN_FORMATION.md) | 10-pin hexagonal spawn layout |
| [Spawn Positioning Diagram](../EnemySpawner/SPAWN_POSITIONING_DIAGRAM.md) | Visual reference for spawn positions |
| [Formation Visual Diagram](../EnemySpawner/FORMATION_VISUAL_DIAGRAM.md) | Bowling pin formation diagrams |

### Archive
| Document | Description |
|----------|-------------|
| [Archive Index](../EnemySpawner/Archive/README.md) | Overview of archived development documents |
| [Formation Approach Implementation](../EnemySpawner/FORMATION_APPROACH_IMPLEMENTATION.md) | *(archived)* Full implementation diary — components, systems, config, rollback |
| [Formation Implementation Summary](../EnemySpawner/FORMATION_IMPLEMENTATION_SUMMARY.md) | *(archived)* Bowling pin formation session summary |

---

## Shooting System

**Hub:** [README](../Shooting/SHOOTING_SYSTEM_README.md)

| Document | Description |
|----------|-------------|
| [Quick Setup Guide](../Shooting/QUICK_SETUP_GUIDE.md) | Step-by-step shooting system setup |
| [Entity Transform Offset — Quick Reference](../Shooting/ENTITY_OFFSET_QUICK_REF.md) | Bullet spawn point offset cheat sheet |
| [Entity Transform Offset — Implementation](../Shooting/ENTITY_TRANSFORM_OFFSET_IMPLEMENTATION.md) | Bullet spawn point offset full implementation |

### Archive
| Document | Description |
|----------|-------------|
| [Archive Index](../Shooting/Archive/README.md) | Overview of archived patch notes |
| [Implementation Summary](../Shooting/IMPLEMENTATION_SUMMARY.md) | *(archived)* Initial implementation diary |
| [SubScene Timing Fix](../Shooting/SUBSCENE_TIMING_FIX.md) | *(archived)* PlayerShootingInput subscene timing fix |
| [Collision System Fix](../Shooting/COLLISION_SYSTEM_FIX.md) | *(archived)* Bullet collision physics dependency fix |
| [Bullet Scale Fix](../Shooting/BULLET_SCALE_FIX.md) | *(archived)* Bullet scale — preserve prefab scale fix |

---

## Effects System

**Hub:** [Dirt Explosion System](../Effects/DIRT_EXPLOSION_SYSTEM_README.md)

| Document | Description |
|----------|-------------|
| [Quick Setup Guide](../Effects/QUICK_SETUP_GUIDE.md) | Dirt explosion system setup |
| [Implementation Summary](../Effects/IMPLEMENTATION_SUMMARY.md) | Pooling architecture and implementation details |

---

## Splines System

| Document | Description |
|----------|-------------|
| [Splines README](../Splines/README.md) | SplineFollowerSystem and formation support |

---

## Document Count by System

| System | Documents |
|--------|-----------|
| Terrain | 23 |
| TransformFollower | 6 |
| Enemy Spawner | 8 |
| Shooting | 8 |
| Effects | 3 |
| Splines | 1 |
| Project Overview | 2 |
| **Total** | **51** |
