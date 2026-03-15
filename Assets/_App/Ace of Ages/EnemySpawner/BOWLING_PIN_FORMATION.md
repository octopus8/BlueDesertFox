# Bowling Pin Formation System

## Overview

The bowling pin formation system allows the `EnemySpawnerSystem` to spawn multiple enemies arranged in a classic 10-pin bowling formation. Each enemy maintains its position in the formation while following a spline path.

## Features

✅ **Spawns 10 enemies** in a bowling pin formation  
✅ **Configurable spacing** between enemies in the formation  
✅ **Formation maintained** while moving along the spline  
✅ **Lateral positioning** - enemies stay in their lanes perpendicular to the path  
✅ **Forward/backward offset** - enemies maintain depth in the formation  
✅ **Burst-compiled job system** for optimal performance  

## Components

### FormationPosition
Located in: `FormationPositionAuthoring.cs`

Defines an entity's position within a formation:
- `positionIndex` - Position in the formation (0-9 for bowling pins)
- `lateralOffset` - Offset perpendicular to the spline path (left/right)
- `forwardOffset` - Offset along the spline path (forward/backward)

### EnemySpawner (Updated)
Located in: `EnemySpawnerAuthoring.cs`

Added fields:
- `formationCount` - Number of enemies to spawn (default: 10)
- `formationSpacing` - Distance between enemies in units (default: 2.0)

## Bowling Pin Formation Layout

The standard 10-pin bowling formation:

```
       [0]              ← Row 0 (back): 1 pin
      [1] [2]           ← Row 1: 2 pins
    [3] [4] [5]         ← Row 2: 3 pins
  [6] [7] [8] [9]       ← Row 3 (front): 4 pins
```

### Position Calculation

The `CalculateBowlingPinPosition()` method in `EnemySpawnerSystem` calculates:

1. **Row number**: Determined by position index
   - Position 0 → Row 0
   - Positions 1-2 → Row 1
   - Positions 3-5 → Row 2
   - Positions 6-9 → Row 3

2. **Forward offset**: `row * spacing`
   - Creates depth in the formation
   - Negative values place enemies behind the lead position

3. **Lateral offset**: `(positionInRow - (pinsInRow - 1) * 0.5) * hexagonalSpacing`
   - Centers the row around the spline path
   - Uses hexagonal spacing (0.866 = √3/2) for realistic bowling pin arrangement

## System Flow

### Spawning (EnemySpawnerSystem)

1. When `doSpawn` is triggered:
   ```csharp
   for (int i = 0; i < formationCount; i++)
   {
       // Calculate formation position
       var formationData = CalculateBowlingPinPosition(i, spacing);
       
       // Spawn enemy with FormationPosition component
       // Position at initial location based on formation offset
   }
   ```

2. Each enemy is spawned with:
   - `SplineDataComponent` - Reference to the spline path
   - `FormationPosition` - Its position in the formation
   - `SplineFollower` - Movement speed and distance ratio
   - `LocalTransform` - Initial position/rotation

### Movement (SplineFollowerSystem)

The `SplineFollowerJob` handles formation positioning:

```csharp
// Check if entity has formation position
if (formationPositionLookup.HasComponent(entity))
{
    // Apply forward offset to distance ratio
    adjustedDistanceRatio = baseRatio + (forwardOffset / totalLength);
    
    // Evaluate spline at adjusted position
    SplineSample sample = spline.Evaluate(adjustedDistanceRatio);
    
    // Calculate perpendicular offset
    rightVector = cross(upVector, tangent);
    targetPosition = sample.position + rightVector * lateralOffset;
}
```

### Key Behavior

- **Base movement**: All enemies share the same `distanceRatio` (synchronized movement)
- **Formation offset**: Each enemy's actual position is offset from the base
  - Forward/backward: Adjusts `distanceRatio` before evaluating the spline
  - Left/right: Adds lateral offset after evaluating the spline
- **Rotation**: All enemies face the same direction (spline tangent)

## Configuration

### In Unity Editor

1. **EnemySpawnerAuthoring** settings:
   - `Loop Spline`: Assign the GameObject with `SplineComponentAuthoring`
   - `Formation Count`: Number of enemies to spawn (default: 10)
   - `Formation Spacing`: Distance between enemies (default: 2.0 units)

2. **SplineComponentAuthoring** settings:
   - `Sample Count`: Quality of spline sampling (affects all enemies)

### Adjusting Formation

- **Wider formation**: Increase `formationSpacing`
- **Tighter formation**: Decrease `formationSpacing`
- **Different count**: Change `formationCount` (layout adjusts automatically)
- **Custom formation**: Modify `CalculateBowlingPinPosition()` method

## Performance

✅ **Burst-compiled**: All movement logic is optimized  
✅ **Job system**: Parallel processing of all entities  
✅ **Component lookup**: Efficient optional component checking  
✅ **Shared spline data**: Blob assets prevent duplication  

The system uses `ComponentLookup<FormationPosition>` to efficiently check if an entity is part of a formation, allowing the same job to handle both formation and non-formation entities.

## Example Usage

### Spawn 10 enemies in bowling pin formation:
```csharp
// Set on EnemySpawner component
enemySpawner.doSpawn = true;
enemySpawner.formationCount = 10;
enemySpawner.formationSpacing = 2f;
```

### Spawn 6 enemies in custom formation:
```csharp
// Modify formationCount (will create incomplete bowling pin pattern)
enemySpawner.formationCount = 6;
// Positions 0-5 will be spawned (back 3 rows of the formation)
```

## Extending the System

### Custom Formations

To create different formations, modify the `CalculateBowlingPinPosition()` method:

```csharp
// Example: V-formation
private static (float3 lateralOffset, float forwardOffset) CalculateVFormation(int index, float spacing)
{
    int side = index % 2; // 0 = left, 1 = right
    int depth = index / 2;
    
    float lateralOffset = (side == 0 ? -1f : 1f) * depth * spacing;
    float forwardOffset = -depth * spacing;
    
    return (new float3(lateralOffset, 0, 0), forwardOffset);
}
```

### Multiple Formations

Create different formation calculation methods and switch based on a formation type enum:

```csharp
public enum FormationType { BowlingPin, VFormation, Line, Circle }

// Add to EnemySpawner component
public FormationType formationType;
```

## Debugging

### Enable Non-Job Path
Set `useJobs = false` in `SplineFollowerSystem.cs` to use the non-job path for debugging:
- Allows breakpoints
- Can inspect component values in the editor
- Shows individual entity processing

### Visualize Formation
- Each enemy's `FormationPosition.positionIndex` shows its place in the formation
- `lateralOffset` shows left/right position
- `forwardOffset` shows depth in the formation

## Architecture Benefits

1. **Separation of concerns**: Formation logic separate from movement logic
2. **Reusable components**: `FormationPosition` can be used by other systems
3. **Data-oriented**: All formation data in components, no MonoBehaviour overhead
4. **Scalable**: Can spawn hundreds of formations with minimal performance impact
5. **Flexible**: Easy to add new formation types or modify existing ones

## Summary

The bowling pin formation system elegantly combines:
- **Spline following** - Entities move along a predefined path
- **Formation positioning** - Entities maintain relative positions
- **Efficient processing** - Burst compilation and job system
- **Clean architecture** - ECS best practices

All entities move together as a cohesive unit while maintaining their individual positions in the formation!

