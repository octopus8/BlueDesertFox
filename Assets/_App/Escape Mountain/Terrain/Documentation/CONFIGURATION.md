# Configuration Reference - Terrain System

Complete guide to all configuration parameters in the TerrainConfigAuthoring component.

## Configuration Location

The terrain system is configured via the `TerrainConfigAuthoring` MonoBehaviour component, which must be placed on a GameObject inside an ECS SubScene.

**File**: `Assets/_App/Escape Mountain/Terrain/TerrainConfigAuthoring.cs`

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

#### Slope Angle Degrees

**Type**: Float  
**Default**: `0`  
**Units**: Degrees  
**Range**: -60 to 60

Constant terrain grade along world +Z. `0` is flat. Positive values rise as Z increases; negative values descend (downhill snowboarding).

Height is path-integrated from `tan(angle)` along Z so the surface stays continuous.

#### Slope Variation Seed

**Type**: Int  
**Default**: `0`

Offsets the per-vertex slope noise domain. Change this to pick a different grade undulation pattern without moving the player.

#### Slope Variation Frequency

**Type**: Float  
**Default**: `0.005`  
**Units**: 1 / meters  
**Range**: ≥ 0

World-space frequency of the 2D simplex field that varies grade per vertex. Lower values undulate over hundreds of meters; higher values change grade more quickly across a tile. `0` samples a constant (seed-only) grade.

This is independent of the detail-noise frequency used for hills and bumps.

#### Slope Variation Amplitude

**Type**: Float  
**Default**: `0`  
**Units**: Degrees  
**Range**: 0 to 30 (also clamped so base angle minus amplitude stays ≥ -60)

Per-vertex grade spread subtracted from Slope Angle Degrees. Example: `-35°` with amplitude `10` produces local grades between `-45°` and `-35°`. `0` keeps a uniform grade.

Each vertex samples continuous XZ noise, then grade is path-integrated along Z at that X. Adjacent tiles share the same noise field, so no blend-distance seam fix is required. Ski trails stay level by sampling grade at the trail centerline rather than the vertex X.

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

Controls physics collider creation, distance culling, and frame budgeting. All in-range tiles use full-resolution collider geometry matching the rendered mesh.

#### Max Colliders Created Per Frame

**Type**: Int  
**Default**: `6`  
**Range**: 1 - 20

Maximum number of Burst mesh-prep jobs submitted per frame. Shared with mesh generation budgeting.

**Recommended Values**:

- VR: 4–6
- Desktop: 8–12

#### Max Physics Colliders Created Per Frame

**Type**: Int  
**Default**: `4`  
**Range**: 1 - 8

Maximum number of BVH `MeshCollider.Create` calls per frame. The effective budget is `min(maxCollidersCreatedPerFrame, maxPhysicsCollidersCreatedPerFrame)`.

**Recommended Values**:

- VR: 3–4 (ensures stable frame times)
- Desktop: 6–8

#### Max Collider Distance

**Type**: Float  
**Default**: `450`  
**Units**: Meters

Distance beyond which terrain colliders are removed completely. Tiles within this distance receive full-resolution colliders.

**Example**: At 450m, tiles beyond this threshold have no physics collider.

#### Terrain Physics Layer

**Type**: Int (Layer Dropdown)  
**Default**: `0`  
**Range**: 0 - 31

Physics layer index for all terrain colliders.

**Recommended**: Select "Terrain" layer from the dropdown. Use `Tools/Terrain/Setup Physics Layer` to configure the collision matrix.

---

### Trails

Up to three flat ski-style trails carved into the terrain. All share one Y height, start X/Z, LUT step, and optional snap-to-player. Each trail has its own width, blend width, and a centerline from **spline** or **noise weave** (spline wins when assigned).

#### Spline (per trail)

**Type**: `SplineContainer` (optional prefab or scene instance)  
**Default**: none

Assign a Unity Splines `SplineContainer` (for example `Trail Spline 00`) to define the trail centerline in XZ. Knot 0 is placed at the shared Start X/Z (or the player, when snap-to-player is on). Spline Y is ignored; trail height stays `trailHeight`.

**Spline convention**:

- Knot 0 = relative `(0, 0)` on the path
- Path must not double back in Z (Z of sampled points must be non-decreasing)
- Closed splines are rejected
- Sample spacing uses the shared **Trail LUT Step Meters**

When a spline is assigned, that trail ignores seed, frequency, amplitude, and the shared straight-run / weave-fade. The trail exists only for the Z range covered by the spline.

Do not add `SplineComponentAuthoring` and do not instance the spline into the SubScene — the baker reads the prefab at bake time.

#### Shared Start / Snap

Spline knot-0 samples are added to `trailStartX` / `trailStartZ`. If **Snap Start To Player** is enabled, a startup system overwrites those with the player content XZ so knot 0 sits under the rider.

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
Max Colliders Created Per Frame: 5
Max Physics Colliders Per Frame: 3
Max Collider Distance: 300m
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
Max Colliders Created Per Frame: 6
Max Physics Colliders Per Frame: 4
Max Collider Distance: 450m
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
Max Colliders Created Per Frame: 10
Max Physics Colliders Per Frame: 8
Max Collider Distance: 600m
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
Max Colliders Created Per Frame: 5
Max Physics Colliders Per Frame: 4
Max Collider Distance: 450m
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
**Yellow line / sphere**: Shared trail start and straight run  
**Orange / cyan / green polylines**: Enabled trail centerlines (spline or noise-weave preview)

This helps visualize the configuration before running the scene.

## Related Documentation

- **[Player Tracking Setup](PLAYER_TRACKING.md)** - Detailed player tracking configuration
- **[Auto-Scrolling Guide](AUTO_SCROLLING.md)** - Complete scrolling documentation
- **[Performance Optimization](PERFORMANCE.md)** - Tuning for best performance
- **[Technical Details](TECHNICAL_DETAILS.md)** - How noise generation works

---

**Back to**: [Documentation Hub](README.md)