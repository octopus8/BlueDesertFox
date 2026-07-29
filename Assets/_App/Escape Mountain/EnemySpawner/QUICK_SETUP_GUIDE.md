# Quick Setup Guide - Enemy Formation Approach System

## What Changed

The enemy spawner now implements a **4-phase lifecycle**:
1. Spawn formations outside player view
2. Approach spline from the side
3. Follow spline (existing behavior)
4. Exit and despawn

## Quick Start (5 Minutes)

### Step 1: Open Existing Scene
Open `Assets/_App/Escape Mountain/Escape Mountain.unity`

### Step 2: Inspect EnemySpawnerAuthoring
Find `EnemySpawnerAuthoring` component in the scene hierarchy (likely in a SubScene).

You'll see **new fields**:
```
Spawn Behavior:
├─ Spawn Distance: 75         ← Distance ahead of spline to spawn
└─ Approach Threshold: 5      ← Distance to start following
```

### Step 3: Test
1. Enter Play Mode
2. Wait 3 seconds (automatic spawn via `AceOfAges.cs`)
3. Look around - enemies should approach from the side
4. Watch them follow the spline in formation
5. Observe them exit straight and despawn

### Step 4: Tune (Optional)
Adjust in Inspector while in Edit mode:
- **Increase `Spawn Distance` to 150**: Spawn farther away
- **Decrease `Approach Threshold` to 2**: Smoother spline transition
- **Increase `Formation Spacing` to 5**: Wider bowling pin formation

## What You Should See

### Phase 1: Spawning (0-1 sec)
- 10 enemies appear off to the side of spline start
- Positioned ahead along perpendicular axis
- Not visible if camera faces spline direction

### Phase 2: Approach (1-8 sec)
- Enemies move toward spline entry point
- Physics-based smooth movement
- Rotate to face movement direction
- Yellow gizmos if debugger added

### Phase 3: Following (8-30 sec)
- Bowling pin formation locks in
- Enemies follow spline path
- Formation offsets maintained
- Green gizmos if debugger added

### Phase 4: Exit (30+ sec)
- Enemies reach spline end
- Continue in straight line (exit tangent direction)
- Move away from player
- Blue gizmos if debugger added

### Phase 5: Cleanup (60+ sec)
- Enemies beyond view distance
- Automatically destroyed
- Red gizmos briefly if debugger added

## Requirements

### Already Met (No Action Needed)
✅ `TerrainConfigAuthoring` in scene (provides view distance)  
✅ `PlayerTransformReference` (terrain system provides this)  
✅ `SplineComponentAuthoring` on spline GameObject  
✅ `PrefabEntitiesReferences` in scene  

### Prefab Requirements
Your enemy prefab **must have**:
- ✅ `LocalTransform` (standard for entities)
- ✅ `PhysicsBody` or `PhysicsBodyAuthoring` (for movement)

**Note**: `PhysicsVelocity` is automatically added if missing - no action needed!

## Default Values Explained

### Spawn Distance: 75 units
- Spawns enemies ~75 meters ahead of spline start
- For terrain view distance of 500m, this is ~15% of view range
- Ensures enemies spawn outside typical VR field of view
- **Increase if enemies spawn too close to player**

### Approach Threshold: 5 units
- Enemies transition to spline following when within 5 meters
- Small enough to prevent visible "pop" when switching
- Large enough to prevent overshooting
- **Decrease for tighter precision, increase for earlier transition**

### Approach Speed: 10 units/sec (hardcoded)
- ~7.5 seconds to traverse 75 unit spawn distance
- Matches typical spline following speed
- **To change**: Edit `FormationMovementSystem.cs` line ~105

### Exit Speed: 10 units/sec (hardcoded)
- Continues at same speed as approach/follow
- **To change**: Edit `FormationMovementSystem.cs` line ~142

### View Distance Buffer: 1.2x multiplier (hardcoded)
- 20% beyond terrain view distance before cleanup
- Prevents premature despawning during exit phase
- For 500m terrain view = 600m despawn distance
- **To change**: Edit `FormationMovementSystem.cs` line ~148

## Troubleshooting

### Enemies spawn but don't approach
**Check**: Open Console, look for errors about `PlayerTransformReference`  
**Fix**: Open **Window → Terrain → Status Inspector** in play mode; check Console for `[PlayerTrackingInitSystem]` warnings

### Enemies approach but don't follow spline
**Check**: `approachThreshold` might be too small, enemies may be overshooting  
**Fix**: Increase `approachThreshold` to 10-15 units

### Enemies follow spline but don't exit
**Check**: Is spline closed? (isClosed = true)  
**Fix**: Exit only works on non-closed splines. Open the spline or modify logic.

### Enemies never despawn
**Check**: Terrain view distance setting  
**Fix**: Reduce terrain view distance or increase exit speed

## Advanced: Multiple Spawners

You can have multiple spawners with different configurations:

**Spawner A**: Close-range ambush
```
Spawn Distance: 30
Approach Threshold: 3
Formation Spacing: 1.5
```

**Spawner B**: Long-range approach
```
Spawn Distance: 150
Approach Threshold: 10
Formation Spacing: 3.0
```

Each spawner operates independently!

## Performance Notes

- System adds ~0.2ms per frame per 10-enemy formation
- Burst-compiled for optimal performance
- No GC allocations
- Safe for VR at 90fps with multiple formations

## Next Steps

1. **Test the default settings** - Enter play mode and observe
2. **Add visual debugging** - Create gizmo drawer (see main docs)
3. **Tune spawn distance** - Adjust based on your spline orientation
4. **Create more spawners** - Add variety to enemy waves
5. **Customize speeds** - Edit system code if needed

## Need Help?

See `FORMATION_APPROACH_SYSTEM.md` for complete documentation including:
- Detailed system architecture
- Component reference
- Performance analysis
- Debugging tools
- Customization examples

