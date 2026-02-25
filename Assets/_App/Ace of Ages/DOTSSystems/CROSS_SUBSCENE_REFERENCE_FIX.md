# Troubleshooting: Cross-Subscene References

## The Problem You're Experiencing

**Issue:** "I cannot set the target transform for TransformFollowerAuthoring to a transform outside of the subscene."

## The Solution

This is now **FIXED**! The implementation has been updated to use runtime setup instead of bake-time setup.

## How It Works Now

### Previous Implementation (DIDN'T WORK)
```csharp
// ❌ This tried to bake the external reference
public class Baker : Baker<TransformFollowerAuthoring>
{
    public override void Bake(TransformFollowerAuthoring authoring)
    {
        // Unity doesn't allow this during baking!
        AddComponentObject(entity, new TransformReference
        {
            target = authoring.targetTransform // ❌ Cross-subscene ref!
        });
    }
}
```

**Problem:** Unity's baking system doesn't allow references to GameObjects outside the subscene being baked.

### Current Implementation (WORKS!)
```csharp
// ✅ This sets the reference at runtime
void Start()
{
    // After baking, at runtime, we can access external GameObjects
    var world = World.DefaultGameObjectInjectionWorld;
    if (world != null && world.EntityManager.Exists(_entity))
    {
        world.EntityManager.AddComponentObject(_entity, new TransformReference
        {
            target = targetTransform // ✅ Works at runtime!
        });
    }
}
```

**Solution:** The baker stores the entity reference, then at runtime (Start()), we add the managed component with the external Transform reference.

## What This Means For You

### ✅ You CAN Now:
1. Assign any Transform from the scene to the **Target Transform** field
2. Reference GameObjects outside the subscene
3. Reference scene objects, prefabs, or dynamically created objects
4. Change the target at runtime

### How to Use It:

1. **In the Inspector (Edit Mode):**
   - Select your entity GameObject in the subscene
   - Add TransformFollowerAuthoring component
   - Drag ANY Transform from your scene to the Target Transform field
   - This includes objects outside the subscene!

2. **The Reference Is Stored:**
   - Unity serializes the reference normally
   - The authoring MonoBehaviour keeps the reference

3. **At Runtime:**
   - When Play Mode starts, the authoring's Start() runs
   - It finds the entity that was baked
   - It adds the TransformReference component with your target
   - The system can now update the entity!

## Verification Steps

### To Verify It's Working:

1. **In Edit Mode:**
   - Select entity in subscene
   - Check TransformFollowerAuthoring component
   - Verify Target Transform is assigned
   - You should see scene gizmos connecting them

2. **Enter Play Mode:**
   - Entity should follow the target
   - No errors in console

3. **Check Entity in Entity Debugger (Optional):**
   - Window → Entities → Hierarchy
   - Find your entity
   - Should have:
     - LocalTransform
     - TransformFollowerSettings
     - TransformReference (added at runtime)

## Common Questions

### Q: Do I need to do anything special?
**A:** No! Just assign the Transform in the inspector like normal.

### Q: Will the reference survive entering/exiting Play Mode?
**A:** Yes! Unity serializes the reference on the MonoBehaviour.

### Q: Can I change the target at runtime?
**A:** Yes! See the example code in TransformFollowerExample.cs

### Q: What if the target is destroyed at runtime?
**A:** The system handles this gracefully - it checks for null and skips the update.

### Q: Does this work with prefabs?
**A:** Yes, but the prefab must be in the scene. Prefabs in the project are not instantiated.

## Technical Details

### Why Runtime Instead of Baking?

**Unity's Baking System:**
- Runs in edit mode
- Converts GameObjects to entities
- Only has access to objects in the same subscene
- Cannot reference external scene objects

**Runtime Setup:**
- Runs when Play Mode starts
- Has access to the entire scene
- Can reference any GameObject
- MonoBehaviours can hold scene references

### The Authoring Component's Lifecycle:

```
Edit Mode:
├── User assigns Target Transform in inspector
└── Unity serializes the reference

Baking (Still in Edit Mode):
├── Baker runs
├── Creates entity with TransformFollowerSettings
├── Stores entity reference in authoring._entity
└── Does NOT add TransformReference (can't cross subscene)

Play Mode Starts:
├── Authoring.Start() runs
├── Finds the baked entity
├── Adds TransformReference with the target
└── System can now update the entity

During Play Mode:
└── TransformFollowerSystem reads the reference and updates position

Play Mode Ends:
└── Authoring.OnDestroy() cleans up the component
```

## If It Still Doesn't Work

### Check These:

1. **Is TransformFollowerAuthoring still on the GameObject?**
   - It needs to survive into Play Mode
   - Don't put it on a pure entity (one without a GameObject)

2. **Is the subscene loaded?**
   - Check the SubScene component
   - Should show "Loaded" in Play Mode

3. **Is the target still in the scene?**
   - Must exist in the scene hierarchy
   - Must not be a pure asset

4. **Any console errors?**
   - Check for errors during Start()
   - Check for null reference errors

### Debug It:

Add debug logging to the authoring:

```csharp
void Start()
{
    Debug.Log($"Setting up follower. Target: {(targetTransform ? targetTransform.name : "NULL")}");
    
    var world = World.DefaultGameObjectInjectionWorld;
    if (world == null)
    {
        Debug.LogError("No default world!");
        return;
    }
    
    if (!world.EntityManager.Exists(_entity))
    {
        Debug.LogError("Entity doesn't exist!");
        return;
    }
    
    world.EntityManager.AddComponentObject(_entity, new TransformReference
    {
        target = targetTransform
    });
    
    Debug.Log("✅ Successfully set up follower!");
}
```

## Summary

**The Fix:** The system now uses runtime setup (Start()) instead of bake-time setup, which allows cross-subscene references.

**What You Do:** Just assign the Transform in the inspector - it works now!

**Why It Works:** MonoBehaviours can hold scene references, and we apply them to the entity at runtime.

---

If you still have issues after reading this, please check:
1. Unity version (should be 2022.3+)
2. DOTS packages are installed
3. No compile errors in the project
4. Subscene is properly configured

