> **Archive Notice:** This is a historical patch note. The fix described here is already integrated into the codebase. See [Archive/README.md](Archive/README.md).

# Bullet Scale Fix - Preserve Prefab Scale

## Issue
Bullets were being spawned with a hardcoded scale of `1.0`, ignoring the scale set on the bullet prefab. This meant if you created a small bullet prefab (e.g., scale 0.1), it would appear at full size (1.0) when spawned.

## Solution
Modified the bullet spawning and pooling systems to preserve the prefab's original scale, following the same pattern used in `EnemySpawnerSystem.cs`.

## Files Changed

### 1. BulletShooterSystem.cs
**Added**: Prefab scale retrieval before spawning
```csharp
// Get the prefab's scale to preserve it
var prefabs = SystemAPI.GetSingleton<PrefabEntitiesReferences>();
float prefabScale = 1f;
if (SystemAPI.HasComponent<LocalTransform>(prefabs.bulletSimplePrefab))
{
    prefabScale = SystemAPI.GetComponent<LocalTransform>(prefabs.bulletSimplePrefab).Scale;
}

// Set bullet transform (preserving prefab scale)
state.EntityManager.SetComponentData(bulletEntity, new LocalTransform
{
    Position = spawnPosition,
    Rotation = spawnRotation,
    Scale = prefabScale  // ✅ Now uses prefab scale instead of hardcoded 1f
});
```

### 2. BulletPoolSystem.cs
**Added**: Prefab scale preservation in **two places**:

#### A. Initial Pool Spawn (OnUpdate)
```csharp
// Get the prefab's scale to preserve it
float prefabScale = 1f;
if (state.EntityManager.HasComponent<LocalTransform>(prefabs.bulletSimplePrefab))
{
    prefabScale = state.EntityManager.GetComponentData<LocalTransform>(prefabs.bulletSimplePrefab).Scale;
}

// Pre-spawn initial pool of bullets
for (int i = 0; i < config.initialPoolSize; i++)
{
    // ...
    state.EntityManager.SetComponentData(bullet, new LocalTransform
    {
        Position = new float3(0, -10000, 0),
        Rotation = quaternion.identity,
        Scale = prefabScale  // ✅ Uses prefab scale
    });
}
```

#### B. Dynamic Pool Growth (GetFromPool)
```csharp
// Get the prefab's scale to preserve it
float prefabScale = 1f;
if (state.EntityManager.HasComponent<LocalTransform>(prefabs.bulletSimplePrefab))
{
    prefabScale = state.EntityManager.GetComponentData<LocalTransform>(prefabs.bulletSimplePrefab).Scale;
}

// Create new bullet when pool exhausted
state.EntityManager.SetComponentData(bullet, new LocalTransform
{
    Position = new float3(0, -10000, 0),
    Rotation = quaternion.identity,
    Scale = prefabScale  // ✅ Uses prefab scale
});
```

## Pattern Used
This follows the exact same pattern as `EnemySpawnerSystem.cs`:

```csharp
// Get the prefab's scale to preserve it
float prefabScale = 1f;
if (SystemAPI.HasComponent<LocalTransform>(prefabEntitiesReferences.enemyZeroEntity))
{
    prefabScale = SystemAPI.GetComponent<LocalTransform>(prefabEntitiesReferences.enemyZeroEntity).Scale;
}
```

## Benefits

1. **Consistent with existing code**: Uses same pattern as enemy spawning
2. **Designer-friendly**: Artists/designers can set bullet scale in prefab, it will be preserved
3. **Flexible**: Supports different bullet sizes for different weapon types
4. **Backward compatible**: Default fallback to scale 1.0 if prefab has no LocalTransform

## Testing

### Before Fix
- Bullet prefab scale: `0.1` → Spawned bullets appear at scale `1.0` (too big)
- Bullet prefab scale: `2.0` → Spawned bullets appear at scale `1.0` (too small)

### After Fix
- Bullet prefab scale: `0.1` → Spawned bullets appear at scale `0.1` ✅
- Bullet prefab scale: `2.0` → Spawned bullets appear at scale `2.0` ✅
- Bullet prefab scale: `1.0` → Spawned bullets appear at scale `1.0` ✅

## Usage Example

To set bullet size:
1. Open bullet prefab in Inspector
2. Set Transform scale (e.g., `0.1, 0.1, 0.1` for small bullets)
3. Save prefab
4. Bullets will now spawn with that scale automatically

## Performance Impact
**None** - Scale is retrieved once per bullet spawn, same as enemy spawning system.

---

**Fix Applied**: May 7, 2026  
**Status**: ✅ Complete  
**Compilation**: ✅ No errors (warnings only - code style)

