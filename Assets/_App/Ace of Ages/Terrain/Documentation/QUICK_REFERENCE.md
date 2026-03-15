# Quick Reference Card - Terrain System

## 🚀 Quick Start (5 minutes)

1. **Create TerrainConfig GameObject**
   ```
   Hierarchy → Right-click → Create Empty → Name: "TerrainConfig"
   ```

2. **Add Component**
   ```
   Add Component → TerrainConfigAuthoring
   ```

3. **Set Values**
   ```
   Tile Size: 100
   View Distance: 300
   Vertices Per Side: 32
   Floating Origin Enabled: ✓
   Shift Threshold: 2000
   ```

4. **Play!**
   - Terrain spawns around player automatically

## 📁 Files Overview

| File | Purpose | Lines |
|------|---------|-------|
| FloatingOriginComponents.cs | Data structures | 39 |
| TileComponents.cs | Tile & mesh data | 88 |
| FloatingOriginSystem.cs | World shifting | 74 |
| TileSpawningSystem.cs | Tile lifecycle | 170 |
| TerrainMeshGenerationSystem.cs | Procedural generation | 258 |
| TerrainRenderingSystem.cs | Rendering setup | 177 |
| TerrainPhysicsSystem.cs | Collision setup | 118 |
| TerrainConfigAuthoring.cs | Editor config | 116 |
| FloatingOriginEnabledAuthoring.cs | Player tag | 17 |

## 🎯 Key Components

```csharp
// Singleton - Configuration
TerrainTileConfig
  - tileSize: 100m
  - viewDistance: 300m  
  - verticesPerSide: 32
  - noise parameters

// Singleton - Floating Origin
FloatingOriginConfig
  - enabled: true
  - shiftThreshold: 2000m

// Singleton - Offset Tracking
WorldOriginOffset
  - accumulatedOffset: double3

// Per-Tile - Identification
TerrainTile
  - gridCoordinate: int2
  - meshGenerated: bool

// Tag - Origin Shifting
FloatingOriginEnabled
  (add to player)
```

## 🔧 Common Tasks

### Change Terrain Height
```csharp
// In TerrainConfigAuthoring:
noiseAmplitude = 50; // Taller mountains
```

### Change Detail Level
```csharp
// In TerrainConfigAuthoring:
verticesPerSide = 64; // More detailed (slower)
verticesPerSide = 16; // Less detailed (faster)
```

### Change View Distance
```csharp
// In TerrainConfigAuthoring:
viewDistance = 500; // More tiles visible
viewDistance = 200; // Fewer tiles (faster)
```

### Add Custom Material
1. Create Material in `Assets/_App/Ace of Ages/Terrain/Resources/`
2. Name it **TerrainMaterial**
3. Set shader to: Universal Render Pipeline/Lit
4. Add texture to Base Map

### Enable Floating Origin for Player
Add this to your player GameObject:
```csharp
FloatingOriginEnabledAuthoring component
```

## 🐛 Troubleshooting

| Problem | Solution |
|---------|----------|
| No terrain appears | Check Console for errors; verify TerrainConfigAuthoring in scene |
| No collision | Player needs Rigidbody; check TerrainPhysicsSystem is running |
| Performance slow | Reduce verticesPerSide to 16, viewDistance to 200 |
| Terrain "jumps" | Player needs FloatingOriginEnabled component |

## 📊 Performance Targets

| Setting | Tiles | Memory | Frame Time |
|---------|-------|--------|------------|
| Low (16 verts, 200m) | ~15 | 0.5MB | < 0.5ms |
| Medium (32 verts, 300m) | ~30 | 1.5MB | < 1.0ms |
| High (64 verts, 500m) | ~80 | 8.0MB | < 2.0ms |

## 🔍 Debug Console Messages

```
[TerrainRendering] Created default URP Lit material
→ Normal startup message

[FloatingOrigin] World shifted by (1500, 0, 200), accumulated offset: (1500, 0, 200)
→ World origin was reset (player moved too far)
```

## 📝 System Execution Order

1. **TileSpawningSystem** - Spawns tiles around player
2. **TerrainMeshGenerationSystem** - Fills mesh buffers
3. **TerrainPhysicsSystem** - Creates colliders
4. **TerrainRenderingSystem** - Sets up rendering
5. **FloatingOriginSystem** - Monitors & shifts world

## 🎮 Testing Checklist

- [ ] Terrain spawns when entering Play Mode
- [ ] Tiles appear around player position
- [ ] Player can walk on terrain (collision works)
- [ ] Tiles disappear when player moves away
- [ ] No errors in Console
- [ ] Frame rate is acceptable (60+ FPS)

## 📚 Documentation Files

- **README.md** - Complete technical documentation
- **SETUP_GUIDE.md** - Step-by-step setup instructions
- **IMPLEMENTATION_SUMMARY.md** - What was built
- **QUICK_REFERENCE.md** - This file

## 💡 Tips

- Start with default settings, tune later
- Use Scene view during Play Mode to see tiles
- Enable Gizmos to see view distance sphere
- Check Entities Hierarchy window to see active tiles
- Use Stats window to monitor draw calls

## 🚀 Next Steps

1. Test basic functionality
2. Add custom material (optional)
3. Test floating origin (move far)
4. Tune performance settings
5. Consider adding enhancements (LOD, biomes, etc.)

---

**Need Help?** See SETUP_GUIDE.md for detailed instructions
**Want Details?** See README.md for complete documentation

