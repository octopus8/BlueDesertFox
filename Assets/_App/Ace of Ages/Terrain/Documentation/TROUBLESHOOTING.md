# Troubleshooting Guide - Common Issues and Solutions

Comprehensive problem-solving guide for the terrain system.

## Quick Diagnostic Checklist

```
✅ Is TerrainConfigAuthoring in a SubScene?
✅ Is player GameObject active in scene?
✅ Is Unity DOTS (Entities package) installed?
✅ Does console show any red errors?
✅ Is camera far clip plane > view distance?
```

## Issue 1: No Tiles Spawning

### Symptoms
- Scene runs but no terrain appears
- Terrain Status Inspector shows zero active tiles in play mode

### Diagnostic Steps

**Step 1**: Check Player Tracking
```
Window → Terrain → Status Inspector (play mode)
Check Console for [PlayerTrackingInitSystem] warnings
```

**Step 2**: Check SubScene
```
TerrainConfig must be inside SubScene (folder icon)
SubScene must be closed (not editing)
```

**Step 3**: Check Configuration
```
Tile Size > 0
View Distance > Tile Size
Vertices Per Side > 2
```

### Solutions

**Solution 1**: Move to SubScene
```
Right-click TerrainConfig → New SubScene From Selection
Save SubScene, close it, restart scene
```

**Solution 2**: Fix Player Tracking
```
Change Player Search Mode to FindMainCamera
See: [Player Tracking Setup](PLAYER_TRACKING.md)
```

**Solution 3**: Check Player Position
```
Move player closer to origin (0, 0, 0)
Extreme coordinates (>100,000) cause issues
```

---

## Issue 2: Terrain Not Rendering

### Symptoms
- Terrain Status Inspector or ECS queries show tiles exist
- No visible terrain in Game view

### Diagnostic Steps

**Step 1**: Check Material
```
Look for console: "Failed to find any suitable shader!"
Create: Resources/TerrainMaterial.mat
Shader: Universal Render Pipeline/Lit
```

**Step 2**: Check Camera
```
Culling Mask: Includes "Default" layer
Far Clip Plane: > View Distance
```

**Step 3**: Check Lighting
```
Ensure scene has Directional Light
Test with Unlit shader to isolate issue
```

### Solutions

**Solution 1**: Create Material
```
Assets/Create → Material → TerrainMaterial
Location: Assets/_App/Ace of Ages/Terrain/Resources/
Shader: URP/Lit
```

**Solution 2**: Fix Camera
```
Far Clip: 1000m (if View Distance = 500m)
Culling Mask: Everything or Default included
```

---

## Issue 3: Player Tracking Fails

### Symptoms
- Console: "Could not find player GameObject!"
- Or: "Transform is null"

### Diagnose Player Tracking

```
Window → Terrain → Status Inspector (play mode)
Review Console for [PlayerTrackingInitSystem] warnings/errors
```

See [Player Tracking Setup](PLAYER_TRACKING.md) for search mode configuration.

### Solutions

**Solution 1**: Use AutoDetect
```
Player Search Mode: AutoDetect
```

**Solution 2**: Use FindMainCamera
```
Player Search Mode: FindMainCamera
Tag camera as "MainCamera"
```

**Solution 3**: Fix Name
```
Player Search Mode: FindByName
Player Name: Exact GameObject name (case-sensitive)
```

**See**: [Player Tracking Setup](PLAYER_TRACKING.md)

---

## Issue 4: Performance Issues

### Symptoms
- Frame rate below target (90fps VR, 60fps desktop)
- Stuttering when moving

### Solutions

**Solution 1**: Reduce Complexity
```
Vertices Per Side: 16 (reduce from 32)
Noise Octaves: 3 (reduce from 4)
```

**Solution 2**: Reduce View Distance
```
View Distance: 300m (reduce from 500m)
Impact: 64% fewer tiles
```

**Solution 3**: Increase Frame Budgets
```
Max Colliders Per Frame: 5 (increase from 3)
Trade-off: Faster but potential brief spikes
```

**Solution 4**: Reduce collider cost
```
Max Collider Distance: 200m (reduce from 450m)
Vertices Per Side: 32 (reduce from 48)
Fewer tiles with colliders and lower triangle count per collider
```

**See**: [Performance Optimization](PERFORMANCE.md)

---

## Issue 5: Physics Problems

### Colliders Not Creating

**Check**:
```
Is Unity.Physics package installed?
Are tiles within maxColliderDistance?
Check console for collider errors
```

### Performance Spikes

**Solution**: Reduce frame budget
```
Max Colliders Per Frame: 2 (reduce from 3)
```

---

## Issue 6: Auto-Scrolling Problems

### Terrain Not Scrolling

**Check**:
```
✅ Scroll Enabled = true
✅ Scroll Speed ≠ 0
✅ Player forward direction valid
```

### Wrong Direction

**Solution**: Player forward determines direction
```
Check player GameObject blue arrow in Scene view
Rotate player to change scroll direction
```

### Gaps in Terrain

**Cause**: Speed too high, tiles can't spawn fast enough

**Solutions**:
```
Reduce scroll speed: 5.0 (was 50.0)
Increase view distance: 800m (was 500m)
```

**See**: [Auto-Scrolling Guide](AUTO_SCROLLING.md)

---

## Debug Tools

See **[Debug Tools](DEBUG_TOOLS.md)** for the full guide. Quick reference:

### Terrain Status Inspector
```
Window → Terrain → Status Inspector
Material, URP, package checks; play-mode tile and tracking status
```

### Console Logs
```
Filter: [Terrain] or [PlayerTracking]
Warnings and errors only — success paths are silent
```

### StaticObjectCleanupDebugSystem
```
Automatic LogWarning if orphaned static objects detected after tile despawn
```

---

## Getting Help

When reporting issues, include:
```
1. Unity version
2. Console errors (full stack trace)
3. Terrain Status Inspector screenshot or Console warnings/errors
4. Configuration screenshot
5. Platform (VR/desktop, GPU model)
```

---

## Related Documentation

- **[Quick Start Guide](QUICK_START.md)** - Proper setup
- **[Player Tracking Setup](PLAYER_TRACKING.md)** - Tracking configuration
- **[Debug Tools](DEBUG_TOOLS.md)** - Using diagnostic tools
- **[Performance Optimization](PERFORMANCE.md)** - Performance issues

---

**Back to**: [Documentation Hub](README.md)

