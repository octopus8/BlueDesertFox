# Infinite Terrain Tiling System with Floating Origin

A high-performance Unity DOTS-based infinite terrain system that uses procedural generation with floating origin support to prevent floating-point precision errors.

## Features

- **Infinite Terrain**: Dynamically spawns/despawns tiles as the player moves
- **Floating Origin**: Automatically shifts the world origin when the player moves far from (0,0,0), preventing precision errors
- **Procedural Generation**: Uses multi-octave Perlin noise for terrain height generation
- **ECS Architecture**: Fully implemented using Unity DOTS for maximum performance
- **Burst Compilation**: All critical systems use Burst compiler for optimal CPU performance
- **Entities Graphics**: Native ECS rendering using Unity.Rendering
- **Physics Collisions**: Automatic mesh collider generation for player collision detection

## Architecture

### Components

- **FloatingOriginComponents.cs**: Defines floating origin system components
  - `WorldOriginOffset`: Singleton tracking accumulated world offset (double3 precision)
  - `FloatingOriginConfig`: Configuration for origin shift threshold
  - `FloatingOriginEnabled`: Tag for entities affected by world shifts

- **TileComponents.cs**: Defines terrain tile components
  - `TerrainTileConfig`: Singleton configuration for terrain generation
  - `TerrainTile`: Component identifying a tile and its grid position
  - `VertexElement`, `NormalElement`, `UVElement`, `IndexElement`: Mesh data buffers
  - `MeshReference`: Managed component holding Unity Mesh reference

### Systems

1. **FloatingOriginSystem**: Monitors player distance from origin and triggers world shifts
2. **TileSpawningSystem**: Manages tile spawning/despawning based on player position
3. **TerrainMeshGenerationSystem**: Generates procedural terrain meshes using noise functions
4. **TerrainRenderingSystem**: Converts ECS mesh data to Unity meshes and sets up rendering
5. **TerrainPhysicsSystem**: Creates mesh colliders for terrain collision

## Setup Instructions

### 1. Add TerrainConfigAuthoring to Scene

1. Create a new GameObject in your scene (e.g., "TerrainConfig")
2. Add the `TerrainConfigAuthoring` component
3. Configure the settings:
   - **Tile Size**: 100m (size of each terrain chunk)
   - **View Distance**: 500m (how far tiles are visible)
   - **Vertices Per Side**: 32 (mesh resolution, higher = more detail)
   - **Floating Origin Enabled**: True
   - **Shift Threshold**: 2000m (when to trigger origin shift)
   - **Noise Settings**: Adjust to taste

### 2. Create Terrain Material

1. Create a new Material in `Assets/_App/Ace of Ages/Terrain/Resources/`
2. Name it `TerrainMaterial`
3. Set shader to "Universal Render Pipeline/Lit"
4. Assign a texture to the Base Map (optional)
5. Set the material reference in TerrainConfigAuthoring (optional - will auto-create if not set)

### 3. Ensure Player Has PlayerTag

The system identifies the player using the `PlayerTag` component. Make sure your player entity has this component (already exists in the project at `DOTSAuthoring/PlayerTagAuthoring.cs`).

### 4. Add FloatingOriginEnabled to Player

Add the `FloatingOriginEnabled` tag to your player entity so it gets shifted along with the terrain during origin resets.

## How It Works

### Floating Origin System

When the player moves more than `shiftThreshold` meters from (0,0,0):
1. The system calculates the offset needed to move the player back to near-origin
2. Updates `WorldOriginOffset.accumulatedOffset` with this shift (using double3 precision)
3. Subtracts the offset from all entities with `FloatingOriginEnabled` tag in a single frame
4. Terrain generation uses the accumulated offset when sampling noise, ensuring consistency

### Tile Management

- Player position is converted to grid coordinates (e.g., at position (250, 0, 150) with 100m tiles = grid (2, 1))
- System maintains a `NativeParallelHashMap<int2, Entity>` of active tiles
- New tiles are spawned when player enters view range
- Old tiles are despawned when player moves too far away

### Mesh Generation

- Each tile generates a mesh with `verticesPerSide x verticesPerSide` vertices
- Height is sampled from multi-octave Perlin noise using world position + accumulated offset
- Normals are calculated from adjacent vertex heights for proper lighting
- UVs are generated for texture mapping

### Rendering

- Mesh data is converted from ECS buffers to Unity Mesh objects
- RenderMeshUtility adds Entities Graphics components for native rendering
- Material is shared across all tiles for efficient batching

### Physics

- Mesh colliders are automatically created from terrain geometry
- Colliders use Unity.Physics for ECS-native collision detection
- Player can walk on terrain and collide with it naturally

## Performance Characteristics

- **Tile Spawning**: O(view distance squared) tiles active at once
- **Mesh Generation**: Burst-compiled, runs per-tile as needed
- **Origin Shift**: O(n) where n = number of entities with FloatingOriginEnabled
- **Memory**: Approximately (verticesPerSide^2 * 20 bytes) per tile for mesh data

## Typical Configuration Values

### Small/Detailed Terrain
- Tile Size: 50m
- View Distance: 200m
- Vertices Per Side: 64
- Active Tiles: ~50-100

### Large/Fast Terrain
- Tile Size: 200m
- View Distance: 1000m
- Vertices Per Side: 16
- Active Tiles: ~50-100

## Troubleshooting

### Terrain Not Appearing
- Verify TerrainConfigAuthoring is in the scene and baked
- Check that Player has PlayerTag component
- Ensure terrain material is valid (check console for errors)

### Performance Issues
- Reduce `verticesPerSide` (16-32 is usually sufficient)
- Reduce `viewDistance` (fewer active tiles)
- Reduce `noiseOctaves` (fewer noise layers)

### Terrain "Jumps" After Origin Shift
- This indicates the noise sampling isn't using the accumulated offset correctly
- Should not happen with current implementation - file a bug report if seen

### Collisions Not Working
- Verify TerrainPhysicsSystem is running (check Console for errors)
- Ensure player has physics components (Rigidbody, Collider)
- Check physics layers/collision matrix

## Future Enhancements

- LOD system with multiple mesh resolutions based on distance
- Texture splatting based on height/slope
- Vegetation placement system
- Chunk saving/loading for persistent worlds
- Biome system with different noise parameters per region
- Water plane with shore detection

## Technical Notes

### Why Double Precision for WorldOriginOffset?

Float precision breaks down at large distances (~10,000+ units). By tracking the accumulated offset in double3, we can:
- Sample noise at the "true" world position (e.g., at x=1,000,000)
- Keep all entity positions near origin (e.g., at x=0)
- Maintain terrain consistency across unlimited distances

### Why Not Use Unity Terrain System?

Unity's terrain system doesn't integrate well with DOTS. This custom system:
- Is fully ECS-native (Burst-compiled jobs)
- Supports true infinite terrain
- Has built-in floating origin support
- Works seamlessly with DOTS physics

### Buffer Writing Limitation

Parallel jobs cannot write to DynamicBuffer. Current implementation:
- Uses NativeArray for intermediate storage during generation
- Copies to buffers on main thread after computation
- Still maintains good performance due to Burst compilation

For even better performance, consider batching multiple tiles per frame rather than processing individually.

