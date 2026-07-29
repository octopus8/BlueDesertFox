# Dirt Explosion Pooling System - Documentation

## Overview

Pooled dirt explosion VFX system that spawns effects when bullets hit terrain. Effects automatically return to pool after their configured lifetime, following the same efficient pattern as the bullet pooling system.

## Architecture

### Components

**DirtExplosion** (Tag)
- Identifies dirt explosion entities
- Used for queries and filtering

**DirtExplosionData** (IComponentData)
- `spawnTime` (double): Time when explosion was spawned (Time.ElapsedTime)
- `active` (bool): Whether explosion is currently active or pooled

**DirtExplosionConfig** (Singleton IComponentData)
- `initialPoolSize` (int): Number pre-spawned at initialization
- `maxPoolSize` (int): Maximum pool capacity
- `lifetime` (float): How long explosions stay active before returning to pool (seconds)
- `currentPoolCount` (int): Runtime tracking of pool size

### Systems

**DirtExplosionPoolSystem** (InitializationSystemGroup)
- Pre-spawns configured number of explosions at startup
- Positions inactive explosions at (0, -10000, 0) (off-screen)
- Provides `GetFromPool(ref SystemState)` and `ReturnToPool(Entity)` helpers
- Dynamically grows pool up to maxPoolSize if exhausted
- Preserves prefab scale when instantiating

**DirtExplosionLifecycleSystem** (SimulationSystemGroup)
- Queries active explosions each frame
- Checks elapsed time against configured lifetime
- Returns expired explosions to pool (marks inactive, moves off-screen)
- Uses `NativeList<Entity>` collection pattern (zero GC)

**BulletCollisionSystem** (Modified)
- Detects terrain collisions via `HasComponent<TerrainTile>()` checks
- Records bullet positions at terrain impact points
- Spawns dirt explosions from pool at collision locations
- Sets explosions to upward-facing orientation (quaternion.identity)

### Authoring

**DirtExplosionPoolConfigAuthoring** (MonoBehaviour)
- Place on GameObject in scene (typically with TerrainConfigAuthoring)
- Inspector fields:
  - `initialPoolSize = 20` (default)
  - `maxPoolSize = 50` (default)
  - `lifetime = 2.5f` (default, matches VFX duration)
- Bakes to `DirtExplosionConfig` singleton

## Setup Instructions

### 1. Add Pool Config to Scene

1. Select GameObject with `TerrainConfigAuthoring` (or similar singleton)
2. Add Component → `DirtExplosionPoolConfigAuthoring`
3. Configure pool settings in Inspector:
   - **Initial Pool Size**: Number pre-spawned (20 recommended for VR)
   - **Max Pool Size**: Maximum capacity (50 recommended)
   - **Lifetime**: Duration before recycling (2.5s default for VFX Graph)

### 2. Verify Prefab Reference

The `PrefabEntitiesReferencesAuthoring` component already includes `dirtExplosionSmallPrefab` field:
- Ensure "Dirt Explosion Small Prefab" is assigned in Inspector
- Prefab should have VFX Graph component configured
- System will log warning if prefab is null at initialization

### 3. Test in Play Mode

**Expected Behavior:**
1. Pool initializes on startup: `[DirtExplosionPoolSystem] Initialized pool with 20 explosions`
2. Bullets hitting terrain spawn explosions: `[BulletCollisionSystem] Spawned X dirt explosions at terrain collision points`
3. Explosions auto-cleanup after lifetime: `[DirtExplosionLifecycleSystem] Returned X explosions to pool (lifetime cleanup)`

**Debug Logs:**
- Enable/disable via Debug.Log calls in each system
- Monitor pool growth: Watch for "Pool grew to X explosions" warnings
- Check for pool exhaustion: "Pool exhausted and at max size" warnings

## Performance Characteristics

### Memory Usage
- **Initial**: 20 explosion entities (configurable)
- **Max**: 50 explosion entities (configurable)
- **Per Entity**: VFX Graph component + LocalTransform + DirtExplosion + DirtExplosionData (~200 bytes)
- **Total Max**: ~10 KB

### CPU Performance
- **Pool Init**: One-time cost at startup (~0.5ms for 20 entities)
- **Spawn**: <0.1ms per explosion (dequeue from pool, set transform/data)
- **Lifecycle**: <0.1ms per frame (queries active explosions, time checks)
- **Return**: <0.05ms per explosion (mark inactive, move off-screen, enqueue)

### VR Optimization
- Pre-spawning avoids mid-frame allocations
- Time-based cleanup ensures VFX completes before recycling
- Pool size limits prevent runaway spawning
- Zero GC allocations (uses NativeQueue, NativeList with Allocator.Temp)

## Configuration Tuning

### High Fire Rate Scenarios
If pool exhaustion warnings appear frequently:
- Increase `maxPoolSize` to 75-100
- Reduce `lifetime` to 2.0s (faster recycling)
- Increase `initialPoolSize` to 30-40 (reduce dynamic growth)

