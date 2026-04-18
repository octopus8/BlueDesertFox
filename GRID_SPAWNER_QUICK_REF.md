# Grid Spawner System - Quick Reference

## Overview
DOTS system that spawns a grid of entities in the XY plane at a specified Z position. Automatically spawns on first system update.

## Files Created
- `GridSpawnerAuthoring.cs` - Authoring component with Baker
- `GridSpawnerSystem.cs` - DOTS system that performs the spawning

## Usage

### Setup in Scene

1. **Create a prefab GameObject** that you want to spawn in the grid
   - Add any components/renderers you need
   - Position/rotation will be overridden by the grid system
   - Scale will be preserved from the prefab

2. **Add GridSpawnerAuthoring component** to a GameObject in your scene
   - Can be added to any GameObject (or create a new empty GameObject)
   - Assign your prefab to the "Prefab" field in Inspector

3. **Configure Grid Settings** in Inspector:
   - **Grid Size**: Number of objects per dimension (default: 75 for 75×75 grid)
   - **Spacing**: Distance between objects in units (default: 2.0)
   - **Z Position**: Z-coordinate for the entire grid (default: 100.0)

### Behavior

- **Automatic spawning**: Grid spawns on first system update (immediately after scene starts)
- **One-time spawn**: System tracks `hasSpawned` flag to prevent repeated spawning
- **Centered grid**: Grid is centered around world origin in XY plane
  - For 75×75 grid with 2.0 spacing: spans from (-74, -74) to (74, 74)
- **Total entities**: gridSize × gridSize (default: 75×75 = 5,625 entities)

## Component Reference

### GridSpawner (IComponentData)
```csharp
public struct GridSpawner : IComponentData
{
    public Entity prefabEntity;  // Prefab to spawn
    public int gridSize;         // Grid dimensions (N×N)
    public float spacing;        // Distance between objects
    public float zPosition;      // Z-coordinate for grid
    public bool hasSpawned;      // Prevents re-spawning
}
```

## Example Configuration

**75×75 grid at Z=100 with 2-unit spacing:**
- Grid Size: 75
- Spacing: 2.0
- Z Position: 100.0
- Result: 5,625 entities spanning X: [-74, 74], Y: [-74, 74], Z: 100

**10×10 grid at Z=0 with 5-unit spacing:**
- Grid Size: 10
- Spacing: 5.0
- Z Position: 0.0
- Result: 100 entities spanning X: [-22.5, 22.5], Y: [-22.5, 22.5], Z: 0

## Performance Notes

- All entities spawn in a single frame
- For very large grids (>10,000 entities), consider frame budgeting
- Uses EntityCommandBuffer for efficient structural changes
- Burst-compiled for maximum performance

## Integration with Existing Systems

The GridSpawner follows the same patterns as other systems in the project:
- Similar to `EnemySpawnerAuthoring` for prefab conversion
- Uses `BeginSimulationEntityCommandBufferSystem` like `EnemySpawnerSystem`
- Follows project conventions for Baker classes and IComponentData structs

## Troubleshooting

**Grid doesn't spawn:**
- Check that prefab is assigned in Inspector
- Verify the GameObject with GridSpawnerAuthoring is in the scene
- Check Console for "GridSpawner: Starting spawn..." debug message

**Wrong position:**
- Grid is centered at origin in XY, extends to Z position specified
- Adjust Z Position parameter to move grid forward/backward
- Spacing controls distance between objects

**Wrong scale:**
- System preserves prefab's LocalTransform.Scale
- Modify the prefab's scale to change spawned entity sizes

