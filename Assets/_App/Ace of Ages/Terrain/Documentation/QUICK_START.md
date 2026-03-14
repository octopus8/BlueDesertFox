# Infinite Terrain System - Quick Start Guide

**Last Updated:** March 14, 2026  
**Difficulty:** Beginner  
**Time Required:** 10-15 minutes

## Prerequisites

- Unity 6 (2023.3 or later)
- Universal Render Pipeline (URP) installed
- Unity DOTS packages installed:
  - Unity.Entities
  - Unity.Entities.Graphics
  - Unity.Physics
  - Unity.Mathematics

---

## Step-by-Step Setup

### Step 1: Create Scene Structure

1. **Open or create a scene** in Unity Editor
2. **Create a new SubScene** (recommended for ECS entities):
   - Right-click in Hierarchy
   - New SubScene → Empty Scene
   - Name it "TerrainSubScene"

### Step 2: Add Terrain Configuration

1. **Create a new GameObject:**
   - Right-click in Hierarchy (inside SubScene)
   - Create Empty
   - Name: "TerrainConfig"

2. **Add the TerrainConfigAuthoring component:**
   - Select "TerrainConfig" GameObject
   - Click "Add Component" in Inspector
   - Search: "TerrainConfigAuthoring"
   - Click to add

3. **Configure terrain settings:**

   **Basic Settings (Good Defaults):**
   ```
   Tile Settings:
   ├─ Tile Size: 100
   ├─ View Distance: 300
   └─ Vertices Per Side: 32
   
   Floating Origin:
   ├─ Floating Origin Enabled: ☑ (checked)
   └─ Shift Threshold: 2000
   
   Procedural Noise Settings:
   ├─ Noise Frequency: 0.01
   ├─ Noise Amplitude: 20
   ├─ Noise Octaves: 4
   ├─ Noise Lacunarity: 2.0
   └─ Noise Persistence: 0.5
   
   Material:
   └─ Terrain Material: (leave empty for auto-creation)
   ```

   **For Performance (VR/Low-End):**
   ```
   Tile Size: 100
   View Distance: 200
   Vertices Per Side: 16
   Noise Octaves: 2
   ```

   **For Visual Quality:**
   ```
   Tile Size: 50
   View Distance: 500
   Vertices Per Side: 64
   Noise Octaves: 6
   ```

### Step 3: Tag Your Player

1. **Find your player GameObject** in Hierarchy
   - Usually named "XR Origin", "Player", or "Camera Offset"
   - Should be the object that moves through the world

2. **Add PlayerTagAuthoring component:**
   - Select player GameObject
   - Add Component → "PlayerTagAuthoring"

3. **Add FloatingOriginEnabledAuthoring component:**
   - Select player GameObject
   - Add Component → "FloatingOriginEnabledAuthoring"
   - This ensures player shifts with the world origin

### Step 4: Create Terrain Material (Optional)

The system will auto-create a material if you don't provide one, but you can create a custom one:

1. **Create material:**
   - Right-click in Project: `Assets/Resources/`
   - Create → Material
   - Name: "TerrainMaterial"

2. **Configure material:**
   - Shader: "Universal Render Pipeline/Lit"
   - Base Map: (optional texture)
   - Base Color: Choose terrain color (e.g., greenish for grass)
   - Smoothness: 0.2 (slightly rough)
   - Metallic: 0 (non-metallic)

3. **Assign to TerrainConfigAuthoring:**
   - Select TerrainConfig GameObject
   - Drag material to "Terrain Material" field

**Note:** If you skip this, the system will automatically create a material on first run.

### Step 5: Close SubScene

1. **Close the SubScene** to finalize baking:
   - In Hierarchy, click the "X" button next to SubScene name
   - Wait for SubScene to finish baking (progress bar in bottom-right)

2. **Verify baking succeeded:**
   - No errors in Console
   - SubScene shows "Closed" in Hierarchy

### Step 6: Test in Play Mode

1. **Enter Play Mode** (click Play button)

2. **Check Console for startup messages:**
   ```
   [TileSpawning] Spawning 9 new tiles
   [TerrainMeshGen] Generating mesh for tile at (0, 0)
   [TerrainMeshGen] ✓ Mesh generated: 1024 vertices, 1922 triangles for tile at (0, 0)
   [TerrainRendering] Processing 9 tiles for rendering setup
   ```

