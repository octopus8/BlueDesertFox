# Quick Setup Guide - Infinite Terrain System

## Step 1: Add TerrainConfigAuthoring to Your Scene

1. Open the Ace of Ages scene
2. Create a new GameObject: Right-click in Hierarchy → Create Empty
3. Name it "TerrainConfig"
4. Add Component → Search "TerrainConfigAuthoring" → Add it

## Step 2: Configure Terrain Settings

In the Inspector for TerrainConfig, set these recommended values:

### Tile Settings
- **Tile Size**: 100
- **View Distance**: 300
- **Vertices Per Side**: 32

### Floating Origin
- **Floating Origin Enabled**: ✓ (checked)
- **Shift Threshold**: 2000

### Procedural Noise Settings
- **Noise Frequency**: 0.01
- **Noise Amplitude**: 20
- **Noise Octaves**: 4
- **Noise Lacunarity**: 2.0
- **Noise Persistence**: 0.5

### Material
- Leave empty - system will auto-create a default material

## Step 3: Ensure Player Setup

Your player needs two components (likely already present):
1. **PlayerTag** - Already on your player via PlayerTagAuthoring
2. **FloatingOriginEnabled** - Add this manually if testing floating origin

To add FloatingOriginEnabled to the player:
- If player is in SubScene: Add a new authoring component that adds FloatingOriginEnabled
- If player is in main scene: The system will need to handle this at runtime

## Step 4: Optional - Create Custom Material

If you want a textured terrain:

1. Create folder: `Assets/_App/Ace of Ages/Terrain/Resources/`
2. Create Material: Right-click → Create → Material
3. Name it **exactly** "TerrainMaterial"
4. Set Shader to: Universal Render Pipeline → Lit
5. Add a texture to Base Map (e.g., a grass/rock texture)

## Step 5: Test the System

### Play Mode Test:
1. Enter Play Mode
2. Move your player around with VR controllers or keyboard
3. Watch the Console for terrain system messages
4. Tiles should spawn around the player as you move

### Expected Console Messages:
```
[TerrainRendering] Created default URP Lit material
```

### Visual Verification:
- Open Scene view during Play Mode
- You should see gray terrain tiles appearing around the player
- Tiles disappear when player moves away from them

## Troubleshooting

### "No terrain appears"
**Check:**
- Is TerrainConfig GameObject in the scene?
- Does the scene have a Player entity with PlayerTag?
- Check Console for errors

**Fix:**
- Verify TerrainConfigAuthoring component is attached
- Ensure player has PlayerTagAuthoring component

### "Terrain appears but no collision"
**Check:**
- Is TerrainPhysicsSystem running? (Check Entities Hierarchy window)
- Does player have physics components?

**Fix:**
- Verify Unity.Physics package is installed (should be)
- Check that player has Rigidbody component

### "Performance is slow"
**Reduce these values:**
- Vertices Per Side: Try 16 instead of 32
- View Distance: Try 200 instead of 300
- Noise Octaves: Try 2 instead of 4

### "Floating origin shift causes jerky movement"
**This shouldn't happen but if it does:**
- Ensure player has FloatingOriginEnabled component
- Check that shift threshold is high enough (2000+ recommended)
- Verify WorldOriginOffset is being accumulated correctly

## Understanding the System

### How Tiles Work:
- Each tile is 100m × 100m (if Tile Size = 100)
- Tiles spawn in a circle around the player (View Distance)
- As player moves, old tiles are destroyed and new ones spawn

### How Floating Origin Works:
- When player is > 2000m from (0,0,0), world shifts
- All entities with FloatingOriginEnabled move back toward origin
- Terrain generation accounts for this shift (no visible change)
- Prevents floating-point precision errors at large distances

### Performance:
With default settings (100m tiles, 300m view distance):
- Active tiles: ~30-40 tiles
- Memory per tile: ~50KB
- Total memory: ~2MB
- Frame time: < 1ms for tile management

## Next Steps

### Test Floating Origin:
1. Increase player movement speed drastically (for testing)
2. Move 2000+ meters in one direction
3. Watch Console for: `[FloatingOrigin] World shifted by...`
4. Verify terrain doesn't change appearance after shift

### Add Player FloatingOriginEnabled:
Create a simple authoring component:
```csharp
public class PlayerFloatingOriginAuthoring : MonoBehaviour
{
    public class Baker : Baker<PlayerFloatingOriginAuthoring>
    {
        public override void Bake(PlayerFloatingOriginAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<FloatingOriginEnabled>(entity);
        }
    }
}
```
Add this to your player GameObject.

### Customize Appearance:
1. Create a terrain material with your desired texture
2. Adjust noise parameters for different terrain shapes:
   - Higher frequency = more bumpy
   - Higher amplitude = taller mountains
   - More octaves = more detail (but slower)

### Add Features:
- See README.md "Future Enhancements" section
- Consider adding biomes, vegetation, or water
- Implement texture splatting based on height/slope

## System Architecture Reference

```
TerrainConfigAuthoring (MonoBehaviour in scene)
    ↓ Bakes to →
TerrainTileConfig + FloatingOriginConfig + WorldOriginOffset (Singletons)
    ↓ Used by →
Systems:
1. FloatingOriginSystem → Monitors player, triggers world shifts
2. TileSpawningSystem → Spawns/despawns tiles around player
3. TerrainMeshGenerationSystem → Generates procedural meshes
4. TerrainRenderingSystem → Creates Unity meshes, sets up rendering
5. TerrainPhysicsSystem → Creates colliders for physics
```

## Files Created

✅ FloatingOriginComponents.cs - Data structures for floating origin
✅ TileComponents.cs - Data structures for tiles and mesh data
✅ FloatingOriginSystem.cs - World origin shifting logic
✅ TileSpawningSystem.cs - Tile lifecycle management
✅ TerrainMeshGenerationSystem.cs - Procedural mesh generation
✅ TerrainRenderingSystem.cs - Rendering setup
✅ TerrainPhysicsSystem.cs - Collision setup
✅ TerrainConfigAuthoring.cs - Unity Editor configuration
✅ README.md - Detailed documentation
✅ SETUP_GUIDE.md - This file

All systems are Burst-compiled where possible for maximum performance!

