> **Archive Notice:** This is a historical patch note. The fix described here is already integrated into the codebase. See [Archive/README.md](Archive/README.md).

# Bullet Collision System - Physics Dependency Fix

## Issue

**Error**: `InvalidOperationException: The previously scheduled job NativeStream:ConstructJob writes to the Unity.Collections.NativeStream`

**Location**: `BulletCollisionSystem.cs`, line 42 (collision event iteration)

**Cause**: The system was trying to read collision events from the physics simulation while the physics jobs were still running and writing to the collision event stream.

## Root Cause

Unity Physics runs collision detection as Burst-compiled jobs that write collision events to a `NativeStream`. When we try to read from this stream before the jobs complete, Unity's job safety system throws an exception to prevent race conditions.

## Solution

Added `state.Dependency.Complete()` before reading collision events:

```csharp
public void OnUpdate(ref SystemState state)
{
    // ...
    
    // IMPORTANT: Complete the physics simulation dependency before reading collision events
    // This ensures all physics jobs have finished writing to the collision event stream
    state.Dependency.Complete();
    
    // Now safe to iterate collision events
    var collisionEvents = simulationSingleton.AsSimulation().CollisionEvents;
    foreach (var collisionEvent in collisionEvents)
    {
        // ...
    }
}
```

## Why This Works

`state.Dependency.Complete()` waits for all scheduled jobs that this system depends on to finish before proceeding. Since `BulletCollisionSystem` runs in `FixedStepSimulationSystemGroup` **after** `PhysicsSystemGroup`, it has a dependency on the physics simulation jobs. Calling `Complete()` ensures those jobs finish writing collision events before we read them.

## Performance Impact

**Minimal**: The system already runs after the physics system group, so the physics simulation would complete soon anyway. We're just explicitly waiting for it to finish before proceeding, which happens within the same frame.

## Alternative Approaches (Not Used)

### 1. ICollisionEventsJob (Burst-compiled)
```csharp
struct CollisionEventJob : ICollisionEventsJob
{
    public void Execute(CollisionEvent collisionEvent)
    {
        // Process collision in parallel job
    }
}
```
**Why not**: Requires Burst compilation, can't access `BulletPoolSystem` or `EntityManager` directly from within job.

### 2. Event Buffer Pattern
Store collision events in a buffer, process them later in main thread.
**Why not**: More complex, requires additional component/buffer allocation.

### 3. Change Update Group
Move to a later system group that runs after physics completes.
**Why not**: `FixedStepSimulationSystemGroup` is the correct place for physics-dependent logic.

## Verification

### Before Fix
- Runtime exception thrown immediately when collision occurs
- System couldn't iterate collision events
- Bullets wouldn't be cleaned up on collision

### After Fix
✅ No runtime exceptions
✅ Collision events readable
✅ Bullets properly returned to pool on collision
✅ Clean console logs showing collision cleanup

## Related Unity Documentation

- [Unity Physics Manual - Collision Events](https://docs.unity3d.com/Packages/com.unity.physics@latest/manual/collision-events.html)
- [ECS Job Dependencies](https://docs.unity3d.com/Packages/com.unity.entities@latest/manual/systems-job-dependency.html)
- [JobHandle.Complete()](https://docs.unity3d.com/ScriptReference/Unity.Jobs.JobHandle.Complete.html)

## Testing

To verify the fix works:

1. Enter Play mode
2. Shoot bullets at terrain/enemies
3. Check console for: `[BulletCollisionSystem] Returned X bullets to pool (collision cleanup)`
4. Verify no exceptions thrown
5. Confirm bullets disappear on collision

---

**Fix Applied**: May 7, 2026  
**Status**: ✅ Resolved  
**Performance**: No significant impact (expected behavior)

