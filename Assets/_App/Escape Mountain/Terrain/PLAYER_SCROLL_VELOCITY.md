# Player Scroll Velocity & World Origin Systems

Documentation for the two scroll velocity providers (`PlayerScrollVelocitySystem` and `ConstantScrollVelocitySystem`) and the `WorldOriginTrackingInitSystem` that enables flight-sim style terrain navigation.

## Overview

The terrain scroll system is driven by a `TerrainScrollVelocity` singleton (direction + speed). Two providers write to this singleton — only one should be active at a time:

| Provider | When to use |
|----------|-------------|
| `PlayerScrollVelocitySystem` | Player faces a direction and the terrain scrolls that way; pitch controls altitude, bank controls turning (flight-sim / endless flyer) |
| `ConstantScrollVelocitySystem` | Fixed direction/speed, independent of player input (testing, racing games) |

---

## Player-Driven Scroll

### `PlayerScrollVelocityAuthoring` — Inspector Configuration

Add to the same SubScene GameObject as `TerrainConfigAuthoring`. Only one velocity provider should be in the scene — if both `PlayerScrollVelocityAuthoring` and `ConstantScrollVelocityAuthoring` exist, both will write to `TerrainScrollVelocity` each frame (last one wins).

| Field | Default | Description |
|-------|---------|-------------|
| `speed` | 50 m/s | Total scroll speed at level pitch (0° pitch = full horizontal scroll) |
| `rotationSpeed` | 2.0 | Bank-to-turn rotation multiplier for the world origin |
| `worldOriginSearchMode` | FindMainCamera | How to find the world origin GO at runtime |
| `worldOriginName` | "Main Camera" | Name to search (only for FindByName mode) |
| `worldOriginTag` | "MainCamera" | Tag to search (only for FindByTag mode) |

**Baked components:** `PlayerTerrainScrollVelocityConfig`, `WorldOriginTrackingSearch`, `WorldOriginTransformReference`

### `PlayerScrollVelocitySystem`

**Group:** `SimulationSystemGroup`  
**Order:** Before `ScrollTerrainSystem` and `TransformFollowerSystemOptimized`  
**Type:** `SystemBase` (managed — writes world-origin Transform)  
**Requires:** `TerrainScrollVelocity`, `PlayerTerrainScrollVelocityConfig`, `PlayerTransformReference`, `WorldOriginTransformReference`

#### Speed Decomposition by Pitch

Total speed is split between horizontal scroll and vertical movement by the player's pitch angle:

```
horizontal scroll speed = config.speed × cos(pitch)
vertical speed          = config.speed × sin(pitch)
```

At **pitch = 0** (level): full horizontal scroll, no vertical movement.  
At **pitch = 90** (nose-up): no scroll, maximum vertical rise.  
At **negative pitch** (nose-down): terrain scrolls forward, world origin descends.

#### Bank-to-Turn Rotation

Player bank angle (world Z-axis Euler) drives rotation of the `worldOriginTransform`:

```
rotationAmount = -sin(bankRadians) × rotationSpeed × deltaTime
worldOriginTransform.rotation *= Quaternion.Euler(0, rotationAmount, 0)
```

Using sine maps ±90° bank → ±1.0 rotation speed and 0°/180° bank → 0 rotation. This creates a natural steering curve where gentle bank produces gentle turns.

#### World Origin Vertical Movement

The world origin's Y position is adjusted by `verticalVelocity × deltaTime` each frame with no vertical bounds.

---

## World Origin Tracking

### `WorldOriginTrackingInitSystem`

**Group:** `InitializationSystemGroup`  
**Type:** `SystemBase` (managed — searches GameObjects)

Finds the world origin GameObject at startup and populates `WorldOriginTransformReference.worldOriginTransform`. Mirrors the same search-and-initialize pattern as `PlayerTrackingInitSystem`.

**Search modes:**

| Mode | Method | Required field |
|------|--------|----------------|
| `FindByName` | `GameObject.Find(name)` | `worldOriginName` |
| `FindByTag` | `GameObject.FindGameObjectWithTag(tag)` | `worldOriginTag` |
| `FindMainCamera` | `Camera.main` | — |

**Console messages:**
- `[WorldOriginTrackingInitSystem] ✅ Found world origin: <name>` — success
- `[WorldOriginTrackingInitSystem] Could not find world origin GameObject!` — check search mode and GO name/tag

### Components

#### `WorldOriginTrackingSearch` (singleton struct)

