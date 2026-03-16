# GameObject Tracking for Infinite Terrain System

## Overview

The infinite terrain system now tracks a **GameObject Transform** (e.g., VR player rig, AutoHandPlayer, or camera) instead of requiring a `PlayerTag` ECS entity in the subscene. This makes it much easier to integrate with existing MonoBehaviour-based player systems.

## How It Works

The system uses a managed ECS component called `PlayerTransformReference` that holds a reference to a Unity Transform. Both the `FloatingOriginSystem` and `TileSpawningSystem` read the player's position directly from this Transform each frame.

### Key Components

1. **PlayerTransformReference** (managed IComponentData)
   - Holds a reference to the player's Transform
   - Set up in `TerrainConfigAuthoring` during baking

2. **FloatingOriginSystem**
   - Monitors the player's distance from origin
   - Triggers world shifts when threshold is exceeded
   - Shifts GameObject via `FloatingOriginEvents`

3. **TileSpawningSystem**
   - Spawns/despawns terrain tiles based on player position
   - Calculates grid coordinates from GameObject position

## Setup Instructions

### 1. Assign Player in TerrainConfigAuthoring

In your scene, select the GameObject with the `TerrainConfigAuthoring` component:

1. In the Inspector, find the **Player Tracking** section at the top
2. Drag your player GameObject into the `Player To Track` field
   - For VR: Use the XR Origin or AutoHandPlayer GameObject
   - For desktop: Use the Main Camera or player controller GameObject

**Auto-Detection:** If you leave this field empty, the system will automatically try to find:
- `AutoHandPlayer` (if using AutoHand VR framework)
- Main Camera (as a fallback)

### 2. Configure Floating Origin GameObject Shifter

The `FloatingOriginGameObjectShifter` component ensures your player GameObject shifts along with the terrain when the origin resets:

