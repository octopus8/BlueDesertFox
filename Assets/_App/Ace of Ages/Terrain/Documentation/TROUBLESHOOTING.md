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
- TerrainTileGizmoVisualizer shows 0 tiles

### Diagnostic Steps

**Step 1**: Check Player Tracking
```
Add TerrainTrackingDebugger
Right-click → "Check Tracking Status"
Look for: "✅ Tracking: [player name]"
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
- Gizmo visualizer shows tiles (yellow wireframes)
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

### Use TerrainTrackingDebugger

```
Add component to GameObject
Right-click → Check Tracking Status
Review detailed console output
```

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

**Solution 4**: Optimize Physics LOD
```
Full Resolution Distance: 100m (reduce from 150m)
More tiles use cheaper colliders
```

**See**: [Performance Optimization](PERFORMANCE.md)

---

## Issue 5: Physics Problems

### Colliders Not Creating

**Check**:
```
Is Unity.Physics package installed?
Are tiles within LOD Quarter Distance?
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

### TerrainTrackingDebugger
```
Right-click component → Check Tracking Status
Shows player tracking state and tile counts
```

### TerrainTileGizmoVisualizer
```
Green: Tile exists, no mesh
Yellow: Tile has mesh data
Cyan: Tile has rendering
```

### Console Logs
```
Filter: [Terrain
Shows all terrain system messages
```

---

## Getting Help

When reporting issues, include:
```
1. Unity version
2. Console errors (full stack trace)
3. TerrainTrackingDebugger output
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