```csharp
public struct WorldOriginTrackingSearch : IComponentData
{
    public Mode mode;           // FindByName / FindByTag / FindMainCamera
    public FixedString64Bytes searchString;
    public bool initialized;    // Set to true after successful search
}
```

#### `WorldOriginTransformReference` (managed singleton)

```csharp
public class WorldOriginTransformReference : IComponentData
{
    public Transform worldOriginTransform;  // Populated at runtime by WorldOriginTrackingInitSystem
}
```

#### `PlayerTerrainScrollVelocityConfig` (singleton struct)

```csharp
public struct PlayerTerrainScrollVelocityConfig : IComponentData
{
    public float speed;               // Total speed (m/s)
    public float rotationSpeed;       // Bank turn multiplier
}
```

---

## Constant Scroll (Testing/Racing)

### `ConstantScrollVelocityAuthoring` — Inspector Configuration

| Field | Default | Description |
|-------|---------|-------------|
| `direction` | (0,0,1) | World-space scroll direction (normalized automatically) |
| `speed` | 50 m/s | Scroll speed in m/s |

**Baked component:** `ConstantTerrainScrollVelocityConfig`

### `ConstantScrollVelocitySystem`

**Group:** `SimulationSystemGroup`  
**Order:** Before `ScrollTerrainSystem`  
**Type:** `ISystem`, Burst-compiled  

Writes `TerrainScrollVelocity.direction = config.direction` and `.speed = config.speed` every frame. No player input is read.

Use this for:
- Fixed-speed terrain testing without VR rig setup
- Racing games with a constant forward lane
- Any scenario where scroll speed should not vary with player orientation

---

## `TerrainScrollVelocity` Singleton

Written by velocity providers, read by `ScrollTerrainSystem`:

```csharp
public struct TerrainScrollVelocity : IComponentData
{
    public float3 direction;  // Normalized world-space scroll direction (XZ plane)
    public float speed;       // Speed in m/s (can be zero to pause scrolling)
}
```

`ScrollTerrainSystem` accumulates: `ScrollOffset.accumulatedOffset += direction × speed × deltaTime`

---

## Setup Guide

### Flight-Sim / Endless Flyer

1. Add `PlayerScrollVelocityAuthoring` to the SubScene config entity
2. Set `speed` to desired cruise speed (50 m/s typical)
3. Set `worldOriginSearchMode` to match your camera/rig setup
4. Do **not** add `ConstantScrollVelocityAuthoring`
5. Disable `scrollEnabled` on `TerrainConfigAuthoring` (the player system writes `TerrainScrollVelocity` directly — the legacy `scrollEnabled` flag controls a different, now-unused code path)

### Fixed Scroll (Testing)

1. Add `ConstantScrollVelocityAuthoring` to the SubScene config entity
2. Set `direction` and `speed` as desired
3. Do **not** add `PlayerScrollVelocityAuthoring`

### No Scroll (Open World)

1. Add neither velocity provider
2. `TerrainScrollVelocity.speed` remains 0 → `ScrollOffset` never accumulates → terrain stays stationary relative to the player

---

## Troubleshooting

**Terrain not scrolling with player movement:**
- Verify `PlayerScrollVelocityAuthoring` is in the subscene (not the main scene)
- Check console for `[WorldOriginTrackingInitSystem] ✅ Found world origin`
- Confirm `PlayerTransformReference` is populated (player tracking must work first)

**World origin rotation not working:**
- Check `worldOriginSearchMode` and ensure the target GO exists at play time
- `worldOriginTransform` must not be null — check `WorldOriginTransformReference` in Entity Debugger

**Both providers active (scroll ignores input):**
- `PlayerScrollVelocitySystem` and `ConstantScrollVelocitySystem` both write `TerrainScrollVelocity` each frame
- The system that runs last wins — remove one authoring component

**Vertical movement not working:**
- `worldOriginTransform` must be a valid Transform (not null)
- Pitch angle is read from the player's world `forward.y` — ensure the player GO's forward is oriented correctly

---

## Related Documentation

- **[Auto-Scrolling Guide](Documentation/AUTO_SCROLLING.md)** — Full auto-scrolling configuration
- **[Turret System](TURRET_SYSTEM.md)** — How `TerrainScrollVelocity` is used for ballistic lead
- **[Configuration Reference](Documentation/CONFIGURATION.md)** — `TerrainConfigAuthoring` parameters
