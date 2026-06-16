# Configuration Reference - Terrain System

Complete guide to all configuration parameters in the TerrainConfigAuthoring component.

## Configuration Location

The terrain system is configured via the `TerrainConfigAuthoring` MonoBehaviour component, which must be placed on a GameObject inside an ECS SubScene.

**File**: `Assets/_App/Ace of Ages/Terrain/TerrainConfigAuthoring.cs`

## Configuration Sections

### Player Tracking

Controls how the terrain system finds and tracks the player GameObject.

#### Player Search Mode

**Type**: Enum  
**Default**: `AutoDetect`  
**Options**:

- `AutoDetect` - Use Camera.main (zero configuration)
- `FindByName` - Search by GameObject name
- `FindByTag` - Search by GameObject tag
- `FindMainCamera` - Use Camera.main explicitly

**When to use each:**

- **AutoDetect**: Best for most cases, uses main camera
- **FindByName**: Use when player has a specific name (e.g., "XR Origin Hands (XR Rig)")
- **FindByTag**: Use when player is tagged (e.g., "Player" tag)
- **FindMainCamera**: Equivalent to AutoDetect but explicit

#### Player Name

**Type**: String  
**Default**: `"XR Origin Hands (XR Rig)"`  
**Used When**: Player Search Mode = FindByName

**Example**: If your VR rig GameObject is named "MyVRPlayer", enter that here.

#### Player Tag

**Type**: String  
**Default**: `"Player"`  
**Used When**: Player Search Mode = FindByTag

**Example**: If your player has tag "MainPlayer", enter that here.

---

### Tile Settings

Controls the size and density of terrain tiles.

#### Tile Size

**Type**: Float  
**Default**: `100`  
**Units**: Meters  
**Range**: 1 - 1000

Size of each square terrain tile. Smaller tiles = more tiles for same view distance.

**Performance Impact**:

- Smaller tiles: More entities, more spawning overhead
- Larger tiles: Larger meshes, more generation time per tile

**Recommended Values**:

- VR: 100m (balanced)
- Desktop: 100-200m
- Mobile: 50-100m

#### View Distance

**Type**: Float  
**Default**: `500`  
**Units**: Meters  
**Range**: Must be ≥ Tile Size

Distance from player that tiles remain active. Circular culling area.

**Tile Count Formula**: Approximately `(viewDistance / tileSize)²` tiles

**Examples**:

- View Distance 300m, Tile Size 100m: ~9 tiles
- View Distance 500m, Tile Size 100m: ~25 tiles
- View Distance 1000m, Tile Size 100m: ~100 tiles

**Performance Impact**: Quadratic scaling! Doubling view distance = 4× tiles.

**Recommended Values**:

- VR High Performance: 300m
- VR Balanced: 500m
- Desktop: 800m+

#### Vertices Per Side

**Type**: Int  
**Default**: `32`  
**Range**: 2 - 256

Number of vertices along each edge of the tile mesh. Total vertices = `verticesPerSide²`

**Examples**:

- 16 vertices per side = 256 total vertices
- 32 vertices per side = 1024 total vertices
- 64 vertices per side = 4096 total vertices

**Performance Impact**: Quadratic scaling! Affects mesh generation, rendering, and physics.

**Recommended Values**:

- VR High Performance: 16
- VR Balanced: 32
- Desktop High Quality: 64
- Desktop Ultra: 128

---

### Auto-Scrolling

Controls automatic terrain scrolling for endless runner gameplay.

#### Scroll Enabled

**Type**: Bool  
**Default**: `false`

Enable/disable automatic terrain scrolling. When enabled, terrain scrolls in the direction the player is facing (XZ plane projection).

**How it works**:

- Player GameObject stays fixed in world space
- Terrain tiles physically move backward relative to scroll direction
- Tiles spawn ahead, despawn behind
- Creates endless runner effect without moving player (no VR motion sickness)

#### Scroll Speed

**Type**: Float  
**Default**: `5.0`  
**Units**: Meters per second  
**Range**: -100 to 100

