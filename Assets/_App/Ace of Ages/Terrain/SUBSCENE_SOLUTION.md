# Runtime Player Search - Subscene Solution

## Problem Solved ✅

The `TerrainConfigAuthoring` component is in an **ECS subscene**, and the player GameObject (e.g., "XR Origin Hands (XR Rig)") is in the **main scene**. Unity doesn't allow cross-scene references during baking, which prevented dragging the player GameObject into the Inspector field.

## Solution: Runtime Search System

The terrain system now uses a **search-based initialization pattern** (similar to `TransformFollowerInitSystem`) that finds the player GameObject at runtime after all scenes have loaded.

---

## How It Works

### 1. At Baking Time (Editor)
```
TerrainConfigAuthoring (in subscene)
    ↓
Baker creates:
  - PlayerTrackingSearch (search parameters)
  - PlayerTransformReference (empty, will be filled at runtime)
```

### 2. At Runtime (Play Mode)
```
PlayerTrackingInitSystem runs
    ↓
Reads PlayerTrackingSearch component
    ↓
Searches for player GameObject using specified mode
    ↓
Populates PlayerTransformReference.playerTransform
    ↓
Terrain systems can now track the player!
```

---

## Setup Instructions

### Step 1: Configure Search Mode

In the subscene, select the GameObject with `TerrainConfigAuthoring`:

1. **Player Search Mode** dropdown - Choose how to find your player:
   - **AutoDetect** (Default) - Tries AutoHandPlayer, then Main Camera
   - **FindByName** - Searches for GameObject by exact name
   - **FindByTag** - Searches for GameObject by tag
   - **FindAutoHandPlayer** - Specifically looks for AutoHandPlayer component
   - **FindMainCamera** - Uses Camera.main

2. **If using "FindByName":**
   - Set **Player Name** to exact GameObject name
   - Example: `"XR Origin Hands (XR Rig)"`
   - Must match exactly (case-sensitive)

3. **If using "FindByTag":**
   - Set **Player Tag** to the tag assigned to your player
   - Example: `"Player"`
   - Make sure the tag exists in Tag Manager

### Step 2: Verify in Play Mode

Press Play and check the Console:

**Success:**
```
[PlayerTrackingInitSystem] ✅ Found player: XR Origin Hands (XR Rig) at position (0, 0, 0)
[PlayerTrackingInitSystem] ✅ PlayerTransformReference updated successfully
```

**Failure:**
```
[PlayerTrackingInitSystem] Could not find player GameObject!
Mode: FindByName, Search: 'XR Origin Hands (XR Rig)'
The terrain system will not work until a player is found.
```

If you see the failure message, the system will also list similar GameObjects it found (helpful for debugging name typos).

---

## Search Modes Explained

### AutoDetect (Recommended)
```
Tries in order:
1. AutoHandPlayer component
2. Main Camera (fallback)
```
**Best for:** VR projects using AutoHand
**Pros:** Works automatically, no configuration needed
**Cons:** Requires either AutoHandPlayer or a MainCamera tagged camera

### FindByName
```
Uses: GameObject.Find(playerName)
```
**Best for:** Specific GameObject name you know
**Pros:** Most precise, works with any GameObject
**Cons:** Must match exact name (case-sensitive)
**Example:** `"XR Origin Hands (XR Rig)"`

### FindByTag
```
Uses: GameObject.FindGameObjectWithTag(playerTag)
```
**Best for:** Player already has a tag assigned
**Pros:** More flexible than name (survives renames)
**Cons:** Requires tag to be set up in Tag Manager
**Example:** `"Player"`

### FindAutoHandPlayer
```
Uses: Object.FindFirstObjectByType<AutoHandPlayer>()
```
**Best for:** VR projects with AutoHand
**Pros:** Specifically targets AutoHandPlayer
**Cons:** Only works if AutoHandPlayer exists

### FindMainCamera
```
Uses: Camera.main
```
**Best for:** Simple scenes or desktop testing
**Pros:** Always available if camera is tagged
**Cons:** Tracks camera, not player rig root

---

## Troubleshooting

### "Could not find player GameObject"

