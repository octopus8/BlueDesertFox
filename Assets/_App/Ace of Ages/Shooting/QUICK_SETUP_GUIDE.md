# Player Ship Shooting System - Quick Setup Guide

## 5-Minute Setup

### Step 1: Player Ship Configuration (2 minutes)

1. **Find player ship GameObject** (in SubScene or main scene)
2. **Add components**:
   - Verify `PlayerShipAuthoring` exists
   - Add `BulletShooterAuthoring`
3. **Configure spawn point**:
   - Create empty child GameObject named "BulletSpawnPoint"
   - Position it at the front of the ship (where bullets should spawn)
   - Assign it to `PlayerShipAuthoring.bulletSpawnPoint` field
4. **Configure shooting** (BulletShooterAuthoring Inspector):
   - Fire Rate: `0.2` (5 rounds/sec)
   - Bullet Speed: `100` (units/sec)

### Step 2: Pool Configuration (30 seconds)

1. **Find singleton GameObject** (e.g., TerrainConfig GameObject)
2. **Add component**: `BulletPoolConfigAuthoring`
3. **Use defaults**:
   - Initial Pool Size: `50`
   - Max Pool Size: `100`

### Step 3: Bullet Prefab Setup (1 minute)

1. **Create bullet prefab**:
   - Create GameObject with Mesh (e.g., Sphere, scaled to 0.1)
   - Add material (e.g., bright red/yellow)
   - Add `PhysicsShape` (Sphere, radius 0.05)
2. **Assign to references**:
   - Find `PrefabEntitiesReferencesAuthoring` in scene
   - Assign bullet prefab to `Bullet Simple Prefab` field

### Step 4: Input Action Setup (1 minute)

1. **Open Input Actions**: `Assets/InputSystem_Actions.inputactions`
2. **Add "Fire" action**:
   - Click + to add new action
   - Name: `Fire`
   - Action Type: `Button`
3. **Add binding**:
   - Click + on Fire action
   - Select "Add Binding"
   - Path: `XR Controller (Right Hand) > Primary Button`
4. **Save** (Ctrl+S)

### Step 5: Input Handler (30 seconds)

1. **Find player ship in MAIN scene** (not SubScene)
2. **Add component**: `PlayerShootingInput`
3. **Verify InputSystemActionsInitializer**:
   - Check if scene has this component
   - If not, add it to any GameObject
   - Assign `InputSystem_Actions` asset to it

## Testing Checklist

- [ ] Enter Play mode
- [ ] Press Fire button (right controller trigger)
- [ ] See bullets spawn from ship
- [ ] See bullets travel forward at high speed
- [ ] See bullets disappear after ~2 seconds (200m at 100 units/sec)
- [ ] Check Console for confirmation logs
- [ ] No errors in Console

## Common Issues

### Issue: "Fire action not found"
**Fix**: Make sure you created the "Fire" action in InputSystem_Actions and saved the asset

### Issue: "No entity found with BulletShooter"
**Fix**: BulletShooterAuthoring must be on the same GameObject as PlayerShipAuthoring

### Issue: Bullets don't spawn
**Fix**: Check that bulletSpawnPoint is assigned in PlayerShipAuthoring Inspector

### Issue: Bullets spawn but don't move
**Fix**: Check Bullet Speed is > 0 in BulletShooterAuthoring Inspector

### Issue: Pool exhausted warning
**Fix**: Bullets aren't being cleaned up fast enough - check that BulletLifecycleSystem and BulletCollisionSystem are running

## Optional: Physics Layer Setup

For better performance and collision filtering:

1. Create "Projectile" layer (Edit > Project Settings > Tags and Layers)
2. Configure collision matrix (Edit > Project Settings > Physics):
   - Projectile ✓ Terrain
   - Projectile ✓ Enemy
   - Projectile ✗ Projectile
   - Projectile ✗ Player
3. Set bullet prefab layer to "Projectile"

## Performance Notes

- **50 pre-spawned bullets** supports continuous fire for 10 seconds (5 rounds/sec)
- **200m cleanup distance** means bullets live for ~2 seconds at 100 units/sec
- **Pool grows dynamically** if you exhaust it (warning logged)
- **Zero GC** during gameplay (all systems use native collections)

## Files to Verify

```
✓ PlayerShipAuthoring.cs           - Has bulletSpawnPoint field
✓ BulletShooterAuthoring.cs         - Attached to player ship
✓ BulletPoolConfigAuthoring.cs      - Attached to singleton GameObject
✓ PlayerShootingInput.cs            - Attached to player ship in main scene
✓ PrefabEntitiesReferences          - Has bulletSimplePrefab assigned
✓ InputSystem_Actions               - Has "Fire" action with binding
✓ InputSystemActionsInitializer     - In scene with asset assigned
```

---

**Setup Time**: ~5 minutes  
**Ready to shoot!** 🚀

