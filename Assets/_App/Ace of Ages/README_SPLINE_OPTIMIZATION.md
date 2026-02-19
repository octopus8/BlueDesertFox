# Spline System - Resolved Burst Compilation Error

## ✅ Problem Fixed

The Burst compilation error `BC1020: Boxing a valuetype 'KnotsReadonlyCollection' to a managed object is not supported` has been resolved.

## 🔧 What Was Changed

### Files Modified:
1. **SplineComponentAuthoring.cs** - Completely refactored
2. **UnitMoverAuthoring.cs** - Simplified component
3. **UnitMoverSystem.cs** - Updated to use pre-sampled data
4. **EnemySpawnerAuthoring.cs** - Updated to new spline system
5. **EnemySpawnerSystem.cs** - Updated entity spawning logic

### Key Changes:

#### ❌ REMOVED (Old Approach):
- `SplineBlobAssetComponent` - Old component that stored knots
- `NativeSplineBlob` - Old blob that required knot reconstruction
- `KnotsReadonlyCollection` - Managed interface causing Burst error
- Runtime `CreateNativeSpline()` calls every frame

#### ✅ ADDED (New Approach):
- `SplineDataComponent` - New component holding pre-sampled data
- `SplineDataBlob` - Blob asset with pre-calculated samples
- `SplineSample` - Struct holding position, tangent, and up vector
- `Evaluate()` method - Fast interpolation between samples

## 🎯 How It Works Now

### At Baking Time (Editor Only):
1. `SplineComponentAuthoring` reads the Unity Spline
2. Creates a temporary `NativeSpline` 
3. Samples the spline at regular intervals (default: 100 samples)
4. Stores samples in a blob asset
5. Blob asset is attached to the entity

### At Runtime (Game):
1. `UnitMoverSystem` queries entities with `UnitMover` + `SplineDataComponent`
2. Calculates distance along spline based on speed and deltaTime
3. Uses `Evaluate()` to interpolate between pre-sampled points
4. Sets physics velocity and rotation
5. Entity smoothly follows the spline using physics

## 📋 Usage Instructions

### For Spline GameObject:
1. Add `SplineContainer` component
2. Add `SplineComponentAuthoring` component
3. Set `Sample Count` (default 100 is usually fine)
   - Higher = more accurate, more memory
   - Lower = less accurate, less memory
   - Recommended: 50-200 depending on spline complexity

### For Moving Entity Prefab:
1. Add `UnitMoverAuthoring` component
2. Set `Move Speed` (units per second along spline)
3. Ensure entity has physics components (PhysicsVelocity, etc.)

### For Spawner:
1. Add `EnemySpawnerAuthoring` component to a GameObject
2. Assign the `SplineContainer` reference
3. Set `Sample Count` if different from default
4. The spawner will add `SplineDataComponent` to spawned entities

## ⚡ Performance Benefits

| Aspect | Before | After |
|--------|--------|-------|
| Spline Creation | Every frame | Once at baking |
| Burst Compilation | ❌ Failed | ✅ Working |
| Memory Allocation | Every frame | One-time blob |
| Cache Efficiency | Poor (managed objects) | Excellent (blob data) |
| Job System | ❌ Not available | ✅ Available (commented out but ready) |

## 🔍 Troubleshooting

### Entity Not Moving:
- Check that entity has both `UnitMover` AND `SplineDataComponent`
- Verify `moveSpeed` is not zero
- Ensure entity has `PhysicsVelocity` component

### Choppy Movement:
- Increase `sampleCount` on SplineComponentAuthoring
- Check if spline has sharp corners (add more knots)

### Memory Usage:
- Each sample is ~36 bytes (3 float3 vectors)
- 100 samples ≈ 3.6 KB per spline
- Reduce `sampleCount` if needed

## 🎮 Enable Job System (Optional)

In `UnitMoverSystem.cs`, change line 11:
```csharp
private const bool useJobs = true;  // Change to true for better performance
```

This enables Burst-compiled parallel job execution for maximum performance.

## 📝 Notes

- Splines are baked at edit time, changes require rebaking
- Supports both open and closed splines
- Physics-based movement provides natural interpolation
- Rotation follows spline tangent automatically
- Angular velocity is zeroed to prevent unwanted spin

## ✨ Example Values

Good starting values:
- **Move Speed**: 3-10 units/second
- **Sample Count**: 100 samples
- **Rotation Speed**: 5.0 (in UnitMoverSystem)

Adjust based on your specific needs!

