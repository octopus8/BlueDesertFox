# Bullet Spawn Point - Entity Transform + Offset Implementation

## Overview

Changed bullet spawning from using a managed Transform reference to using the PlayerShip entity's transform with a pre-calculated offset. This is a pure ECS approach that eliminates cross-boundary calls and ensures the spawn point always follows the entity properly.

## Problem with Previous Approach

**Before**: Stored a managed `Transform` reference in `BulletSpawnPointReference` and read its position/rotation each frame.

**Issues**:
- Managed component (not Burst-compatible)
- Cross-boundary calls from ECS to GameObject/Transform
- Transform might not update in sync with entity
- Requires GameObject to exist in same scene

## New Approach

**Now**: Store the spawn point's **local offset** relative to the PlayerShip at bake time, then apply it to the entity's transform at runtime.

**Benefits**:
- ✅ Pure ECS (unmanaged struct component)
- ✅ Burst-compatible (could be optimized further)
- ✅ Always synchronized with entity transform
- ✅ No GameObject dependencies at runtime
- ✅ Proper rotation (offset rotates with ship)

## Implementation Details

### 1. Component Definition (PlayerShipAuthoring.cs)

Changed from managed to unmanaged component:

```csharp
// Before (managed):
public class BulletSpawnPointReference : IComponentData
{
    public Transform spawnPoint;
}

// After (unmanaged):
public struct BulletSpawnPointReference : IComponentData
{
    public float3 localOffset;      // Position offset from ship center
    public quaternion localRotation; // Rotation offset from ship rotation
}
```

### 2. Baking (PlayerShipAuthoring.cs)

Calculate offset at bake time from the spawn point GameObject:

```csharp
public class Baker : Baker<PlayerShipAuthoring>
{
    public override void Bake(PlayerShipAuthoring authoring)
    {
        Entity entity = GetEntity(TransformUsageFlags.Dynamic);
        AddComponent<PlayerShip>(entity);
        
        // Calculate local offset from the bullet spawn point GameObject
        float3 localOffset = float3.zero;
        quaternion localRotation = quaternion.identity;
        
        if (authoring.bulletSpawnPoint != null)
        {
            localOffset = authoring.bulletSpawnPoint.transform.localPosition;
            localRotation = authoring.bulletSpawnPoint.transform.localRotation;
        }
        
        AddComponent(entity, new BulletSpawnPointReference
        {
            localOffset = localOffset,
            localRotation = localRotation
        });
    }
}
```

**Key Points**:
- `localPosition` gives position relative to parent (PlayerShip)
- `localRotation` gives rotation relative to parent
- These are **baked once** and used forever
- Designer can position spawn point in Editor, offset is captured automatically

### 3. Runtime Application (BulletShooterSystem.cs)

Apply offset to entity's world transform:

```csharp
// Query includes LocalTransform and BulletSpawnPointReference
foreach (var (shooter, transform, spawnPointRef) in 
    SystemAPI.Query<RefRW<BulletShooter>, RefRO<LocalTransform>, RefRO<BulletSpawnPointReference>>())
{
    var shipTransform = transform.ValueRO;
    
    // Calculate world position: ship position + rotated offset
    float3 spawnPosition = shipTransform.Position + 
        math.rotate(shipTransform.Rotation, spawnPointRef.ValueRO.localOffset);
    
    // Calculate world rotation: ship rotation * local rotation
    quaternion spawnRotation = math.mul(shipTransform.Rotation, 
        spawnPointRef.ValueRO.localRotation);
    
    // Get forward direction from rotation
    float3 forward = math.mul(spawnRotation, new float3(0, 0, 1));
    
    // Spawn bullet at calculated position/rotation
    // ...
}
```

**Math Explained**:
- `math.rotate(shipRotation, offset)` - Rotates the offset vector by the ship's rotation
- `math.mul(shipRotation, localRotation)` - Combines rotations (ship * local = world)
- `math.mul(rotation, forward)` - Converts quaternion to forward direction vector

### 4. Example

**Setup in Unity**:
```
PlayerShip GameObject
├── Transform: Position (5, 2, 10), Rotation (0, 45, 0)
└── BulletSpawnPoint (child)
    └── Transform: localPosition (0, 0, 2), localRotation (0, 0, 0)
```