3. **Look around in Scene View:**
   - Navigate to world origin (0, 0, 0)
   - You should see terrain tiles surrounding the player
   - Terrain should be greenish/grayish color

4. **Test walking:**
   - Move player around (in Game View or Scene View)
   - Watch Console for new tiles spawning
   - Tiles should spawn/despawn as you move

### Step 7: Verify Systems Are Running

1. **Open Entities window:**
   - Window → DOTS → Systems

2. **Find terrain systems:**
   - Search for "Terrain" or "Tile"
   - Should see:
     - TileSpawningSystem ✓
     - TerrainMeshGenerationSystem ✓
     - TerrainPhysicsSystem ✓
     - TerrainRenderingSystem ✓
     - FloatingOriginSystem ✓

3. **Check they're enabled:**
   - Green checkmark = running
   - Gray = disabled (check requirements)

---

## Troubleshooting Quick Reference

### Problem: No Terrain Visible

**Check:**
1. ✅ Is TerrainConfigAuthoring in scene?
2. ✅ Is SubScene closed (baked)?
3. ✅ Does player have PlayerTag?
4. ✅ Is camera near player position?
5. ✅ Any errors in Console?

**Quick Fix:**
- Open Tools → Terrain → Create Terrain Material
- Restart Unity Editor
- Rebuild SubScene (right-click SubScene → Rebuild)

### Problem: Systems Not Running

**Check:**
1. Window → DOTS → Systems
2. Expand SimulationSystemGroup
3. Find TileSpawningSystem
4. If grayed out: Check "Required Components" section
   - Needs: PlayerTag, TerrainTileConfig, WorldOriginOffset

**Quick Fix:**
- Ensure TerrainConfig GameObject is in SubScene (not regular scene)
- Ensure SubScene is closed
- Ensure player has PlayerTagAuthoring

### Problem: Poor Performance

**Reduce settings:**
```
Vertices Per Side: 32 → 16
View Distance: 500 → 250
Noise Octaves: 4 → 2
```

**Check in Profiler:**
- Window → Analysis → Profiler
- Look for TerrainMeshGenerationSystem spikes
- Should be <2ms per frame average

### Problem: Terrain "Pops" or Changes

**Symptom:** Terrain looks different after walking far

**Cause:** Floating origin not working correctly

**Fix:**
1. Check FloatingOriginConfig.enabled = true
2. Check player has FloatingOriginEnabled tag
3. Check Console for "[FloatingOrigin] World shifted" messages

---

## Configuration Presets

### Preset 1: VR Performance
**Use Case:** Quest 2/3, mobile VR, 72/90 FPS target

```
Tile Size:           100
View Distance:       200
Vertices Per Side:   16
Noise Frequency:     0.015
Noise Amplitude:     15
Noise Octaves:       2
Noise Lacunarity:    2.0
Noise Persistence:   0.4
```

**Expected:**
- ~12 active tiles
- ~5ms per frame
- Low detail, smooth performance

### Preset 2: Desktop Balanced
**Use Case:** PC VR, PCVR, 90 FPS target

```
Tile Size:           100
View Distance:       400
Vertices Per Side:   32
Noise Frequency:     0.01
Noise Amplitude:     20
Noise Octaves:       4
Noise Lacunarity:    2.0
Noise Persistence:   0.5
```

**Expected:**
- ~50 active tiles
- ~10ms per frame (when spawning)
- Good balance of detail and performance

### Preset 3: Desktop High Quality
**Use Case:** High-end PC, 60 FPS target, screenshot mode

```
Tile Size:           50
View Distance:       500
Vertices Per Side:   64
Noise Frequency:     0.008
Noise Amplitude:     30
Noise Octaves:       6
Noise Lacunarity:    2.2
Noise Persistence:   0.55
```

**Expected:**
- ~314 active tiles
- ~25ms per frame (when spawning heavily)
- Maximum visual quality
- May drop frames during heavy spawning

### Preset 4: Smooth Rolling Hills
**Use Case:** Gentle landscape, racing games, exploration

