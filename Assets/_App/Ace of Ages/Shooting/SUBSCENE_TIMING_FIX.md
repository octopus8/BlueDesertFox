> **Archive Notice:** This is a historical patch note. The fix described here is already integrated into the codebase. See [Archive/README.md](Archive/README.md).

# PlayerShootingInput - SubScene Timing Fix

## Problem

`PlayerShootingInput` was failing to find the PlayerShip entity on Quest because:

1. PlayerShip GameObject is in a **SubScene** (gets baked to entity)
2. `PlayerShootingInput.Start()` ran **immediately** on scene load
3. SubScene baking **hadn't finished yet** → entity didn't exist
4. Component disabled itself thinking entity was missing
5. Even after SubScene finished, component stayed disabled forever

## Solution

Changed `Start()` to use a **coroutine that retries** finding the entity until SubScene baking completes.

### Implementation

**Before (Immediate Query)**:
```csharp
void Start()
{
    _entityManager = world.EntityManager;
    
    var query = _entityManager.CreateEntityQuery(typeof(BulletShooter), typeof(PlayerShip));
    var entities = query.ToEntityArray(Allocator.Temp);
    
    if (entities.Length == 0)
    {
        Debug.LogError("No entity found"); // ← Fails immediately
        enabled = false; // ← Disables forever!
        return;
    }
}
```

**After (Retry with Coroutine)**:
```csharp
void Start()
{
    StartCoroutine(InitializeWhenPlayerShipReady());
}

private IEnumerator InitializeWhenPlayerShipReady()
{
    Debug.Log("Waiting for PlayerShip entity...");
    
    int retryCount = 0;
    const int maxRetries = 300; // 5 seconds
    
    while (retryCount < maxRetries)
    {
        var query = _entityManager.CreateEntityQuery(...);
        var entities = query.ToEntityArray(Allocator.Temp);
        
        if (entities.Length > 0)
        {
            Debug.Log($"Found entity after {retryCount} frames!");
            // Initialize and return
            yield break;
        }
        
        retryCount++;
        yield return null; // Wait one frame, try again
    }
    
    Debug.LogError("Failed to find entity after 300 frames");
}
```

## Expected Console Output (Quest)

### Success Case:
```
[PlayerShootingInput] Waiting for PlayerShip entity to be created...
[PlayerShootingInput] Found PlayerShip entity after 12 frames!
[PlayerShootingInput] Fire action initialized successfully
[PlayerShootingInput] Initialized successfully
```

Then when trigger pressed:
```
[PlayerShootingInput] Fire button pressed - triggered shoot
[BulletShooterSystem] Fired bullet at position (x,y,z)...
```

### Failure Case:
```
[PlayerShootingInput] Waiting for PlayerShip entity to be created...
[PlayerShootingInput] Failed to find PlayerShip entity after 300 frames
    Make sure PlayerShipAuthoring and BulletShooterAuthoring are in SubScene
```

## Why This Happens

**SubScene Baking Timeline**:
```
Frame 0:  Scene loads, SubScene starts baking
Frame 1:  PlayerShootingInput.Start() runs
Frame 2:  SubScene still baking...
Frame 3:  SubScene still baking...
...
Frame 10: SubScene finishes baking
Frame 11: PlayerShip entity NOW exists
```

Without retry, the query runs at **Frame 1** (entity doesn't exist yet), component disables, and never checks again.

With retry, the query runs **every frame** until entity is found at **Frame 11**.

## Technical Details

### Retry Parameters
- **Max retries**: 300 frames (5 seconds at 60fps)
- **Retry interval**: Every frame (`yield return null`)
- **Timeout handling**: Disables component and logs error after max retries

### Performance Impact
- **Minimal**: Only runs during initialization (first few frames)
- **No ongoing cost**: Once entity is found, coroutine stops
- **Query cleanup**: Properly disposes temp allocations each retry

### Memory Safety
- Uses `Allocator.Temp` for query results (frame-scoped)
- Disposes arrays immediately after checking
- Disposes query handle after use

## Alternative Solutions (Not Used)

### 1. Move PlayerShip to Main Scene
- **Pro**: Entity exists immediately (no baking delay)
- **Con**: Loses SubScene benefits (baking, streaming, etc.)
- **Verdict**: Not ideal for DOTS workflow

### 2. Use EntityManager.CompleteAllTrackedJobs()
- **Pro**: Forces SubScene to finish baking
- **Con**: Blocks main thread, causes frame stutter
- **Verdict**: Bad for VR (causes hitches)

### 3. Wait Fixed Delay
```csharp
yield return new WaitForSeconds(1f);
// Then query once
```
- **Pro**: Simple
- **Con**: Unreliable (baking time varies), wastes time if fast
- **Verdict**: Retry loop is more robust

## Files Changed

- `PlayerShootingInput.cs` - Added `using System.Collections;`, changed `Start()` to coroutine-based retry

## Testing

### In Editor
1. Enter Play mode
2. Check Console for "Found PlayerShip entity after X frames"
3. Press A key → bullets should fire

### On Quest
1. Build and deploy
2. Connect ADB logcat
3. Check for "Found PlayerShip entity after X frames"
4. Press right trigger → bullets should fire

### Verify Logs
If you see **"Failed to find PlayerShip entity after 300 frames"**:
- Check SubScene has PlayerShipAuthoring GameObject
- Check GameObject has BulletShooterAuthoring component
- Check SubScene is opened/closed (forcing re-bake)

---

**Fix Applied**: May 8, 2026  
**Status**: ✅ Complete  
**Breaking Changes**: None  
**Performance**: Negligible (initialization only)

