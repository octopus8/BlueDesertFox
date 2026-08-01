# Turret System

Documentation for the ECS turret system: authoring chain, ballistic intercept aiming, barrel pitch, burst-fire with LOS gating, and player velocity estimation.

## Overview

Turrets are static objects spawned onto terrain tiles that track and shoot at the player. Each turret has three parts, each with its own authoring component and ECS component:

```
ConcreteTurret prefab
├── [Root]            (spawned as static object via StaticObjectSpawnerConfigAuthoring)
├── Dome              → TurretDomeAuthoring   → TurretDome component
└── howitzer_barrel   → TurretBarrelAuthoring  → TurretBarrelTag component
                      → TurretShooterAuthoring → TurretShooterState + TurretLaunchOffset
```

After the prefab is instantiated, `StaticObjectHierarchyFlattenUtility` removes the ECS parent-child hierarchy. All three entities end up with independent world-space `LocalTransform` components. The barrel and dome cross-reference each other via `TurretBarrelTag.domeEntity`.

### Hierarchical LOD (prefab swap)

Turret LOD0 is a multi-entity prefab (root + dome + barrel/shooter). LOD1/LOD2 are mesh-only. At spawn, distance picks the initial prefab. When the player later crosses a structural LOD band:

1. `StaticObjectLODUpdateSystem` sets `GlobalStaticObjectInstanceData.pendingPrefabLOD` (does **not** mesh-swap hierarchical slots).
2. `StaticObjectLODPrefabSwapSystem` destroys the old hierarchy and instantiates the target prefab (budgeted via `maxPrefabLODSwapsPerFrame`), preserving tile ownership.
3. Combat (aim/shoot) exists **only** on LOD0 instances. Approaching a distant LOD1 turret upgrades it to LOD0 so it can fire; leaving LOD0 range downgrades back to the mesh-only prefab.

Mesh-only object types (trees, rocks) still use in-place `MaterialMeshInfo` swaps.

---

## Authoring Setup

### 1. `TurretDomeAuthoring` — on the Dome child

| Field | Default | Description |
|-------|---------|-------------|
| `bulletSpeed` | 30 m/s | Speed of fired bullets (used in intercept calculation) |
| `rotationSpeed` | 90°/s | Maximum Y-axis rotation speed (0 = instant snap) |

Bakes `TurretDome` component onto the dome entity.

### 2. `TurretBarrelAuthoring` — on the howitzer_barrel child

| Field | Default | Description |
|-------|---------|-------------|
| `pitchSpeed` | 90°/s | Maximum barrel pitch speed |
| `minPitchDegrees` | -20° | Minimum pitch delta from neutral |
| `maxPitchDegrees` | 60° | Maximum pitch delta from neutral |
| `modelForwardAxis` | (0,0,1) | Model-space axis pointing toward the barrel tip |

The baker captures the barrel's dome-local position and neutral-pitch rotation, and pre-computes `neutralElevationAngle = atan2(F0.y, F0.z)` from the model's forward axis. This allows `TurretBarrelSystem` to compute the required pitch delta without trigonometric overhead at runtime.

Bakes `TurretBarrelTag` component onto the barrel entity.

### 3. `TurretShooterAuthoring` — also on the howitzer_barrel child

| Field | Default | Description |
|-------|---------|-------------|
| `bulletSpawnPoint` | (required) | Child GameObject placed at the muzzle tip |
| `bulletsPerBurst` | 3 | Shots per burst |
| `burstIntraDelay` | 0.15s | Delay between shots within a burst |
| `cooldownDuration` | 3s | Wait time between bursts |
| `maxFireAngleDegrees` | 8° | Maximum barrel-to-intercept angle to allow firing |
| `maxFireDistance` | 100m | Maximum barrel-to-player distance to allow firing |

The baker bakes the bullet spawn point's local offset/rotation into `TurretShooterState.spawnLocalOffset/spawnLocalRotation` and the muzzle dome-local position into `TurretLaunchOffset.domeLocalOffset`.

Bakes `TurretShooterState` and `TurretLaunchOffset` onto the barrel entity.

---

## Systems

### `PlayerTargetVelocityEstimateSystem`

**Group:** `TransformSystemGroup`  
**Order:** Before `TurretAimingSystem`  
**Type:** `ISystem` (managed — reads Transform)

Estimates the player's world-space horizontal velocity from finite differences on `PlayerTransformReference.playerTransform.position`. Applies light smoothing (lerp factor 0.45) to reduce VR tracking noise.

