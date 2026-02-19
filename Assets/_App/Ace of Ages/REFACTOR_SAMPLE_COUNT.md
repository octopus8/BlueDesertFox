# Refactor: Removed Duplicate Sample Count

## Problem

Both `SplineComponentAuthoring` and `EnemySpawnerAuthoring` were defining a `sampleCount` field, which appeared to represent the same concept but was being used in different contexts:

1. **SplineComponentAuthoring.sampleCount** - Controlled the quality of spline sampling when baking a `SplineContainer` into a `SplineDataComponent`.

2. **EnemySpawnerAuthoring.sampleCount** - Also controlled spline sampling, but was duplicating the baking process for a spline that may have already been baked.

## The Root Issue

`EnemySpawnerAuthoring` was **duplicating** the spline baking process:

```csharp
// OLD APPROACH - DUPLICATE BAKING ❌
public class EnemySpawnerAuthoring : MonoBehaviour
{
    [SerializeField] private SplineContainer loopSpline;  // References a SplineContainer
    [SerializeField] private int sampleCount = 100;       // Duplicate sample count
    
    // Baker would create its own SplineDataComponent
    // even if the spline was already baked by SplineComponentAuthoring
}
```

This created:
- **Duplicate blob assets** (wasted memory)
- **Inconsistent sample counts** (same spline could have different quality in different contexts)
- **Confusion** about which sample count controls the actual spline quality

## Solution

The `EnemySpawnerAuthoring` now **references** the spline entity instead of duplicating the baking:

```csharp
// NEW APPROACH - REFERENCE EXISTING SPLINE ENTITY ✅
public class EnemySpawnerAuthoring : MonoBehaviour
{
    [SerializeField] private GameObject splineObject;  // References GameObject with SplineComponentAuthoring
    
    // No sampleCount field - uses the value from SplineComponentAuthoring
}

public struct EnemySpawner : IComponentData
{
    public bool doSpawn;
    public Entity splineEntity;  // References the baked spline entity
}
```

## Changes Made

### 1. EnemySpawnerAuthoring.cs
- **Removed**: `sampleCount` field
- **Removed**: `CreateSplineDataComponent()` method (duplicate baking logic)
- **Changed**: `loopSpline` (SplineContainer) → `splineObject` (GameObject)
- **Changed**: `EnemySpawner.splineData` (SplineDataComponent) → `EnemySpawner.splineEntity` (Entity reference)

### 2. EnemySpawnerSystem.cs
- **Updated**: Retrieves `SplineDataComponent` from the referenced spline entity at runtime
- **Added**: Check for `HasComponent<SplineDataComponent>` before accessing spline data

## Benefits

✅ **Single Source of Truth**: Only `SplineComponentAuthoring.sampleCount` controls spline quality  
✅ **No Duplication**: Spline data is baked once and referenced multiple times  
✅ **Better Memory Usage**: No duplicate blob assets  
✅ **Consistent Quality**: All systems use the same spline samples  
✅ **Clearer Design**: Separation of concerns - SplineComponentAuthoring bakes, EnemySpawner references  

## How to Use

1. Create a GameObject with:
   - `SplineContainer` component
   - `SplineComponentAuthoring` component (set `sampleCount` here)

2. Create another GameObject with:
   - `EnemySpawnerAuthoring` component
   - Assign the spline GameObject to the `splineObject` field

3. At runtime:
   - The spawner will use the spline entity's `SplineDataComponent`
   - Spawned enemies will follow the spline path with the quality defined by the original `sampleCount`

## Architecture Alignment

This refactor aligns with the architecture diagram:

```
BAKING:
  SplineComponentAuthoring → Samples Spline → Creates Blob → SplineDataComponent
  
  EnemySpawnerAuthoring → References Spline Entity → EnemySpawner.splineEntity

RUNTIME:
  EnemySpawnerSystem → Reads splineEntity → Gets SplineDataComponent → Adds to spawned enemy
  
  UnitMoverSystem → Uses SplineDataComponent → Moves enemy along path
```

Now there's a clear **single source of truth** for spline data, eliminating duplication and confusion.

