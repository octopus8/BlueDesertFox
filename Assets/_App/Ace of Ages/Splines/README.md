# Splines System

ECS spline system that bakes Unity.Splines into Burst-compatible `BlobAssetReference<SplineDataBlob>` for use by `EnemySpawnerSystem` and `SplineFollowerSystem`.

## Overview

Unity Splines use managed objects (`IReadOnlyList`, `KnotsReadonlyCollection`) which are incompatible with Burst compilation. The solution: **bake once at edit time**, store pre-sampled data in a `BlobAsset`, and interpolate at runtime with zero allocations.

## Files

| File | Purpose |
|------|---------|
| `SplineComponentAuthoring.cs` | Authoring component + baker + `SplineDataBlob` definition |
| `SplineFollowerAuthoring.cs` | Authoring component for entities that follow a spline |
| `SplineFollowerSystem.cs` | `[DisableAutoCreation]` — Burst parallel job for spline following |
| `SimpleSplineFollower.cs` | MonoBehaviour prototype (non-ECS, for testing) |
| `ARCHITECTURE_DIAGRAM.txt` | ASCII architecture diagram |

## Key Types

### `SplineDataBlob`

Stored as a `BlobAssetReference<SplineDataBlob>` in `SplineDataComponent`:

```csharp
struct SplineDataBlob
{
    BlobArray<SplineSample> samples;  // 100 samples by default (configurable)
    float totalLength;
    bool isClosed;
}

struct SplineSample
{
    float3 position;
    float3 tangent;
    float3 up;
}
```

### `SplineDataComponent`

```csharp
struct SplineDataComponent : IComponentData
{
    BlobAssetReference<SplineDataBlob> splineData;
}
```

Added by `SplineComponentAuthoring.Baker` to entities with a `SplineContainer`.

### `SplineFollower`

```csharp
struct SplineFollower : IComponentData
{
    float moveSpeed;
    float distanceRatio;  // 0.0 → 1.0 along spline
}
```

## Authoring Setup

### Spline Entity (the spline path itself)

1. Create a GameObject with `SplineContainer` component
2. Add `SplineComponentAuthoring`
3. Set `sampleCount` (100 = good balance of accuracy vs memory; increase for long winding paths)
4. Bake into the SubScene

### Follower Entity (enemies, projectiles)

`EnemySpawnerSystem` handles followers automatically:
1. It reads `EnemySpawner.splineEntity` to find the spline entity
2. Copies `SplineDataComponent` from the spline entity onto each spawned enemy
3. Each enemy's `FormationMovementSystem` uses this for spline-following movement

For standalone followers, add `SplineFollowerAuthoring` and enable `SplineFollowerSystem`.

## Baking Pipeline

```
Edit Mode:
  SplineContainer (managed) 
      → SplineComponentAuthoring.Baker
      → NativeSpline (temp) samples at sampleCount intervals
      → SplineDataBlob (BlobAsset)
      → SplineDataComponent on entity

Runtime:
  SplineDataComponent.splineData.Value.Evaluate(ratio)
      → Linear interpolation between adjacent samples
      → Returns position + tangent + up (no managed allocations)
```

## `SplineFollowerSystem`

**Status:** `[DisableAutoCreation]` — must be manually enabled if you need standalone spline-following entities.

For enemies, `FormationMovementSystem` handles the spline following phase directly using `SplineDataComponent` without `SplineFollowerSystem`.

## Runtime API

```csharp
// Evaluate a position along the spline
ref SplineDataBlob blob = ref splineData.splineData.Value;
int sampleIndex = (int)(ratio * (blob.samples.Length - 1));
float3 position = blob.samples[sampleIndex].position;

// Or use the Evaluate() method for interpolated result:
float3 pos = blob.Evaluate(ratio);
```

## Performance

- Zero allocations at runtime
- Burst-compiled evaluation
- `sampleCount = 100` → 100 × 36 bytes = 3.5KB per spline (negligible)
- Increase `sampleCount` for tighter curves (accuracy vs memory tradeoff)

## See Also

- `EnemySpawner/README.md` — How enemies follow splines
- `EnemySpawner/FORMATION_APPROACH_SYSTEM.md` — Formation movement state machine
- `ARCHITECTURE_DIAGRAM.txt` — Detailed ASCII architecture diagram