Writes to `PlayerTargetVelocity` singleton:
- `horizontal` — smoothed XZ velocity (float3, Y=0)
- `lastWorldPosition` — position from previous frame
- `hasPrevious` — false on first frame

**Note:** `TurretAimingSystem` currently ignores `PlayerTargetVelocity` and leads only from `TerrainScrollVelocity` (scroll direction × speed). The velocity estimate is available if game logic evolves to require full player locomotion lead.

---

### `TurretAimingSystem`

**Group:** `TransformSystemGroup`  
**Order:** After `objectPositionUpdateSystem`, before `LocalToWorldSystem`  
**Type:** `ISystem` with Burst parallel job

Rotates each dome entity's `LocalTransform.Rotation` (Y axis only) to lead the player using a ballistic intercept calculation.

**Algorithm:**
1. Read player world position from `PlayerTransformReference`
2. Read `TerrainScrollVelocity` as the lead velocity (scroll direction × speed)
3. For each dome, solve the XZ-plane quadratic intercept equation:
   - Relative position: `p = playerPos - muzzlePos` (XZ plane)
   - Relative velocity: `v = scrollVelocity` (XZ plane)
   - Solve: `|p + v*t|² = (bulletSpeed*t)²` for smallest positive `t`
4. Convert intercept displacement to Y-axis quaternion
5. Smooth-lerp current dome Y angle toward target at `rotationSpeed` deg/s
6. Write intercept 3D world position to `TurretDome.interceptPoint` (Y = player's current Y)
7. Update `LocalTransform.Rotation`

If the intercept equation has no real solution (player too fast or too far), the dome stops rotating and holds its last angle.

---

### `TurretBarrelSystem`

**Group:** `TransformSystemGroup`  
**Order:** After `TurretAimingSystem`  
**Type:** `ISystem` with Burst parallel job

Positions and pitches the barrel entity to follow its dome after the hierarchy was flattened.

**Algorithm (per barrel entity):**
1. Look up the dome's `LocalToWorld` matrix (read from `LocalTransform` after `TurretAimingSystem` runs)
2. Apply `barrelTag.localOffset` and `barrelTag.localRotation` in dome-local space → world barrel position and rotation at neutral pitch
3. Calculate pitch delta from neutral to point toward `dome.interceptPoint` in the dome-local YZ plane
4. Clamp pitch to `[minPitchAngle, maxPitchAngle]`
5. Smooth-lerp current pitch angle toward target at `pitchSpeed` deg/s
6. Compose final barrel rotation: `neutralRotation × pitchDelta`
7. Write `LocalTransform.Position` and `.Rotation` for the barrel entity

---

### `TurretShooterSystem`

**Group:** `SimulationSystemGroup`  
**Order:** After `TransformSystemGroup`  
**Type:** `ISystem`

Manages the burst-fire state machine and spawns bullets from the pool.

**State machine:**
```
COOLDOWN (inCooldown=true)
    → When ElapsedTime ≥ cooldownEndsAt: reset burst, enter READY
    
READY (bulletsRemainingInBurst > 0)
    → Evaluate fire gate:
       - Barrel-to-intercept angle ≤ maxFireAngleRadians
       - Player distance ≤ maxFireDistance
       - No terrain LOS block (raycast, cached for whole burst)
    → When gate passes and intraDelay elapsed: fire bullet, decrement bulletsRemainingInBurst
    → When bulletsRemainingInBurst = 0: enter COOLDOWN
```

**LOS gating:** The first shot in each burst performs a physics raycast along the muzzle-to-player direction. If terrain is hit before reaching the player, the entire burst is skipped and cooldown begins immediately. The result is cached (`burstTerrainBlocked`) for the remaining shots in the burst.

**Bullet spawn:** Calls `BulletPoolSystem.GetFromPool()`, sets `LocalTransform` at muzzle world position, assigns `LinearVelocity = muzzleForward × bulletSpeed - terrainScrollVelocity` via `BulletTerrainScrollVelocitySystem`.

---

## Component Reference

### `TurretDome` (on dome entity)

| Field | Type | Description |
|-------|------|-------------|
| `bulletSpeed` | float | m/s for intercept calculation |
| `rotationSpeed` | float | °/s maximum dome Y rotation |
| `currentYAngle` | float | Current dome angle (radians), tracked for smooth interpolation |
| `interceptPoint` | float3 | World-space intercept point written by `TurretAimingSystem`, read by `TurretBarrelSystem` and `TurretShooterSystem` |

### `TurretBarrelTag` (on barrel entity)

| Field | Type | Description |
|-------|------|-------------|
| `domeEntity` | Entity | The dome entity this barrel belongs to |
| `localOffset` | float3 | Barrel position in dome-local space |
| `localRotation` | quaternion | Barrel rotation at neutral pitch in dome-local space |
| `neutralElevationAngle` | float | Baked elevation angle (radians) of model forward at pitch=0 |
| `currentPitchAngle` | float | Current pitch delta (radians) |
| `pitchSpeed` | float | °/s maximum barrel pitch rotation |
| `minPitchAngle` | float | Minimum pitch delta (radians, typically negative) |
| `maxPitchAngle` | float | Maximum pitch delta (radians) |

### `TurretShooterState` (on barrel entity)

| Field | Type | Description |
|-------|------|-------------|
| `bulletsPerBurst` | int | Shots per burst (baked) |
| `burstIntraDelay` | float | Seconds between shots in burst |
| `cooldownDuration` | float | Seconds between bursts |
| `maxFireAngleRadians` | float | Max aiming error to allow shot |
| `maxFireDistance` | float | Max range to allow shot |
| `spawnLocalOffset` | float3 | Muzzle tip local position (from spawn point GO) |
| `spawnLocalRotation` | quaternion | Muzzle tip local rotation |
| `bulletsRemainingInBurst` | int | Bullets left in current burst |
| `lastShotTime` | double | `ElapsedTime` of last fired shot |
| `inCooldown` | bool | True while waiting between bursts |
| `cooldownEndsAt` | double | `ElapsedTime` when cooldown expires |
| `burstLineOfSightEvaluated` | bool | True after first-shot LOS raycast |
| `burstTerrainBlocked` | bool | Cached LOS result (true = terrain blocks) |

### `TurretLaunchOffset` (on barrel entity)

| Field | Type | Description |
|-------|------|-------------|
| `domeLocalOffset` | float3 | Muzzle position in dome-local space at neutral pitch (baked from barrel localPosition + rotated spawn offset) |

### `PlayerTargetVelocity` (singleton)

| Field | Type | Description |
|-------|------|-------------|
| `horizontal` | float3 | Smoothed XZ velocity (Y=0 always) |
| `lastWorldPosition` | float3 | Player position from previous frame |
| `hasPrevious` | bool | False on first frame after world creation |

---

## Prefab Setup Checklist

1. Create a prefab with hierarchy: Root → Dome → howitzer_barrel → BulletSpawnPoint
2. On **Dome**: add `TurretDomeAuthoring`
3. On **howitzer_barrel**: add `TurretBarrelAuthoring` + `TurretShooterAuthoring`
4. Set `TurretShooterAuthoring.bulletSpawnPoint` to the BulletSpawnPoint child
5. Add the prefab to `StaticObjectSpawnerConfigAuthoring.staticObjectPrefabs[]` (alongside tree prefabs)
6. Add LOD meshes to each LOD set in the spawner config if desired
7. Enter Play mode — turrets should spawn on terrain tiles and track the player

---

## Troubleshooting

**Dome not rotating:**
- Check `TerrainScrollVelocity.speed > 0` (scroll must be active for lead to be non-zero)
- Verify `PlayerTransformReference` is populated (`[PlayerTrackingInitSystem] ✅ Found player`)
- Confirm `TurretDome` component exists on the dome entity (Entity Debugger)

**Barrel not pitching:**
- Check `TurretBarrelTag.domeEntity` references the correct dome entity
- Verify `modelForwardAxis` is set correctly for the barrel mesh

**Turret never fires:**
- Confirm the instance is LOD0 (dome/barrel entities present). Distant spawns start as LOD1 until `StaticObjectLODPrefabSwapSystem` upgrades them inside `lod0Distance - hysteresis`.
- Confirm `maxFireDistance` covers typical turret-to-player distance (often tighter than `lod0Distance`)
- Check if terrain LOS always blocks (use scene gizmos to verify muzzle direction)
- Verify bullet pool has capacity (`BulletPoolConfig.initialPoolSize`)

**Bullets spawn at wrong position:**
- Ensure `bulletSpawnPoint` child GO is correctly placed at the muzzle tip in the prefab
- Check that `StaticObjectHierarchyFlattenUtility` ran after instantiation

---

## Related Documentation

- **[Static Object Spawning](STATIC_OBJECT_SPAWNING_SYSTEM.md)** — How turret prefabs are placed on tiles
- **[Player Scroll Velocity](PLAYER_SCROLL_VELOCITY.md)** — `TerrainScrollVelocity` used for intercept lead
- **[Terrain/Documentation/SYSTEM_REFERENCE.md](Documentation/SYSTEM_REFERENCE.md)** — Full system listing
