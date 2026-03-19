# Player Tracking Setup Guide

Complete guide to configuring player GameObject tracking for terrain centering.

## Why Player Tracking?

The terrain system needs to know where the player is to:
- Spawn tiles around the player's position
- Despawn tiles far from the player
- Apply auto-scrolling relative to player position
- Calculate camera-aware prioritization

## How It Works

### Cross-Scene Reference Problem

**Challenge**: 
- TerrainConfigAuthoring is in an ECS SubScene (required for baking)
- Player GameObject is in the main scene (VR rig, camera, etc.)
- Can't reference GameObjects across scenes during baking

**Solution**: Runtime initialization using search components
- `PlayerTrackingSearch` component stores search parameters (baked)
- `PlayerTrackingInitSystem` runs at startup to find player
- `PlayerTransformReference` gets populated with found Transform (runtime)

### System Flow

```
1. Baking Time:
   TerrainConfigAuthoring → PlayerTrackingSearch (search mode + params)
                         → PlayerTransformReference (empty, will be filled)

2. Runtime (InitializationSystemGroup):
   PlayerTrackingInitSystem reads PlayerTrackingSearch
                          → searches for player GameObject
                          → populates PlayerTransformReference

3. Runtime (SimulationSystemGroup):
   TileSpawningSystem reads PlayerTransformReference
                    → spawns tiles around player position
```

## Configuration Options

### Option 1: AutoDetect (Recommended)

**When to use**: Most cases, especially VR projects

**How it works**:
1. Tries to find `AutoHandPlayer` component first
2. If not found, falls back to `Camera.main`

**Configuration**:
```
Player Search Mode: AutoDetect
Player Name: (not used)
Player Tag: (not used)
```

**Pros**:
- Zero configuration
- Works for VR and desktop
- Automatic fallback

**Cons**:
- Less explicit, harder to debug if multiple candidates exist

---

### Option 2: Find AutoHand Player

**When to use**: VR project using Autohand package

**How it works**: Searches for `Autohand.AutoHandPlayer` component using `FindFirstObjectByType<>()`

**Configuration**:
```
Player Search Mode: FindAutoHandPlayer
Player Name: (not used)
Player Tag: (not used)
```

**Pros**:
- Explicit VR player tracking
- Fast search (component-based)

**Cons**:
- Requires Autohand package
- Fails if AutoHandPlayer doesn't exist

---

### Option 3: Find Main Camera

**When to use**: Simple projects where camera represents player, or non-VR

**How it works**: Uses `Camera.main` to get the main camera's Transform

**Configuration**:
```
Player Search Mode: FindMainCamera
Player Name: (not used)
Player Tag: (not used)
```

**Requirements**:
- Camera must have "MainCamera" tag

**Pros**:
- Simple and reliable
- Works for most single-camera projects

**Cons**:
- Tracks camera position, not player body
- May not be ideal for VR (camera is inside head)

---

### Option 4: Find by Name

**When to use**: When you know the exact GameObject name

**How it works**: Uses `GameObject.Find(name)` to search by name

**Configuration**:
```
Player Search Mode: FindByName
Player Name: "XR Origin Hands (XR Rig)"
Player Tag: (not used)
```

**Pros**:
- Explicit and predictable
- Works for any GameObject

**Cons**:
- Name must match exactly (case-sensitive)
- Slower than component or tag search
- Name changes break tracking

**Tips**:
- Use full path if ambiguous: "XR Origin/Camera Offset/Main Camera"
- Check spelling carefully
- Test with TerrainTrackingDebugger

---

### Option 5: Find by Tag

**When to use**: When multiple scenes share same player tag

**How it works**: Uses `GameObject.FindGameObjectWithTag(tag)`

**Configuration**:
```
Player Search Mode: FindByTag
Player Name: (not used)
Player Tag: "Player"
```

**Requirements**:
- GameObject must have the specified tag

**Pros**:
- Tag-based, survives name changes
- Faster than name search
- Standard Unity pattern

**Cons**:
- Requires tag to be set
- Returns first match only (undefined if multiple)

---

## Troubleshooting Player Tracking

### Using TerrainTrackingDebugger

The easiest way to diagnose tracking issues:

1. Add `TerrainTrackingDebugger` component to any GameObject
2. Enter Play mode
3. Right-click component → `Check Tracking Status`
4. Review console output

**Example Output (Success)**:
```
=== Terrain Tracking Status ===
🔍 Search Mode: FindAutoHandPlayer
🔍 Search String: ''
🔍 Initialized: True
✅ Tracking: XR Origin Hands (XR Rig)
   GameObject: XR Origin Hands (XR Rig)
   Position: (0.0, 1.5, 0.0)
   Active: True
📦 Active Terrain Tiles: 25
```

**Example Output (Failure)**:
```
=== Terrain Tracking Status ===
🔍 Search Mode: FindByName
🔍 Search String: 'PlayerRig'
🔍 Initialized: True
⚠️ PlayerTransformReference exists but Transform is null!
   Player search completed but failed to find GameObject.
   Check that a GameObject matching search mode 'FindByName' exists.
```

### Common Issues

#### Issue 1: "No PlayerTransformReference singleton found"

**Cause**: TerrainConfigAuthoring not in a SubScene or not baked

