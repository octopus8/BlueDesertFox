# Entity Transform + Offset - Quick Reference

## What Changed

Bullets now spawn using the **PlayerShip entity's transform + a baked offset**, instead of reading from a GameObject Transform at runtime.

## Quick Setup (No Changes Required!)

Your existing setup still works! The system now:

1. **At Bake Time**: Captures the spawn point's local position/rotation
2. **At Runtime**: Applies that offset to the ship entity's transform

## Troubleshooting

### Bullets spawn from wrong position

**Check**:
1. Is `bulletSpawnPoint` GameObject assigned in PlayerShipAuthoring Inspector?
2. Is `bulletSpawnPoint` a **child** of the PlayerShip GameObject?
3. Is the local position of `bulletSpawnPoint` correct?

**Fix**:
- Open PlayerShip in Editor
- Select bulletSpawnPoint child GameObject
- Set its **local** position relative to parent (e.g., `0, 0, 2` for 2 units forward)
- Re-bake the scene (close and reopen SubScene)

### Bullets spawn from (0,0,0) or origin

**Cause**: `bulletSpawnPoint` is null or not assigned

**Fix**:
1. Create a child GameObject under PlayerShip
2. Name it "BulletSpawnPoint"
3. Position it where bullets should spawn (local position)
4. Assign it to `PlayerShipAuthoring.bulletSpawnPoint` field
5. Re-bake scene

### Bullets fire in wrong direction

**Cause**: Spawn point rotation is incorrect

**Fix**:
1. Select bulletSpawnPoint GameObject in Editor
2. Rotate it so the **blue Z-axis** (forward) points in the firing direction
3. Verify local rotation is set correctly
4. Re-bake scene

## How It Works

### Bake Time (Editor)

```
PlayerShip GameObject at (10, 5, 20)
└── BulletSpawnPoint at localPosition (0, 0, 2)
    
↓ Baker captures ↓

BulletSpawnPointReference component:
- localOffset = (0, 0, 2)
- localRotation = identity
```

### Runtime (Gameplay)

```
PlayerShip entity at Position (10, 5, 20), Rotation (0°, 45°, 0°)
    
↓ System calculates ↓

Bullet spawn position = shipPos + rotate(shipRot, localOffset)
                     = (10, 5, 20) + rotate(45°, (0, 0, 2))
                     = (10, 5, 20) + (1.414, 0, 1.414)
                     = (11.414, 5, 21.414)
```

**Result**: Bullet spawns 2 units forward from ship in world space!

## Benefits

✅ **Follows entity perfectly** - No sync issues  
✅ **Rotates with ship** - Offset rotates too  
✅ **Pure ECS** - No GameObject dependencies at runtime  
✅ **Better performance** - No cross-boundary calls  
✅ **Same workflow** - Setup in Editor is unchanged  

## Testing Tips

1. **Move the ship** - Bullets should spawn from the moving ship
2. **Rotate the ship** - Bullets should fire in the ship's facing direction
3. **Check offset** - Bullets should spawn at the spawn point's position
4. **Check Console** - Look for: `[BulletShooterSystem] Fired bullet at position...`

## Debug Console Logs

Expected output when firing:
```
[BulletShooterSystem] Fired bullet at position (x,y,z), velocity (vx,vy,vz)
```

Position should update as ship moves.

## Common Mistakes

❌ **bulletSpawnPoint is NOT a child** - Offset will be wrong  
❌ **Using world position** - Should use **local** position  
❌ **Not re-baking after changes** - Scene must be re-baked to update offset  

✅ **bulletSpawnPoint IS a child** - Correct  
✅ **Using local position** - Correct  
✅ **Re-baking after changes** - Correct  

---

**Implementation**: May 7, 2026  
**Status**: ✅ Ready to use  
**Breaking Changes**: None