Speed of terrain scrolling. Positive values scroll forward (in player's facing direction).

**Examples**:

- `5.0` - Walking speed (5 m/s = 18 km/h)
- `10.0` - Running speed (10 m/s = 36 km/h)
- `30.0` - Vehicle speed (30 m/s = 108 km/h)
- `-5.0` - Backward scrolling

**Performance Impact**: None - scrolling itself is essentially free.

**See**: [Auto-Scrolling Guide](AUTO_SCROLLING.md) for complete details.

---

### Procedural Noise Settings

Controls the appearance of terrain using multi-octave Perlin noise.

#### Noise Frequency

**Type**: Float  
**Default**: `0.01`  
**Range**: 0.0001 - 1.0

Base frequency of noise sampling. Lower = smoother/larger features, higher = rougher/smaller features.

**Examples**:

- `0.001` - Very smooth, rolling hills
- `0.01` - Default, natural terrain
- `0.05` - Rough, mountainous
- `0.1` - Very rough, rocky

#### Noise Amplitude

**Type**: Float  
**Default**: `20`  
**Units**: Meters  
**Range**: 0 - 1000

Maximum height variation of terrain features. This is the height range from lowest to highest points.

**Examples**:

- `5` - Gentle hills
- `20` - Default, moderate terrain
- `50` - Mountainous
- `100` - Extreme mountains

#### Noise Octaves

**Type**: Int  
**Default**: `4`  
**Range**: 1 - 8

Number of noise layers combined. More octaves = more detail at different scales.

**Examples**:

- `1` - Single noise layer, very smooth
- `4` - Default, good balance of large and small features
- `8` - Maximum detail, expensive

**Performance Impact**: Linear - each octave adds one noise sample per vertex.

#### Noise Lacunarity

**Type**: Float  
**Default**: `2.0`  
**Range**: 1.0 - 4.0

Frequency multiplier for each octave. Controls how quickly detail increases.

**Formula**: `frequency[octave] = baseFrequency × lacunarity^octave`

**Examples**:

- `2.0` - Default, each octave doubles frequency
- `3.0` - Each octave triples frequency (more high-frequency detail)

#### Noise Persistence

**Type**: Float  
**Default**: `0.5`  
**Range**: 0.0 - 1.0

Amplitude multiplier for each octave. Controls how much each detail layer contributes.

**Formula**: `amplitude[octave] = baseAmplitude × persistence^octave`

**Examples**:

- `0.5` - Default, each octave contributes half
- `0.3` - Less detail contribution (smoother)
- `0.7` - More detail contribution (rougher)

**Visual Impact**: Lower = smoother terrain, higher = more textured/noisy terrain.

---

### Physics Optimization

Controls physics collider creation and LOD behavior.

#### Max Colliders Per Frame

**Type**: Int  
**Default**: `3`  
**Range**: 1 - 10

Maximum number of physics colliders created per frame. Higher = faster collider creation but potential frame spikes.

**Performance Impact**: Each collider creation takes ~2-5ms on main thread.

**Recommended Values**:

- VR: 3 (ensures <15ms budget)
- Desktop: 5-10 (more headroom)

#### LOD Full Resolution Distance

**Type**: Float  
**Default**: `150`  
**Units**: Meters  
**Range**: 0 - View Distance

Distance threshold for full-resolution colliders. Tiles closer than this use all vertices for physics.

**Example**: At 150m, a 32×32 tile uses all 1024 vertices for collider.

#### LOD Half Resolution Distance

**Type**: Float  
**Default**: `300`  
**Units**: Meters  
**Range**: Full Resolution Distance - View Distance

Distance threshold for half-resolution colliders. Tiles between this and full resolution distance use every 2nd vertex.

**Example**: At 300m, a 32×32 tile uses only 16×16 = 256 vertices (75% reduction).

#### LOD Quarter Resolution Distance

**Type**: Float  
**Default**: `450`  
**Units**: Meters  
**Range**: Half Resolution Distance - View Distance

Distance threshold for quarter-resolution colliders. Tiles beyond this use every 4th vertex.

**Example**: At 450m, a 32×32 tile uses only 8×8 = 64 vertices (93.75% reduction).

**Note**: Tiles beyond this distance have no collider at all.

#### Max Collider Cache Memory (MB)

**Type**: Int  
**Default**: `50`  
**Units**: Megabytes  
**Range**: 10 - 200

Maximum memory for cached collider BlobAssets. When exceeded, least recently used colliders are evicted.

**Memory Estimation**:

- Full resolution (32×32): ~50KB per collider
- Half resolution (16×16): ~12KB per collider
- Quarter resolution (8×8): ~3KB per collider

**Recommended Values**:

- VR: 50MB (1000 full-res or 4000 quarter-res colliders)
- Desktop: 100MB (more caching capacity)

#### Use Physics LOD Layers

**Type**: Bool  
**Default**: `true`

Assign distant tiles (half/quarter resolution) to separate physics layer. Allows you to configure collision matrix to ignore low-detail terrain.

**Use Case**: Player should only collide with high-detail nearby terrain, not distant low-res tiles.

#### Close Terrain Physics Layer

**Type**: Int (Layer Dropdown)  
**Default**: `0`  
**Range**: 0 - 31

Physics layer index for close terrain tiles (full resolution).

**Inspector**: Displays as a dropdown menu showing all available Unity layers.

**Recommended**: Select "Terrain" layer from the dropdown. Use the menu item `Tools/Terrain/Setup Physics Layers` to automatically create and configure both layers.

#### Low Detail Physics Layer

**Type**: Int (Layer Dropdown)  
**Default**: `0`  
**Range**: 0 - 31

Physics layer index for low-detail terrain tiles (half/quarter resolution).

**Inspector**: Displays as a dropdown menu showing all available Unity layers.

**Recommended**: Select "TerrainLowDetail" layer from the dropdown. Use the menu item `Tools/Terrain/Setup Physics Layers` to automatically create and configure both layers.

---

## Configuration Presets

### Preset 1: VR High Performance

**Target**: 90fps on Quest 2

```
Tile Size: 100
View Distance: 300
Vertices Per Side: 16
Noise Frequency: 0.01
Noise Amplitude: 20
Noise Octaves: 3
Max Colliders Per Frame: 5
Full Res Distance: 100
Half Res Distance: 200
Quarter Res Distance: 300
```

### Preset 2: VR Balanced

**Target**: 90fps on PC VR (Index, Quest 3)

```
Tile Size: 100
View Distance: 500
Vertices Per Side: 32
Noise Frequency: 0.01
Noise Amplitude: 20
Noise Octaves: 4
Max Colliders Per Frame: 3
Full Res Distance: 150
Half Res Distance: 300
Quarter Res Distance: 450
```

### Preset 3: Desktop High Quality

**Target**: 60fps on desktop PC

```
Tile Size: 100
View Distance: 800
Vertices Per Side: 64
Noise Frequency: 0.01
Noise Amplitude: 50
Noise Octaves: 6
Max Colliders Per Frame: 10
Full Res Distance: 200
Half Res Distance: 400
Quarter Res Distance: 600
```

### Preset 4: Endless Runner VR

**Target**: Scrolling VR game

```
Tile Size: 100
View Distance: 500
Vertices Per Side: 32
Scroll Enabled: true
Scroll Speed: 10.0
Noise Frequency: 0.015
Noise Amplitude: 15
Noise Octaves: 4
Max Colliders Per Frame: 5
```

## Runtime Configuration

### Accessing Configuration at Runtime

```csharp
using Unity.Entities;

// Get the ECS world
var world = World.DefaultGameObjectInjectionWorld;
var em = world.EntityManager;

// Query for config singletons
var configQuery = em.CreateEntityQuery(typeof(TerrainTileConfig));
var entity = configQuery.GetSingletonEntity();

// Get config component
var config = em.GetComponentData<TerrainTileConfig>(entity);

// Modify config
config.viewDistance = 800f;
em.SetComponentData(entity, config);

configQuery.Dispose();
```

### Modifying Scroll Settings at Runtime

```csharp
// Enable/disable scrolling
var scrollQuery = em.CreateEntityQuery(typeof(ScrollConfig));
var scrollEntity = scrollQuery.GetSingletonEntity();
var scrollConfig = em.GetComponentData<ScrollConfig>(scrollEntity);

scrollConfig.enabled = true;
scrollConfig.scrollSpeed = 15.0f;

em.SetComponentData(scrollEntity, scrollConfig);
scrollQuery.Dispose();
```

### Resetting Scroll Offset

```csharp
// Reset accumulated scroll distance
var offsetQuery = em.CreateEntityQuery(typeof(ScrollOffset));
var offsetEntity = offsetQuery.GetSingletonEntity();

em.SetComponentData(offsetEntity, new ScrollOffset 
{ 
    accumulatedOffset = float3.zero 
});

offsetQuery.Dispose();
```

## Validation Rules

The following validation occurs in `OnValidate()`:

### Automatic Corrections

- `tileSize` - Clamped to minimum 1.0
- `viewDistance` - Clamped to minimum `tileSize`
- `verticesPerSide` - Clamped to minimum 2
- `noiseFrequency` - Clamped to minimum 0.0001
- `noiseAmplitude` - Clamped to minimum 0.0
- `noiseLacunarity` - Clamped to minimum 1.0

### Default Values

- If `playerName` empty when using FindByName → sets to "XR Origin Hands (XR Rig)"
- If `playerTag` empty when using FindByTag → sets to "Player"

## Performance Tuning Guidelines

### For Maximum Frame Rate

1. **Reduce View Distance**: Fewer tiles = less overhead
2. **Reduce Vertices Per Side**: Lower mesh complexity
3. **Reduce Noise Octaves**: Faster mesh generation
4. **Increase Frame Budgets**: More work per frame (trade smoothness for speed)

### For Maximum Quality

1. **Increase Vertices Per Side**: More detailed meshes
2. **Increase Noise Octaves**: More terrain detail
3. **Increase View Distance**: See farther
4. **Decrease Frame Budgets**: Prevent spikes (trade speed for smoothness)

### For VR Optimization

1. Keep `Vertices Per Side` ≤ 32
2. Keep `View Distance` ≤ 500m
3. Keep `Max Colliders Per Frame` ≤ 3
4. Enable `Use Physics LOD Layers`
5. Set appropriate LOD distances

## Common Configuration Mistakes

### ❌ Tile Size Too Small

**Problem**: Tile Size = 10m, View Distance = 500m  
**Result**: 2500 tiles spawned, system overwhelmed  
**Solution**: Increase tile size to 100m (25 tiles instead)

### ❌ Too Many Vertices

**Problem**: Vertices Per Side = 256  
**Result**: 65,536 vertices per tile, slow mesh generation  
**Solution**: Use 32 or 64 for balanced performance

### ❌ No Frame Budget

**Problem**: Max Colliders Per Frame = 100  
**Result**: Frame spikes when spawning many tiles  
**Solution**: Use 3-5 for VR, 10 for desktop

### ❌ LOD Distances Wrong Order

**Problem**: Half Res Distance < Full Res Distance  
**Result**: System behaves incorrectly  
**Solution**: Ensure: Full < Half < Quarter < View Distance

### ❌ Config Not in SubScene

**Problem**: TerrainConfigAuthoring in main scene  
**Result**: Player tracking fails, no tiles spawn  
**Solution**: Must be in SubScene for cross-scene references

## Configuration Examples

### Example 1: Flat Plane (No Noise)

```
Noise Frequency: 0.01
Noise Amplitude: 0        ← Zero height variation
Noise Octaves: 1
```

### Example 2: Gentle Rolling Hills

```
Noise Frequency: 0.005    ← Low frequency = large features
Noise Amplitude: 10       ← Low amplitude = gentle slopes
Noise Octaves: 2          ← Few octaves = smooth
Noise Persistence: 0.3
```

### Example 3: Rough Mountains

```
Noise Frequency: 0.02     ← Higher frequency = smaller features
Noise Amplitude: 100      ← High amplitude = tall peaks
Noise Octaves: 6          ← Many octaves = detailed
Noise Persistence: 0.6
```

### Example 4: Endless Runner

```
Scroll Enabled: true
Scroll Speed: 15.0        ← Fast forward scrolling
Tile Size: 100
View Distance: 600        ← See farther ahead
Noise Frequency: 0.015
Noise Amplitude: 15       ← Moderate terrain for gameplay
```

## Inspector Tooltips Quick Reference

All parameters have tooltips in the Unity Inspector. Hover over parameter names to see brief descriptions.

**Tip**: Press F2 in Inspector to see extended tooltip descriptions.

## Gizmo Visualization

When `TerrainConfigAuthoring` GameObject is selected in the Scene view:

**Magenta Sphere**: Player position (5m radius)  
**Green Sphere**: View distance (wireframe)  
**Cyan Box**: Example tile at player position  

This helps visualize the configuration before running the scene.

## Related Documentation

- **[Player Tracking Setup](PLAYER_TRACKING.md)** - Detailed player tracking configuration
- **[Auto-Scrolling Guide](AUTO_SCROLLING.md)** - Complete scrolling documentation
- **[Performance Optimization](PERFORMANCE.md)** - Tuning for best performance
- **[Technical Details](TECHNICAL_DETAILS.md)** - How noise generation works

---

**Back to**: [Documentation Hub](README.md)