**Solution**:
1. Ensure TerrainConfigAuthoring is in a SubScene
2. Close and reopen SubScene to trigger baking
3. Check SubScene is included in build settings

---

#### Issue 2: "Transform is null" but initialized = true

**Cause**: Player search completed but failed to find matching GameObject

**Solutions**:
- **FindByName**: Check GameObject name spelling (case-sensitive)
- **FindByTag**: Verify GameObject has the tag
- **FindAutoHandPlayer**: Ensure Autohand package installed and AutoHandPlayer exists
- **FindMainCamera**: Ensure camera tagged as "MainCamera"

**Debug Steps**:
1. Open console when system runs
2. Look for PlayerTrackingInitSystem logs showing search attempts
3. System logs all similar GameObjects if search fails
4. Try different search mode

---

#### Issue 3: "Could not find player GameObject"

**Cause**: Player doesn't exist when PlayerTrackingInitSystem runs

**Solutions**:
- Ensure player GameObject is active in hierarchy when scene starts
- If player spawns later, change to a search mode that will find it
- Check player isn't disabled or destroyed at startup

---

#### Issue 4: Tracking works but terrain doesn't spawn

**Cause**: Tracking succeeded but tile spawning has different issue

**Debug Steps**:
1. Verify player position is not at extreme coordinates (>100,000)
2. Check TerrainTileConfig has reasonable values
3. Add TerrainTileGizmoVisualizer to see if tiles exist but invisible
4. See [Troubleshooting Guide](TROUBLESHOOTING.md)

---

## Advanced: Custom Player Tracking

### Tracking a Specific Child Transform

If you want to track a specific child (e.g., camera offset):

```csharp
// Find by name using full path
Player Search Mode: FindByName
Player Name: "XR Origin/Camera Offset/Main Camera"
```

### Tracking Custom Player Controller

For custom controllers without AutoHandPlayer:

**Option A**: Add "Player" tag to your controller
```
Player Search Mode: FindByTag
Player Tag: "Player"
```

**Option B**: Use GameObject name
```
Player Search Mode: FindByName
Player Name: "MyCustomPlayerController"
```

### Multiple Players

The system currently supports ONE player only. For multiplayer:

**Option 1**: Create separate terrain instances per player  
**Option 2**: Modify `PlayerTransformReference` to support multiple Transforms  
**Option 3**: Track a "center point" GameObject that averages player positions

---

## Player Tracking API Reference

### Components

#### PlayerTransformReference
**Type**: Managed IComponentData (class)  
**Purpose**: Stores reference to player GameObject's Transform

```csharp
public class PlayerTransformReference : IComponentData
{
    public UnityEngine.Transform playerTransform;
}
```

**Usage**: Read-only by most systems, written by PlayerTrackingInitSystem

---

#### PlayerTrackingSearch
**Type**: IComponentData (struct)  
**Purpose**: Stores search parameters for finding player at runtime

```csharp
public struct PlayerTrackingSearch : IComponentData
{
    public enum Mode : byte
    {
        FindByName = 0,
        FindByTag = 1,
        FindAutoHandPlayer = 2,
        FindMainCamera = 3
    }
    
    public Mode mode;
    public FixedString128Bytes searchString;
    public bool initialized;
}
```

**Fields**:
- `mode` - How to search for player
- `searchString` - Name or tag (only used for FindByName/FindByTag modes)
- `initialized` - True after PlayerTrackingInitSystem runs successfully

---

### System

#### PlayerTrackingInitSystem
**Update Group**: InitializationSystemGroup  
**Purpose**: Finds player GameObject and populates PlayerTransformReference

**Execution**:
- Runs every frame until all entities with PlayerTrackingSearch are initialized
- Logs search attempts and results to console
- Implements fallback logic (AutoDetect tries multiple modes)

**Requirements**:
- Entities must have both PlayerTrackingSearch and PlayerTransformReference components
- Player GameObject must exist and be active when system runs

---

## Testing Player Tracking

### Test 1: Verify in Editor
1. Add TerrainConfigAuthoring to SubScene
2. Configure player search mode
3. Select TerrainConfig GameObject in Scene view
4. Look for magenta sphere gizmo at player position
5. If visible → tracking will work at runtime

### Test 2: Runtime Check
1. Enter Play mode
2. Open Console
3. Look for:
   ```
   [PlayerTrackingInitSystem] ✅ Found player: [name]
   ```
4. If missing → tracking failed

### Test 3: Use Debug Tool
1. Add TerrainTrackingDebugger to scene
2. Right-click → Check Tracking Status
3. Review detailed output

## Best Practices

✅ **Use AutoDetect** for most projects  
✅ **Keep SubScene closed** during gameplay (reduces Inspector overhead)  
✅ **Tag your camera** as MainCamera for fallback  
✅ **Name VR rigs consistently** across scenes  
✅ **Test tracking** with debugger before building  

❌ **Don't use FindByName** with dynamic spawning  
❌ **Don't track destroyed** GameObjects  
❌ **Don't change player** during runtime (not supported)  

---

**Next**: [Auto-Scrolling Guide](AUTO_SCROLLING.md)  
**Back to**: [Documentation Hub](README.md)