```
Tile Size:           100
View Distance:       400
Vertices Per Side:   32
Noise Frequency:     0.005
Noise Amplitude:     10
Noise Octaves:       2
Noise Lacunarity:    1.8
Noise Persistence:   0.3
```

**Visual Style:** Gentle slopes, easy traversal, calming

### Preset 5: Mountainous Terrain
**Use Case:** Climbing games, dramatic vistas

```
Tile Size:           100
View Distance:       600
Vertices Per Side:   48
Noise Frequency:     0.015
Noise Amplitude:     80
Noise Octaves:       5
Noise Lacunarity:    2.5
Noise Persistence:   0.6
```

**Visual Style:** Steep mountains, deep valleys, challenging terrain

---

## Testing Checklist

After setup, verify each item:

- [ ] TerrainConfigAuthoring exists in scene
- [ ] SubScene is closed (baked)
- [ ] Player has PlayerTagAuthoring component
- [ ] Player has FloatingOriginEnabledAuthoring component
- [ ] No errors in Console
- [ ] Enter Play Mode
- [ ] Console shows "[TileSpawning] Spawning X new tiles"
- [ ] Console shows "[TerrainMeshGen] Generating mesh"
- [ ] Console shows "[TerrainRendering] Processing X tiles"
- [ ] Terrain is visible in Scene View around origin
- [ ] Moving player spawns new tiles
- [ ] Moving away despawns old tiles
- [ ] Can walk on terrain (physics collision works)
- [ ] Performance is acceptable (check FPS in Game View stats)

---

## Next Steps

Once basic terrain is working:

1. **Customize appearance:**
   - Create custom terrain material with textures
   - Adjust noise parameters for desired terrain style
   - Add normal maps for visual detail

2. **Tune performance:**
   - Profile with Unity Profiler
   - Adjust vertices per side based on frame budget
   - Reduce view distance if too many tiles active

3. **Add features:**
   - Vegetation spawning system
   - Dynamic terrain deformation
   - LOD system for distant tiles
   - Biome transitions

4. **Read advanced documentation:**
   - [SYSTEM_ARCHITECTURE.md](SYSTEM_ARCHITECTURE.md) - Deep system understanding
   - [TECHNICAL_DETAILS.md](TECHNICAL_DETAILS.md) - Implementation details
   - [API_REFERENCE.md](API_REFERENCE.md) - Complete API documentation

---

## Common Questions

### Q: Can I have multiple terrain configurations?

**A:** Not currently. The system uses singletons, so only one configuration is active. To have multiple terrains, you'd need to:
- Add a TerrainID component to distinguish terrain types
- Modify systems to process each terrain type separately
- Create multiple material references

### Q: Can terrain wrap around (planet surface)?

**A:** Not currently. The system generates infinite flat terrain. For spherical planets:
- Would need to modify vertex generation to use spherical coordinates
- Adjust noise sampling to use latitude/longitude
- Modify physics colliders for curved surfaces

### Q: Can I use this with Unity's built-in Terrain system?

**A:** No, they're incompatible. This is a fully custom ECS-based system. Benefits:
- Much better performance (DOTS)
- True infinite generation
- Floating origin support
- Full control over generation

### Q: How do I modify terrain at runtime?

**A:** Currently not supported. To implement:
1. Store height modifications in a buffer component
2. Apply modifications during mesh generation
3. Set `tile.needsRegeneration = true` to trigger update
4. Would need to implement a system to handle player edits

### Q: Can I export terrain to .obj file?

**A:** Yes, write a custom system:
```csharp
var mesh = EntityManager.GetComponentData<MeshReference>(entity).mesh;
// Use Unity's OBJ exporter or write custom serializer
```

### Q: Does this work with Netcode for Entities?

**A:** Not tested, but theoretically yes. Considerations:
- Only sync PlayerTag entity position (clients generate own terrain)
- Ensure same seed/configuration on all clients
- Terrain modifications would need explicit networking

---

## Support

For issues, refer to:
- [COMPLETE_SOLUTION_SUMMARY.md](../COMPLETE_SOLUTION_SUMMARY.md) - Rendering issues
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Detailed problem solving
- Unity Forums: DOTS section
- Unity Discord: #dots channel

