# Transform Follower for DOTS Entities

This solution allows entities in a DOTS subscene to follow a Transform defined outside of the subscene.

## Files Created

1. **TransformFollowerAuthoring.cs** - Authoring component to set up the follower
2. **TransformFollowerSystemOptimized.cs** - Runtime system (batched Transform reads + parallel Burst updates)
3. **TransformFollowerInitSystem.cs** - Runtime target search initialization

## Usage

### Basic Setup

1. In your subscene, select an entity GameObject
2. Add the `TransformFollowerAuthoring` component
3. Assign the target Transform (can be from outside the subscene)
4. Configure offset, rotation following, and smoothing as needed
5. The entity will automatically follow the Transform at runtime

**Important Note About Cross-Subscene References:**
You CAN assign a Transform from outside the subscene in the inspector! The authoring component stores this reference and applies it at **runtime** (in Start()), not during baking. This is the workaround that allows cross-subscene references to work, since Unity's baking system doesn't allow direct cross-subscene references.

### Settings

- **Target Transform**: The Transform to follow (GameObject outside the subscene)
- **Offset**: Local offset from the target position
- **Follow Rotation**: Whether to match the target's rotation
- **Smooth Time**: Smoothing factor (0 = instant snap, higher values = smoother interpolation)

## The Fundamental Limitation

**DOTS entities cannot directly reference GameObjects or Transforms in a Burst-compatible way.**

This is because:
- DOTS is designed for data-oriented, cache-friendly, deterministic processing
- GameObjects/MonoBehaviours are managed objects with garbage collection
- Burst-compiled code and Jobs cannot access managed references
- Subscene entities exist in the ECS World, while GameObjects exist in the GameObject World

## How This Solution Works

`TransformFollowerSystemOptimized` uses a **managed component** (`TransformReference`) to store the Transform reference, then batches reads and updates entities in parallel:

```csharp
// Managed component - can hold GameObject references
public class TransformReference : IComponentData
{
    public Transform target;
}
```

1. **Main Thread Phase**: Collect all Transform positions/rotations into a `NativeArray`
2. **Job Phase**: Use Burst-compiled parallel job to update all entity positions

**Pros:**
- Uses Burst compilation for entity updates
- Can process entities in parallel
- Good performance across follower counts

**Cons:**
- Still requires main thread to read Transform data
- Managed Transform references cannot be Burst-compiled directly

## Alternative Approaches

If you need better performance or different behavior, consider:

### 1. Transform Access Array (For Hybrid Approach)
```csharp
// Use TransformAccessArray for efficient Transform access from jobs
TransformAccessArray transformAccessArray;
```

### 2. Singleton Pattern
Store a single Transform reference in a singleton component:
```csharp
public class FollowTargetSingleton : IComponentData
{
    public Transform target;
}
```

### 3. Copy GameObject to Entity
Convert the external GameObject to an entity:
```csharp
// Use EntityManager to create an entity from the GameObject
Entity targetEntity = EntityManager.CreateEntity();
```

### 4. Use ComponentObject
For read-only access, ComponentObject can be more efficient:
```csharp
// Reference a component directly
public class TransformComponentObject : IComponentData
{
    public UnityEngine.Transform transform;
}
```

## Performance Considerations

- **Few Followers (< 10)**: Use the simple system
- **Many Followers (100+)**: Use the optimized system
- **Very Performance Critical**: Consider converting the followed GameObject to an entity
- **Physics-Based**: Add PhysicsVelocity component and update velocity instead of position

## Debugging

To debug, check:
1. Is the target Transform assigned in the authoring component?
2. Is the entity in a subscene that's loaded?
3. Check the Entity Inspector to verify components are baked correctly
4. Use `Debug.Log` in the system to verify it's running

## Example Scene Setup

```
Scene Hierarchy:
├── GameManager (outside subscene)
│   └── PlayerTransform ← This is the target
├── DOTS Subscene
│   └── FollowerEntity
│       └── TransformFollowerAuthoring
│           └── targetTransform → PlayerTransform
```

## Known Limitations

1. **Performance**: Reading Transform data requires main thread access
2. **Memory**: Managed components create GC pressure
3. **Determinism**: Transform data is not deterministic (affected by Unity's Transform system)
4. **Netcode**: This approach won't work well with Unity Netcode for Entities (managed components aren't serialized)
5. **Subscene Baking**: The Transform reference must be set at bake time or runtime (not both)

## When NOT to Use This

Avoid this pattern if:
- You need deterministic physics/gameplay
- You're using Unity Netcode for Entities
- You need maximum performance (convert both to entities instead)
- The "target" is also manageable as an entity

## License

Free to use in your project.

