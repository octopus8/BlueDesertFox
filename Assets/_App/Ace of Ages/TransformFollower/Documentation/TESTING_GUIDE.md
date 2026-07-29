# Testing the Transform Follower System

## Simple Test Setup (5 minutes)

Follow these steps to quickly test that everything works:

### Step 1: Create Test Objects

#### 1.1 Create the Target (Outside Subscene)
1. In your scene (NOT in a subscene), create an empty GameObject
2. Name it "FollowTarget"
3. Position it at (0, 0, 0)
4. Add a simple script to move it (or move it manually in play mode)

**Optional Test Script:**
```csharp
using UnityEngine;

public class MoveInCircle : MonoBehaviour
{
    public float speed = 2f;
    public float radius = 5f;
    
    void Update()
    {
        float x = Mathf.Cos(Time.time * speed) * radius;
        float z = Mathf.Sin(Time.time * speed) * radius;
        transform.position = new Vector3(x, 1, z);
    }
}
```

#### 1.2 Create the Follower (Inside Subscene)
1. Open or create a SubScene
2. Inside the subscene, create a Cube GameObject
3. Name it "FollowerEntity"
4. Add the **TransformFollowerAuthoring** component
5. In the inspector:
   - **Target Transform**: Drag the "FollowTarget" GameObject here
   - **Offset**: Set to (0, 2, 0) to float above the target
   - **Follow Rotation**: Leave unchecked for now
   - **Smooth Time**: Set to 0.2 for smooth following

### Step 2: Verify Setup

Before entering Play Mode:
- ✅ "FollowTarget" is in the main scene (NOT in subscene)
- ✅ "FollowerEntity" is in the subscene
- ✅ TransformFollowerAuthoring component is on FollowerEntity
- ✅ Target Transform field is assigned
- ✅ You can see yellow/green gizmos connecting them in Scene View

### Step 3: Test

1. Enter Play Mode
2. Move the "FollowTarget" in the Scene View (or let the script move it)
3. Watch the follower entity track it with an offset
4. Success! ✅

---

## Advanced Test Scenarios

### Test 2: Multiple Followers

Test with several followers at different offsets:

```
SubScene:
├── Follower_Above      offset: (0, 3, 0)
├── Follower_Behind     offset: (0, 0, -3)
├── Follower_Left       offset: (-3, 0, 0)
└── Follower_Right      offset: (3, 0, 0)
```

All following the same target → Creates a formation!

### Test 3: Rotation Following

1. On FollowerEntity, check **Follow Rotation**
2. Rotate the target
3. Follower should match rotation

### Test 4: Smooth vs Instant

Compare different smooth time values:
- **0.0** → Instant (no lag, but snappy)
- **0.1** → Fast and smooth
- **0.3** → Slow and smooth (noticeable lag)
- **1.0** → Very slow (significant lag)

### Test 5: Runtime Changes

Add this to a MonoBehaviour on the target:

```csharp
void Update()
{
    if (Input.GetKeyDown(KeyCode.Space))
    {
        transform.position = Random.insideUnitSphere * 10;
        Debug.Log("Teleported target!");
    }
}
```

Press Space in play mode → Follower should catch up based on smooth time

---

## Debugging Checklist

### Problem: Follower doesn't move

**Check:**
- [ ] Is the subscene loaded? (Check SubScene component - should show "Loaded")
- [ ] Is the target assigned in TransformFollowerAuthoring?
- [ ] Is TransformFollowerSystem enabled?
  - Open `TransformFollowerSystem.cs`
  - Should NOT have `[DisableAutoCreation]` attribute
- [ ] Are you in Play Mode?
- [ ] Does the target actually move?

**Debug:**
```csharp
// Add this to TransformFollowerSystem.cs OnUpdate:
Debug.Log($"Processing {_followerQuery.CalculateEntityCount()} followers");
```

### Problem: Follower is in wrong position

**Check:**
- [ ] Offset value is correct
- [ ] Target Transform is the right object
- [ ] Both objects are where you expect in Scene View

**Debug:**
```csharp
// In TransformFollowerSystem.cs, in the ForEach:
Debug.Log($"Target: {transformRef.target.position}, Entity: {localTransform.Position}");
```

### Problem: Performance is bad

