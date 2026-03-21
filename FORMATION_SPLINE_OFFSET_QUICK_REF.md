# Formation Spline Offset Fix - Quick Reference

## Problem Summary
Enemies behind the lead on non-closed splines snapped to spline start due to negative `adjustedDistanceRatio` being clamped to 0.

## Solution
Extend enemies beyond spline bounds along tangent direction instead of clamping.

## Code Changes

**File**: `SplineFollowerSystem.cs`

### Added Variables
```csharp
float forwardOffsetDistance = 0f; // Track offset for bounds checks
```

### New Logic Block (after line 145)
```csharp
// For non-closed splines, handle formation offsets beyond bounds
if (hasFormation && !spline.isClosed && forwardOffsetDistance != 0f)
{
    float rawAdjustedRatio = splineFollower.distanceRatio + (forwardOffsetDistance / spline.totalLength);
    
    if (rawAdjustedRatio < 0f) // Behind spline start
    {
        float offsetDistance = -rawAdjustedRatio * spline.totalLength;
        SplineSample startSample = spline.Evaluate(0f);
        float3 backwardDirection = -math.normalize(startSample.tangent);
        targetPosition = startSample.position + backwardDirection * offsetDistance;
        sample = startSample;
    }
    else if (rawAdjustedRatio > 1f) // Ahead of spline end
    {
        float offsetDistance = (rawAdjustedRatio - 1f) * spline.totalLength;
        SplineSample endSample = spline.Evaluate(1f);
        float3 forwardDirection = math.normalize(endSample.tangent);
        targetPosition = endSample.position + forwardDirection * offsetDistance;
        sample = endSample;
    }
}
```

## Testing Checklist

✅ Spawn 10-enemy bowling pin formation  
✅ Verify rear enemies start behind spline start (not at start)  
✅ Confirm formation shape maintained during approach phase  
✅ Check smooth transition to FollowingSpline phase (no snapping)  
✅ Verify formation flows naturally along spline  
✅ Test with different formation spacing values  

## Key Behavior Change

**Before**: Rear enemies snap to spline start, then spread out  
**After**: Rear enemies maintain formation offset from spawn, positioned behind spline start

## Performance
- ✅ Burst-compiled
- ✅ Minimal overhead (only for out-of-bounds formation members)
- ✅ No GC allocations

