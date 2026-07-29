# Infinite Terrain System
**Version:** 3.0 — Unity 6 (6000.3.10f1) · Unity.Entities 1.3.0+

Production-ready DOTS infinite terrain with procedural mesh generation, physics colliders, instanced tree/static-object rendering, auto-scrolling, and VR optimization.

## Documentation

| | |
|--|--|
| **[Documentation Hub](Documentation/README.md)** | Start here — full feature list, architecture, config reference, troubleshooting |
| **[Table of Contents](Documentation/TABLE_OF_CONTENTS.md)** | Index of all 20+ terrain docs |
| **[Quick Start Guide](Documentation/QUICK_START.md)** | Get terrain running in 10 minutes |
| **[Static Object Spawning](STATIC_OBJECT_SPAWNING_SYSTEM.md)** | Trees, turrets, and decorations |
| **[Turret System](TURRET_SYSTEM.md)** | Ballistic-intercept aiming and burst fire |
| **[Player Scroll Velocity](PLAYER_SCROLL_VELOCITY.md)** | Flight-sim and constant scroll providers |

## Performance Targets

| Platform | Frame Budget | Trees |
|----------|-------------|-------|
| Quest 3 VR | <8ms | 1000+ |
| Desktop RTX 3070 | <12ms | 2000+ |
| Desktop RTX 4080 | <10ms | 5000+ |

## Quick Troubleshooting

| Problem | Fix |
|---------|-----|
| Terrain not spawning | Check console: `[PlayerTrackingInitSystem] ✅ Found player` |
| Terrain not visible | Verify material in `TerrainConfigAuthoring.terrainMaterial` |
| Performance issues | See [Performance Guide](Documentation/PERFORMANCE.md) |
| Trees not rendering | Check static object prefabs in `StaticObjectSpawnerConfigAuthoring` |