1. Add `FloatingOriginGameObjectShifter` to a GameObject in your scene (can be on the same GameObject as `TerrainConfigAuthoring`)
2. In the Inspector:
   - **Transforms To Shift**: Drag the **root Transform** of your player rig (e.g., XR Origin's parent)
   - **Update Device Tracking Immediate**: Keep checked (true) if using VR with `DeviceTracking`
   - **Debug Log**: Enable if you want to see when origin shifts occur

**Important:** The transform you add should be the **root** of your player hierarchy, not a child camera or hand. For VR, this is typically:
- The XR Origin GameObject
- The AutoHandPlayer GameObject's parent
- The Tracking Origin Transform

### 3. Test in Play Mode

Press Play and move your player around:

1. **Terrain spawns** around your GameObject position
2. **Tiles despawn** when you move away from them
3. **Origin shifts** occur automatically when you move far from (0,0,0)
   - You'll see a debug log message when this happens
   - The world will shift smoothly without visible artifacts

## Visualizing the System

When you select the GameObject with `TerrainConfigAuthoring` in the Scene view:

- **Magenta sphere + line**: Player position (if assigned)
- **Green wireframe sphere**: View distance (active tile radius)
- **Yellow wireframe sphere**: Shift threshold (origin reset distance)
- **Cyan wireframe cube**: Sample tile at player position

## Migration from PlayerTag System

If you were previously using the `PlayerTag` component on an ECS entity:

### What Changed

- **Removed**: `PlayerTag` component requirement
- **Added**: `PlayerTransformReference` managed component
- **Modified**: `FloatingOriginSystem` and `TileSpawningSystem` now read from GameObject Transform

### Migration Steps

1. **Remove PlayerTagAuthoring** from any entities in your subscene
2. **Assign playerToTrack** in `TerrainConfigAuthoring` to your player GameObject
3. **Update FloatingOriginGameObjectShifter** to include your player's root transform
4. **Test** - The terrain should now follow your GameObject

### Cleanup (Optional)

You can safely delete these files if no longer needed:
- `Assets/_App/Ace of Ages/Player/PlayerTagAuthoring.cs`
- `Assets/_App/Ace of Ages/Player/PlayerMover.cs` (if it was only for testing)

## Performance Considerations

### Managed Component Overhead

The `PlayerTransformReference` is a **managed component** (class), which means:
- ✅ Can hold references to GameObject Transforms
- ✅ Easy to set up and use
- ⚠️ Cannot use Burst compilation for systems that read it
- ⚠️ Slightly slower than pure ECS approach

**Impact:** Minimal - only a single player Transform is accessed per frame, and the overhead is negligible compared to terrain generation and rendering.

### System Execution

Both systems use `SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>()` to access the managed component, which runs on the main thread. This is necessary because:
- Managed objects cannot be accessed from Burst jobs
- Transform.position access requires main thread execution

## Troubleshooting

### "Player transform reference is null" Warning

**Cause:** The `playerToTrack` field in `TerrainConfigAuthoring` is not assigned.

**Solution:**
1. Select the TerrainConfig GameObject in the scene
2. Assign your player GameObject to the `Player To Track` field
3. If auto-detection failed, make sure you have either:
   - An AutoHandPlayer component in the scene
   - A Camera tagged as "MainCamera"

### Terrain Not Following Player

**Check:**
1. Is `playerToTrack` assigned in `TerrainConfigAuthoring`?
2. Is the assigned GameObject active in the hierarchy?
3. Is the assigned GameObject actually moving?
4. Check Console for any errors from the terrain systems

### Player Doesn't Shift During Origin Reset

**Cause:** `FloatingOriginGameObjectShifter` is not configured or missing.

**Solution:**
1. Add `FloatingOriginGameObjectShifter` component to a GameObject in your scene
2. Assign your player's **root Transform** to `Transforms To Shift`
3. Enable debug logging to verify shifts are occurring

### Terrain "Jumps" After Origin Shift

**Cause:** The player GameObject is not being shifted by `FloatingOriginGameObjectShifter`.

**Solution:**
1. Verify `FloatingOriginGameObjectShifter` is in the scene and enabled
2. Check that the player's root transform is in the `Transforms To Shift` array
3. Make sure the transform is not being overridden by another script after the shift

## Advanced Usage

### Tracking Multiple Objects

While the system is designed for a single player, you can modify it to track multiple objects:

1. Change `PlayerTransformReference.playerTransform` to a list or array
2. Modify systems to calculate center point of all tracked objects
3. Update `FloatingOriginGameObjectShifter` to shift all tracked objects

### Custom Player Position Logic

If you need to track something other than a Transform's position:

1. Create a MonoBehaviour that updates the tracked Transform's position each frame
2. This could average multiple positions, apply offsets, or use custom logic
3. Assign this dummy Transform to `playerToTrack`

### Runtime Player Assignment

To change the tracked player at runtime:

```csharp
using Unity.Entities;

public void SetTrackedPlayer(Transform newPlayer)
{
    var world = World.DefaultGameObjectInjectionWorld;
    var entityManager = world.EntityManager;
    
    // Find the entity with PlayerTransformReference
    var query = entityManager.CreateEntityQuery(typeof(PlayerTransformReference));
    var entity = query.GetSingletonEntity();
    
    // Update the reference
    var playerRef = entityManager.GetComponentObject<PlayerTransformReference>(entity);
    playerRef.playerTransform = newPlayer;
    
    query.Dispose();
    
    Debug.Log($"Now tracking: {newPlayer.name}");
}
```

## Technical Details

### Why Managed Components?

Unity DOTS typically uses unmanaged structs for performance, but accessing GameObjects/Transforms from ECS requires managed components because:
- GameObject references are managed objects
- Transform.position requires Unity's C# API (not available in Burst jobs)
- Baking can't always resolve runtime GameObject references

### Baking Process

During baking, `TerrainConfigAuthoring.Baker`:
1. Creates an entity with configuration singletons
2. Adds `PlayerTransformReference` with the assigned Transform
3. If `playerToTrack` is null during baking, it can be assigned at runtime

### System Update Order

```
SimulationSystemGroup
└── TileSpawningSystem (reads player position, spawns tiles)
    └── TransformSystemGroup
        └── FloatingOriginSystem (checks distance, triggers shifts)
```

This order ensures tiles are updated before any visual transforms are calculated.

## See Also

- [README.md](./README.md) - Main terrain system documentation
- [FLOATING_ORIGIN_GAMEOBJECT_README.md](./FLOATING_ORIGIN_GAMEOBJECT_README.md) - Detailed floating origin guide
- [FloatingOriginGameObjectShifter.cs](./FloatingOriginGameObjectShifter.cs) - GameObject shift implementation
- [TransformFollower/](../TransformFollower/) - Alternative ECS-GameObject bridge pattern

