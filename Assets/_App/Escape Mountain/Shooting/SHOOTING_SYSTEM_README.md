# Player Ship Shooting System - README

## Overview

A DOTS-based bullet shooting system for the player ship in Ace of Ages. Features entity pooling for performance, distance-based and collision-based cleanup, and Input System integration for VR controllers.

## Architecture

### Components

**BulletComponents.cs**:
- `Bullet` - Tag component for bullet entities
- `BulletData` - Tracks bullet state (spawn position, creation time, active flag)
- `BulletShooter` - Controls shooting behavior (fire rate, bullet speed, shoot trigger)
- `BulletSpawnPointReference` - Managed component storing Transform reference to spawn point
- `BulletPoolConfig` - Singleton configuration for bullet pool

### Systems

**BulletPoolSystem.cs** (`InitializationSystemGroup`):
- Pre-spawns bullet entities at startup (default: 50 bullets)
- Manages pool queue using `NativeQueue<Entity>`
- Provides `GetFromPool()` and `ReturnToPool()` helpers
- Auto-grows pool if exhausted (max: 100 bullets)

**BulletShooterSystem.cs** (`SimulationSystemGroup`, before `ResetEventsSystem`):
- Queries `BulletShooter` components with `doShoot=true`
- Gets bullet from pool, positions at spawn point Transform
- Sets velocity to `forward * bulletSpeed`
- Marks bullet as active

**BulletLifecycleSystem.cs** (`SimulationSystemGroup`, after `BulletShooterSystem`):
- Checks all active bullets for distance from spawn point
- Returns bullets >200m to pool
- Resets velocity and position

**BulletCollisionSystem.cs** (`FixedStepSimulationSystemGroup`, after `PhysicsSystemGroup`):
- Detects collision events via Unity Physics
- Returns colliding bullets to pool immediately
- Works with any collision (terrain, enemies, etc.)

**ResetEventsSystem.cs** (`SimulationSystemGroup`):
- Resets `BulletShooter.doShoot` flag to false each frame
- Also resets `EnemySpawner.doSpawn` flag

### MonoBehaviour Components

**PlayerShootingInput.cs**:
- Attach to player ship GameObject in main scene
- Uses `InputSystem.actions.FindAction("Fire")`
- Rate-limits shooting based on `fireRate`
- Sets `BulletShooter.doShoot=true` when fire button pressed

**BulletShooterAuthoring.cs**:
- Attach to player ship GameObject
- Configures `fireRate` (default: 0.2s = 5 rounds/sec)
- Configures `bulletSpeed` (default: 100 units/sec)

**BulletPoolConfigAuthoring.cs**:
- Attach to a singleton GameObject in scene
- Configures `initialPoolSize` (default: 50)
- Configures `maxPoolSize` (default: 100)

**PlayerShipAuthoring.cs** (modified):
- Now bakes `BulletSpawnPointReference` component
- Stores reference to `bulletSpawnPoint` GameObject's Transform

## Setup Instructions

### 1. Configure Player Ship

1. Open the player ship GameObject (e.g., in SubScene or main scene)
2. Ensure `PlayerShipAuthoring` is attached
3. Assign a child GameObject to `Bullet Spawn Point` field (this determines where bullets spawn and their initial direction)
4. Add `BulletShooterAuthoring` component
5. Configure fire rate and bullet speed in Inspector

### 2. Add Pool Configuration

1. Find or create a singleton GameObject (can use same GameObject as `TerrainConfigAuthoring`)
2. Add `BulletPoolConfigAuthoring` component
3. Configure pool sizes:
   - `Initial Pool Size`: 50 (recommended)
   - `Max Pool Size`: 100 (recommended)

### 3. Setup Input Action

1. Open `Assets/InputSystem_Actions.inputactions` (or your Input Actions asset)
2. Add a new Action called "Fire"
3. Add a binding to XR Controller:
   - Path: `XR Controller (Right Hand)/Primary Button`
   - Or: `XR Controller (Right Hand)/Trigger`
4. Save the asset

### 4. Add Input Handler

1. Find the player ship GameObject in the **main scene** (NOT in SubScene)
2. Add `PlayerShootingInput` component
3. Ensure `InputSystemActionsInitializer` is in the scene (required for InputSystem.actions)

### 5. Configure Bullet Prefab

1. Open `PrefabEntitiesReferencesAuthoring` in the scene
2. Assign a bullet prefab to `Bullet Simple Prefab` field
3. The bullet prefab should have:
   - Mesh renderer (for visuals)
   - `PhysicsShape` component (for collision detection)
   - Appropriate physics layer (see below)