**Solutions:**
1. How many followers? If >100, enable optimized system
2. Check profiler for main thread bottlenecks
3. Reduce smooth time (less interpolation)
4. Consider converting to full ECS

---

## Performance Test

### Simple Load Test

Create many followers:

```csharp
// Add to a MonoBehaviour in the scene
void Start()
{
    var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    var target = GameObject.Find("FollowTarget").transform;
    
    // Create 100 follower entities
    for (int i = 0; i < 100; i++)
    {
        Entity entity = entityManager.CreateEntity();
        entityManager.AddComponentData(entity, LocalTransform.Identity);
        entityManager.AddComponentData(entity, new TransformFollowerSettings
        {
            offset = Random.insideUnitSphere * 5,
            followRotation = false,
            smoothTime = 0.1f
        });
        entityManager.AddComponentData(entity, new TransformReference
        {
            target = target
        });
    }
    
    Debug.Log("Created 100 followers");
}
```

**Expected Performance:**
- Simple system: ~0.1-0.2ms
- Optimized system: ~0.05-0.1ms
- Check profiler: Window → Analysis → Profiler

---

## Visual Test Checklist

When everything works correctly:

### In Scene View (Edit Mode)
- [ ] See cyan line from follower to target
- [ ] See yellow sphere at target position
- [ ] See green sphere at target + offset position
- [ ] See text label with target name and offset

### In Scene View (Play Mode)
- [ ] Follower moves when target moves
- [ ] Follower maintains offset distance
- [ ] Movement is smooth (if smooth time > 0)
- [ ] Rotation matches (if followRotation = true)

### In Inspector
- [ ] TransformFollowerAuthoring shows all fields
- [ ] Preset buttons are visible
- [ ] Help text is available (click "?" button)
- [ ] No console errors

---

## Example Test Scene Hierarchy

```
Scene: TestScene
│
├── FollowTarget (GameObject - NOT in subscene)
│   ├── Transform
│   └── MoveInCircle script (optional)
│
├── Main Camera
│   └── Position: (0, 5, -10), Rotation: (30, 0, 0)
│
├── Directional Light
│
└── SubScene_Test
    └── (Right-click, Open SubScene)
        │
        ├── FollowerEntity_1 (Cube)
        │   ├── TransformFollowerAuthoring
        │   │   ├── Target: FollowTarget
        │   │   ├── Offset: (0, 2, 0)
        │   │   ├── Follow Rotation: false
        │   │   └── Smooth Time: 0.2
        │   └── Visual: MeshRenderer with material
        │
        └── FollowerEntity_2 (Sphere)
            ├── TransformFollowerAuthoring
            │   ├── Target: FollowTarget
            │   ├── Offset: (0, 0, -3)
            │   ├── Follow Rotation: false
            │   └── Smooth Time: 0.1
            └── Visual: MeshRenderer with material
```

---

## Console Output (Expected)

When working correctly, you should see:

```
[Subscene] SubScene_Test loaded
[TransformFollowerSystem] Processing 2 followers
```

No errors or warnings.

---

## Unit Tests (Optional)

If you want to write tests:

```csharp
[Test]
public void TransformFollower_FollowsTarget()
{
    // Create target GameObject
    var target = new GameObject("Target");
    target.transform.position = new Vector3(5, 0, 0);
    
    // Create entity with follower
    var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    Entity entity = entityManager.CreateEntity();
    entityManager.AddComponentData(entity, LocalTransform.Identity);
    entityManager.AddComponentData(entity, new TransformFollowerSettings
    {
        offset = float3.zero,
        followRotation = false,
        smoothTime = 0
    });
    entityManager.AddComponentData(entity, new TransformReference
    {
        target = target.transform
    });
    
    // Update system
    World.DefaultGameObjectInjectionWorld.Update();
    
    // Check position
    var transform = entityManager.GetComponentData<LocalTransform>(entity);
    Assert.AreEqual(5, transform.Position.x, 0.01f);
}
```

---

## Next Steps After Testing

Once you confirm it works:

1. ✅ Test complete - system is working!
2. 📖 Read full documentation for advanced usage
3. 🔧 Integrate into your game
4. ⚡ Optimize if needed (enable optimized system for many followers)
5. 🎮 Ship it!

---

**Happy testing! 🧪**

