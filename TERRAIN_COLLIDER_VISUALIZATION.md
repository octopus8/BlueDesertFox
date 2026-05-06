# Terrain Collider Visualization System

## Overview
System for visualizing terrain physics colliders as colored wireframes in the Unity Scene view. Colors indicate LOD levels to help debug physics performance.

## Implementation Date
April 23, 2026

## Files Modified

### 1. TerrainConfigAuthoring.cs
- Added `visualizeColliders` boolean field in Debug/Testing section (Inspector toggle)
- Field is baked into `TerrainTileConfig` component for ECS access

### 2. TileComponents.cs
- Added `visualizeColliders` field to `TerrainTileConfig` struct
- Available for systems to read configuration at runtime

### 3. TerrainColliderVisualizer.cs (NEW)
- Standalone MonoBehaviour component for visualization
- Draws full mesh wireframes of physics colliders
- Real-time LOD level statistics in Inspector

## Usage Instructions

### Setup
1. In your terrain scene, create a new empty GameObject (e.g., name it "Collider Visualizer")
2. Add the `TerrainColliderVisualizer` component to it
3. Configure visualization settings in the Inspector:
   - **Enable Visualization**: Toggle on/off
   - **Full Resolution Color**: Green (default) - close tiles
   - **Half Resolution Color**: Yellow (default) - mid-distance tiles  
   - **Quarter Resolution Color**: Orange (default) - far tiles

### Features

#### LOD-Based Color Coding
- **Green**: Full resolution colliders (all vertices) - closest to player
- **Yellow**: Half resolution colliders (every 2nd vertex) - medium distance
- **Orange**: Quarter resolution colliders (every 4th vertex) - far distance

#### Inspector Statistics
The component displays real-time counts:
- Total tiles with colliders
- Count at each LOD level (Full/Half/Quarter)

#### Scene View Visualization
- Wireframes only visible when game is running (Play mode)
- Draws actual triangle edges from mesh geometry
- Works independently of terrain rendering (useful when `renderTerrain` is disabled)

## Technical Details

### How It Works
1. Queries all entities with:
   - `PhysicsCollider` component (has collision)
   - `TerrainTileDistanceToPlayer` component (contains LOD level)
   - `VertexElement` and `IndexElement` buffers (mesh data)
   - `LocalTransform` component (position)

2. Reads mesh geometry from existing `VertexElement`/`IndexElement` buffers
   - No need to extract from Unity.Physics.MeshCollider (avoids unsafe code)
   - Uses the same mesh data that generated the collider

3. Draws wireframe using `Gizmos.DrawLine()` for each triangle edge
   - Iterates through index buffer in triplets (3 indices = 1 triangle)
   - Transforms vertices by tile position
   - Colors based on LOD level from `TerrainTileDistanceToPlayer`

### Performance Considerations
- Only tiles near player have colliders (limited by LOD system)
- Typical active collider count: 20-40 tiles
- Gizmo drawing is editor-only (zero cost in builds)
- Mesh data access is direct (no allocations)

## Configuration

### TerrainConfigAuthoring Inspector Field (Optional)
The `visualizeColliders` field in TerrainConfigAuthoring is baked into ECS but **not currently used** by the visualizer. The visualizer has its own independent toggle (`enableVisualization`).

This field is available for future extensions if you want ECS systems to read the visualization preference.

## Debugging Tips

### No Wireframes Visible?
1. Check `Enable Visualization` is toggled on
2. Ensure game is in Play mode
3. Verify terrain tiles have colliders (check Info counts)
4. Make sure you're viewing the Scene view (not Game view)

### Wrong Colors?
- LOD colors can be customized in Inspector
- Colors are assigned based on `TerrainTileDistanceToPlayer.lodLevel`
- Distance thresholds configured in `TerrainConfigAuthoring` (LOD Full/Half/Quarter Resolution Distance)

### Performance Issues?
- Gizmo drawing is editor-only
- If Scene view lags, reduce view distance in TerrainConfigAuthoring
- Toggle visualization off when not needed

## Integration with Existing Systems

### Works With
- ✅ Terrain rendering enabled or disabled (`renderTerrain` flag)
- ✅ All LOD levels (Full/Half/Quarter resolution)
- ✅ Terrain scrolling (directional auto-scroll)
- ✅ Origin shifts / floating origin (if re-enabled)

### Does Not Affect
- ❌ Runtime performance (editor-only visualization)
- ❌ Physics simulation (read-only access to colliders)
- ❌ Mesh generation (uses existing data)

## Example Use Cases

1. **Debug LOD transitions**: See when tiles switch between Full→Half→Quarter resolution
2. **Verify collision coverage**: Ensure player area has full-resolution colliders
3. **Performance tuning**: Adjust LOD distance thresholds based on visual feedback
4. **Test without rendering**: Disable terrain mesh but verify colliders still generate

## Future Enhancements

Potential improvements:
- Add toggle to show/hide specific LOD levels
- Distance-based culling of far wireframes (performance)
- Show collider generation priority (color by distance to camera forward)
- Integrate with TerrainConfigAuthoring.visualizeColliders flag for centralized control

