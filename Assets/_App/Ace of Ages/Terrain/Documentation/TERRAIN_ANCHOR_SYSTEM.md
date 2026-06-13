# Terrain Anchor System

Allows arbitrary ECS entities to move with scrolling terrain, without being terrain tiles themselves.

## Overview

When auto-scrolling is active, terrain tiles and static objects all shift each frame to create the illusion of movement. Any entity that needs to stay "attached" to the world (obstacles, decorations, waypoints) must also shift with the terrain — otherwise it appears frozen while the ground scrolls beneath it.

`TerrainAnchorTag` + `TerrainAnchorSystem` provide this behavior for any entity in the subscene.

## How It Works

Every frame, `TerrainAnchorSystem` sets each anchor's `LocalTransform.Position` to:

```
actualPosition = basePosition - scrollOffset.accumulatedOffset
```

- `basePosition` is the anchor's intended world-space origin (baked at startup or set at runtime).
- `scrollOffset.accumulatedOffset` grows each frame as terrain scrolls.
- Subtracting the offset moves the anchor opposite to the terrain scroll direction, keeping it visually stationary relative to the terrain.

## Components

### `TerrainAnchorTag` (`IComponentData`)

```csharp
public struct TerrainAnchorTag : IComponentData
{
    public float3 basePosition;  // World-space origin; actual position = basePosition - scrollOffset
}
```

### `TerrainAnchorSystem` (`ISystem`, Burst-compiled)

- **Update group:** `SimulationSystemGroup`
- **Update after:** `ScrollTerrainSystem`
- **Update before:** `TransformSystemGroup`
- Parallel `IJobEntity` — scales linearly with anchor count across CPU cores
- Requires `ScrollOffset` singleton; if no `TerrainAnchorTag` entities exist the system skips

## Authoring

Add `TerrainAnchorTagAuthoring` to any GameObject inside the subscene.

| Inspector Field | Default | Description |
|----------------|---------|-------------|
| `useCustomBasePosition` | false | Use transform position at bake time (false) or a manual override (true) |
| `customBasePosition` | (0,0,0) | Custom world-space origin (only active when `useCustomBasePosition = true`) |

The baker stores `basePosition` from either the GameObject's transform or the custom value, and adds `TerrainAnchorTag` to the baked entity.

A cyan gizmo (sphere + axes) appears in Scene view when the component is selected.

## Setup

1. Place the GameObject in the subscene at its desired world-space position.
2. Add `TerrainAnchorTagAuthoring` component.
3. Leave `useCustomBasePosition` unchecked (default) — the bake position is used automatically.
4. Enter Play mode. The entity moves with scrolling terrain.

## Use Cases

| Use Case | Notes |
|----------|-------|
| Spawned obstacles at fixed terrain positions | Set `basePosition` to desired spawn location |
| Waypoints and trigger zones | Add `TerrainAnchorTag` at runtime via ECB |
| Decorative props placed in the subscene | Simple — just add the authoring component |

## When NOT to Use `TerrainAnchorTag`

| Entity type | Correct approach |
|-------------|-----------------|
| Terrain tiles | Managed by `TileScrollPositionSystem` (already scroll-aware) |
| Static objects (trees, turrets) | Managed by `StaticObjectPositionUpdateSystem` via `StaticObjectTileOwnership` |
| Bullets | Use `BulletTerrainScrollVelocitySystem` to correct physics velocity |
| Player entity | Player stays at world origin; terrain scrolls around them |

## Runtime Control

Change `basePosition` at runtime to reposition an anchor:

```csharp
var anchor = EntityManager.GetComponentData<TerrainAnchorTag>(entity);
anchor.basePosition = newWorldPosition;
EntityManager.SetComponentData(entity, anchor);
```

The system reads `basePosition` every frame, so the entity moves to `newWorldPosition - currentScrollOffset` immediately on the next frame.

## Performance

- Burst-compiled `IJobEntity` with `ScheduleParallel` — no main-thread cost
- Typical: <0.1ms for 100 anchors on Quest 3
- System skips entirely if no `TerrainAnchorTag` entities exist

## Related Documentation

- **[Auto-Scrolling](AUTO_SCROLLING.md)** — How `ScrollOffset` accumulates
- **[Static Object Spawning](../STATIC_OBJECT_SPAWNING_SYSTEM.md)** — For objects placed on tile surfaces
- **[System Reference](SYSTEM_REFERENCE.md)** — Complete system listing

---

**Back to:** [Documentation Hub](README.md)