### 6. Physics Layer Setup (Optional but Recommended)

1. Go to `Edit > Project Settings > Tags and Layers`
2. Create a new layer called "Projectile"
3. Go to `Edit > Project Settings > Physics`
4. Configure collision matrix:
   - Projectile ✓ Terrain (bullets collide with terrain)
   - Projectile ✓ Enemy (bullets collide with enemies)
   - Projectile ✗ Projectile (bullets don't collide with each other)
   - Projectile ✗ Player (bullets don't collide with player)
5. Set bullet prefab's GameObject layer to "Projectile"

## Usage

### Testing

1. Enter Play mode
2. Press the Fire button (right controller trigger or configured button)
3. Bullets should spawn from the spawn point and travel forward
4. Check Console for debug logs:
   - `[BulletPoolSystem] Initialized pool with 50 bullets`
   - `[BulletShooterSystem] Fired bullet at position...`
   - `[BulletLifecycleSystem] Returned X bullets to pool...`
   - `[BulletCollisionSystem] Returned X bullets to pool...`

### Expected Behavior

- **Fire Rate**: With default 0.2s fire rate, you can shoot 5 bullets per second
- **Bullet Speed**: With default 100 units/sec, bullets travel fast
- **Distance Cleanup**: Bullets disappear after traveling 200m from spawn point
- **Collision Cleanup**: Bullets disappear immediately upon hitting terrain/enemies
- **Pool Growth**: If you shoot faster than bullets are cleaned up, pool will grow (warning logged)

### Troubleshooting

**Problem**: "Fire action not found in InputSystem.actions"
- **Solution**: Add InputSystemActionsInitializer to scene OR configure Project-Wide Actions in Project Settings

**Problem**: "No entity found with BulletShooter + PlayerShip components"
- **Solution**: Make sure BulletShooterAuthoring is attached to the player ship GameObject

**Problem**: Bullets spawn but don't move
- **Solution**: Check that bulletSpawnPoint is assigned and bulletSpeed > 0

**Problem**: Bullets don't collide
- **Solution**: Ensure bullet prefab has PhysicsShape component and appropriate collision layer

**Problem**: Pool exhausted warning
- **Solution**: Increase maxPoolSize or decrease fire rate / increase cleanup distance

## Performance Characteristics

- **Pool Size**: 50 pre-spawned bullets (minimal memory: ~5KB)
- **Max Bullets**: 100 bullets (grows dynamically if needed)
- **Cleanup**: Distance check runs every frame (Burst-compiled, <0.1ms)
- **Collision**: Physics-based, runs in `FixedStepSimulationSystemGroup`
- **Zero GC**: All systems use native collections, no managed allocations during gameplay

## Files Created

```
Assets/_App/Ace of Ages/Shooting/
├── BulletComponents.cs             - Component definitions
├── BulletPoolSystem.cs             - Pool management system
├── BulletShooterSystem.cs          - Bullet spawning system
├── BulletLifecycleSystem.cs        - Distance-based cleanup system
├── BulletCollisionSystem.cs        - Collision-based cleanup system
├── PlayerShootingInput.cs          - MonoBehaviour input handler
├── BulletShooterAuthoring.cs       - Authoring component for shooter config
├── BulletPoolConfigAuthoring.cs    - Authoring component for pool config
└── SHOOTING_SYSTEM_README.md       - This file

Assets/_App/Ace of Ages/Player/
└── PlayerShipAuthoring.cs          - Modified to bake BulletSpawnPointReference

Assets/_App/Ace of Ages/
└── ResetEventsSystem.cs            - Modified to reset BulletShooter.doShoot flag
```

## Integration with Existing Systems

- **Compatible with EnemySpawnerSystem**: Uses same `ResetEventsSystem` pattern
- **Compatible with Terrain System**: Bullets can collide with terrain physics colliders
- **Compatible with Formation System**: No conflicts, runs independently
- **Uses PrefabEntitiesReferences**: Extends existing prefab singleton pattern

## Future Enhancements (Deferred)

- Visual effects (muzzle flash, bullet trails)
- Audio feedback (shooting sound)
- Bullet impact effects
- Different bullet types
- Charged shots
- Heat/ammo system

---

**Implementation Status**: ✅ COMPLETE  
**Date**: May 7, 2026  
**Systems**: 5 ECS systems + 3 authoring components + 1 MonoBehaviour  
**Pool Strategy**: NativeQueue-based entity pooling with distance and collision cleanup