**Baked Data**:
```csharp
BulletSpawnPointReference
{
    localOffset = (0, 0, 2),        // 2 units forward
    localRotation = identity         // No rotation offset
}
```

**Runtime Calculation** (when ship is at position (5, 2, 10) facing 45° right):
```csharp
shipPosition = (5, 2, 10)
shipRotation = quaternion.RotateY(45°)

// Rotate offset by ship rotation
rotatedOffset = math.rotate(shipRotation, (0, 0, 2))
              = (1.414, 0, 1.414)  // 2 units forward at 45°

// Final spawn position
spawnPosition = (5, 2, 10) + (1.414, 0, 1.414)
              = (6.414, 2, 11.414) // 2 units forward from ship in world space
```

**Result**: Bullet spawns 2 units ahead of the ship, regardless of ship's rotation!

## Behavior Changes

### Position
- **Before**: Read from `bulletSpawnPoint.transform.position` (world space)
- **After**: Calculated from `shipTransform.Position + rotated offset`
- **Result**: Same world position, but calculated differently

### Rotation
- **Before**: Read from `bulletSpawnPoint.transform.rotation` (world space)
- **After**: Calculated from `shipTransform.Rotation * localRotation`
- **Result**: Same world rotation, but calculated differently

### Performance
- **Before**: Cross-boundary call to Transform (managed → unmanaged)
- **After**: Pure math operations on entity data
- **Result**: Slightly faster, more GC-friendly

## Designer Workflow

### Before (GameObject Required)
1. Create BulletSpawnPoint GameObject
2. Make it child of PlayerShip
3. Position it where bullets should spawn
4. Assign to `bulletSpawnPoint` field
5. **GameObject must exist at runtime**

### After (GameObject Only for Authoring)
1. Create BulletSpawnPoint GameObject
2. Make it child of PlayerShip
3. Position it where bullets should spawn
4. Assign to `bulletSpawnPoint` field
5. **GameObject only needed for baking** - can be removed from runtime scene!

The offset is captured at bake time and stored in the entity component.

## Compatibility

### Breaking Changes
- ✅ **None for designers** - Same workflow in Editor
- ✅ **None for users** - Bullets spawn at same position
- ⚠️ **Code change only** - Internal implementation changed

### Scene Updates Required
- ❌ **No scene changes needed**
- The component structure changed but Unity auto-migrates
- Existing scenes will re-bake automatically

## Testing Verification

✅ **Position Test**: Bullets spawn at spawn point position
✅ **Rotation Test**: Bullets fire in spawn point's forward direction
✅ **Following Test**: Spawn point moves with PlayerShip entity
✅ **Rotation Test**: Spawn point rotates with PlayerShip entity
✅ **Offset Test**: If ship rotates, offset rotates too (e.g., shoots from side when ship tilts)

### Expected Console Logs
```
[BulletShooterSystem] Fired bullet at position (x,y,z), velocity (vx,vy,vz)
```
Position should match the spawn point's world position and update as ship moves.

## Future Optimizations

Since the component is now unmanaged, we could:

1. **Burst-compile the system** (requires removing Debug.Log calls)
2. **Use IJobEntity** for parallel bullet spawning (if multiple ships)
3. **Pre-calculate forward direction** at bake time for fixed spawn rotations

## Files Changed

1. **PlayerShipAuthoring.cs**
   - Changed `BulletSpawnPointReference` from class to struct
   - Added `localOffset` and `localRotation` fields
   - Updated Baker to calculate offset from child GameObject

2. **BulletShooterSystem.cs**
   - Removed managed component access (`GetComponentObject`)
   - Added `LocalTransform` and `BulletSpawnPointReference` to query
   - Calculate spawn position/rotation from entity transform + offset
   - Removed UnityEngine.Transform dependency

3. **Documentation**
   - This file

---

**Implementation Date**: May 7, 2026  
**Status**: ✅ Complete and tested  
**Breaking Changes**: None (backwards compatible workflow)  
**Performance Impact**: Slight improvement (pure ECS)

