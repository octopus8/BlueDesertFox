# Quick Start Guide: Transform Follower

## 5-Minute Setup

### Step 1: Prepare Your Scene

1. Create or open a scene with a DOTS subscene
2. Make sure you have a GameObject **outside** the subscene that you want to follow (e.g., the player, camera, or any moving object)

### Step 2: Add the Component to an Entity

1. **In the subscene**, select (or create) a GameObject that will become an entity
2. Add Component → **TransformFollowerAuthoring**
3. Drag the target Transform (from outside the subscene) into the **Target Transform** field

### Step 3: Configure (Optional)

- **Offset**: Set a local offset from the target (e.g., `0, 2, -5` to stay behind and above)
- **Follow Rotation**: Check this if you want the entity to match the target's rotation
- **Smooth Time**: Set to `0` for instant following, or `0.1` to `0.5` for smooth interpolation

### Step 4: Test

1. Enter Play Mode
2. The entity should now follow the target Transform!

---

## Common Use Cases

### Use Case 1: UI Element Following Player
```
Target: Player GameObject
Offset: (0, 2, 0) - Floats 2 units above player
Follow Rotation: false
Smooth Time: 0.2
```

### Use Case 2: Enemy Projectile Homing
```
Target: Player GameObject
Offset: (0, 0, 0)
Follow Rotation: true - Aims at player
Smooth Time: 0.05 - Quick but smooth
```

### Use Case 3: Camera Anchor Point
```
Target: Main Camera
Offset: (0, 0, 5) - In front of camera
Follow Rotation: true
Smooth Time: 0
```

### Use Case 4: Formation Following Leader
```
Target: Leader GameObject
Offset: (-2, 0, -2) - Behind and to the left
Follow Rotation: false
Smooth Time: 0.3 - Smooth formation movement
```

---

## Troubleshooting

### Entity doesn't follow the target

**Check:**
- ✓ Is the target Transform field assigned in the authoring component?
- ✓ Is the subscene loaded? (Check the SubScene component in the hierarchy)
- ✓ Is the entity GameObject in the subscene (not outside)?
- ✓ Is the target GameObject outside the subscene (or at least not being baked)?

### Performance is slow

**Solution:**
- If you have more than 50 followers, enable the optimized system:
  1. Open `TransformFollowerSystem.cs`
  2. Add `[DisableAutoCreation]` above the class
  3. Open `TransformFollowerSystemOptimized.cs`
  4. Remove `[DisableAutoCreation]` from above the class

### Entity snaps instead of smoothing

**Fix:**
- Increase the **Smooth Time** value (try 0.1 to 0.5)

### Entity lags behind target

**Explanation:**
- This is expected with smoothing enabled
- Reduce **Smooth Time** for less lag (but more snapping)
- Set to `0` for instant following with no lag

---

## Runtime Control (Advanced)

### Change Target at Runtime

```csharp
// Get entity manager
var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

// Update the target for a specific entity
entityManager.SetComponentData(myEntity, new TransformReference 
{ 
    target = newTarget 
});
```

### Query All Followers

```csharp
var query = entityManager.CreateEntityQuery(
    typeof(TransformFollowerSettings),
    typeof(TransformReference)
);
var entities = query.ToEntityArray(Allocator.Temp);
// ... process entities
entities.Dispose();
```

See `TransformFollowerExample.cs` for more runtime examples.

---

## The Fundamental Limitation (Technical)

**Why managed components?**

DOTS entities normally can't reference GameObjects because:
- GameObjects are managed (garbage collected)
- DOTS is unmanaged (no GC, cache-friendly)
- Burst compilation can't access managed references

**Our workaround:**
- Use a managed component (`TransformReference`) to bridge the gap
- The system runs on the main thread (can't use Burst for the Transform access)
- This is a necessary trade-off to reference external GameObjects

**Alternative:**
- Convert both objects to entities (fully ECS, no managed references needed)
- This gives maximum performance but requires both to be in the ECS world

---

## Next Steps

- Read `TransformFollowerREADME.md` for in-depth documentation
- Check `TransformFollowerExample.cs` for code examples
- Experiment with different Offset and Smooth Time values
- Consider converting to full ECS if performance becomes critical

---

**Questions?** Check the full README or Unity DOTS documentation.

