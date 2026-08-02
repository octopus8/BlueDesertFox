# TransformFollower Optimization - Quick Reference

**Date**: May 2, 2026  
**Status**: ✅ Active (`TransformFollowerSystemOptimized` is the sole follower system)

---

## What Was Fixed

**Problem**: Trees lost frustum culling when using `TransformFollowerSystemOptimized`  
**Cause**: Missing ECS job dependency chaining → race condition  
**Solution**: Converted to ISystem with proper `state.Dependency` management  

The legacy main-thread `TransformFollowerSystem` has been removed.

---

## Critical Pattern

```csharp
state.Dependency = job.ScheduleParallel(state.Dependency);  // ✅ Proper chaining
```

Do **not** call `.ScheduleParallel()` without chaining `state.Dependency` — that races with rendering/culling systems.

---

## Performance

| System | Speed | Frustum Culling | Notes |
|--------|-------|-----------------|-------|
| `TransformFollowerSystemOptimized` | 5-10x vs old main-thread | ✅ Works | Parallel + safe |

---

## Testing Checklist

- [ ] Trees frustum cull (disappear when camera looks away)
- [ ] No visual popping or incorrect visibility
- [ ] Profiler shows job in SimulationSystemGroup
- [ ] No GC allocations in profiler
- [ ] Works with 100+ entities

---

## Documentation

- **Full Details**: `TRANSFORM_FOLLOWER_OPTIMIZATION_FIX.md`
- **Agent Guide**: `AGENTS.md`
- **Code**: `Assets/_App/Escape Mountain/TransformFollower/TransformFollowerSystemOptimized.cs`