**Check:**
1. Is your player GameObject **active** in the hierarchy?
2. Does the **name match exactly** (if using FindByName)?
3. Does the **tag exist and is assigned** (if using FindByTag)?
4. Is **AutoHandPlayer component** present (if using that mode)?
5. Is a camera **tagged as MainCamera** (if using that mode)?

**Debug:**
- The system logs similar GameObject names when search fails
- Look for lines like: `Found similar: 'XR Origin' (active: True)`
- Check if your GameObject name is slightly different

### "XR Origin Hands (XR Rig)" vs "XR Origin"

Common issue: GameObject name has extra spaces or characters.

**Solution:**
1. Select your player GameObject in Hierarchy
2. Copy the exact name from Inspector (top field)
3. Paste into **Player Name** field in TerrainConfigAuthoring

### Terrain Not Following Player After Init Success

**Check:**
1. Is `FloatingOriginGameObjectShifter` in the main scene?
2. Does it have the player's **root Transform** assigned?
3. Check Console for any errors from terrain systems

---

## Advanced: Runtime Change

To change the tracked player at runtime:

```csharp
using Unity.Entities;
using UnityEngine;

public void ChangeTrackedPlayer(Transform newPlayer)
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    
    // Find the singleton entity with player tracking
    var query = em.CreateEntityQuery(
        typeof(PlayerTransformReference),
        typeof(PlayerTrackingSearch)
    );
    
    if (query.CalculateEntityCount() > 0)
    {
        var entity = query.GetSingletonEntity();
        
        // Update the reference
        var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
        playerRef.playerTransform = newPlayer;
        
        Debug.Log($"Now tracking: {newPlayer.name}");
    }
    
    query.Dispose();
}
```

---

## Architecture Comparison

### Old Approach (Didn't Work)
```
TerrainConfigAuthoring (subscene)
    ↓
playerToTrack field ← Drag XR Origin (main scene)
    ❌ ERROR: "Cross scene references are not supported"
```

### New Approach (Works!)
```
TerrainConfigAuthoring (subscene)
    ↓
playerSearchMode = FindByName
playerName = "XR Origin Hands (XR Rig)"
    ↓
Baker creates PlayerTrackingSearch
    ↓
[Runtime] PlayerTrackingInitSystem finds GameObject
    ↓
✅ PlayerTransformReference populated
```

---

## Component Reference

### PlayerTrackingSearch (IComponentData)
```csharp
public struct PlayerTrackingSearch : IComponentData
{
    public Mode mode;              // How to search
    public FixedString128Bytes searchString; // Name or tag
    public bool initialized;       // True when found
}
```

### PlayerTransformReference (Managed)
```csharp
public class PlayerTransformReference : IComponentData
{
    public Transform playerTransform; // Found at runtime
}
```

---

## System Execution

```
InitializationSystemGroup (runs first frame)
    ↓
PlayerTrackingInitSystem
    ↓
Searches for player → Populates PlayerTransformReference
    ↓
[Later in frame]
    ↓
TileSpawningSystem & FloatingOriginSystem can now read player position
```

---

## Benefits of This Approach

✅ **Works across scenes** - No cross-scene reference errors
✅ **Flexible search** - Multiple ways to find player
✅ **Runtime resolution** - Player found after all scenes load
✅ **Debug-friendly** - Logs search attempts and results
✅ **Auto-fallback** - AutoDetect mode tries multiple methods
✅ **Editor visualization** - Gizmos still work in edit mode

---

## Example Configurations

### For VR with AutoHand
```
Player Search Mode: AutoDetect
(Leave other fields as default)
```

### For VR with Specific Rig Name
```
Player Search Mode: FindByName
Player Name: XR Origin Hands (XR Rig)
```

### For VR with Tagged Player
```
Player Search Mode: FindByTag
Player Tag: Player
```

### For Desktop Testing
```
Player Search Mode: FindMainCamera
```

---

## See Also

- **TransformFollowerInitSystem.cs** - Similar pattern used for DOTS entity tracking
- **GAMEOBJECT_TRACKING_GUIDE.md** - Original implementation guide
- **TerrainTrackingDebugger.cs** - Debug utility with status checks

---

**Problem:** Cross-scene references not supported ❌
**Solution:** Runtime search system ✅
**Status:** Ready to use!