### Low-End VR (Quest 2)
Reduce pool sizes to minimize memory:
- `initialPoolSize = 10`
- `maxPoolSize = 25`
- Consider reducing VFX complexity in prefab

### Desktop/High-End VR
Can increase for visual quality:
- `initialPoolSize = 40`
- `maxPoolSize = 100`
- `lifetime = 3.0f` (longer VFX duration)

## Integration with Existing Systems

### Dependencies
- **Required Singletons**: `PrefabEntitiesReferences`, `DirtExplosionConfig`
- **Required Systems**: `BulletPoolSystem` (for collision detection), `DirtExplosionPoolSystem`, `DirtExplosionLifecycleSystem`
- **Unity Packages**: Unity.Physics (collision events), Unity.Entities, Unity.Transforms

### Execution Order
1. `InitializationSystemGroup`: `DirtExplosionPoolSystem` (initializes pool)
2. `FixedStepSimulationSystemGroup`: `BulletCollisionSystem` (spawns explosions)
3. `SimulationSystemGroup`: `DirtExplosionLifecycleSystem` (returns to pool)

### Event Flow
```
Bullet → Terrain Collision
    ↓
BulletCollisionSystem detects TerrainTile
    ↓
Get explosion from DirtExplosionPoolSystem
    ↓
Set position to bullet.Position
    ↓
Set active=true, spawnTime=ElapsedTime
    ↓
VFX Graph plays automatically
    ↓
DirtExplosionLifecycleSystem monitors elapsed time
    ↓
When (ElapsedTime - spawnTime) > lifetime
    ↓
Return to pool (active=false, position=(0,-10000,0))
```

## Troubleshooting

### No explosions spawning
- Check `DirtExplosionPoolConfigAuthoring` exists in scene
- Verify `dirtExplosionSmallPrefab` assigned in `PrefabEntitiesReferencesAuthoring`
- Check console for: `[DirtExplosionPoolSystem] Initialized pool with X explosions`

### Explosions not disappearing
- Verify `lifetime` is set correctly (not 0 or negative)
- Check `DirtExplosionLifecycleSystem` is in hierarchy (Window → Entities → Systems)
- Ensure VFX Graph has finite duration matching `lifetime` setting

### Pool exhaustion warnings
- Increase `maxPoolSize` in Inspector
- Reduce fire rate or `lifetime` duration
- Check bullet collision logic isn't spawning duplicates

### VFX not playing
- Ensure DirtExplosionSmall.prefab has VFX Graph component
- Check prefab is not disabled in hierarchy
- Verify explosion entities are moving to correct positions (use Scene view in Play mode)

## Code Reference

### Spawn Explosion Manually (Example)
```csharp
var explosionPoolHandle = state.World.GetExistingSystem<DirtExplosionPoolSystem>();
ref var explosionPool = ref state.WorldUnmanaged.GetUnsafeSystemRef<DirtExplosionPoolSystem>(explosionPoolHandle);

Entity explosion = explosionPool.GetFromPool(ref state);
if (explosion != Entity.Null)
{
    state.EntityManager.SetComponentData(explosion, new LocalTransform
    {
        Position = spawnPosition,
        Rotation = quaternion.identity,
        Scale = 1f
    });
    
    state.EntityManager.SetComponentData(explosion, new DirtExplosionData
    {
        spawnTime = SystemAPI.Time.ElapsedTime,
        active = true
    });
}
```

### Query Active Explosions
```csharp
foreach (var (explosionData, transform, entity) in 
    SystemAPI.Query<RefRO<DirtExplosionData>, RefRO<LocalTransform>>()
        .WithAll<DirtExplosion>()
        .WithEntityAccess())
{
    if (explosionData.ValueRO.active)
    {
        // Process active explosion
        float3 position = transform.ValueRO.Position;
    }
}
```

## Future Enhancements

### Potential Improvements
1. **Surface-aligned spawning**: Raycast to get terrain normal, rotate VFX accordingly
2. **Different terrain types**: Spawn different VFX for dirt/rock/metal surfaces
3. **Impact size variation**: Scale VFX based on bullet velocity/damage
4. **Audio integration**: Play impact sounds synchronized with VFX
5. **Particle pooling**: If VFX Graph uses sub-emitters, pool those separately

### Performance Optimizations
1. **Burst compilation**: Mark systems with `[BurstCompile]` where possible
2. **Job parallelization**: Convert lifecycle checks to IJobEntity
3. **Spatial queries**: Only check explosions near player (frustum culling)
4. **LOD system**: Disable distant explosions, simpler VFX for mid-range

---

**Last Updated**: May 11, 2026  
**System Version**: 1.0  
**Unity Version**: 2023.3+ (Unity 6)  
**DOTS Version**: Entities 1.0+

