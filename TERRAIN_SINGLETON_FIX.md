# Terrain Singleton Initialization Fix

## Problem
When running the Start Scene, `TerrainDistanceTrackingSystem` was throwing an error:
```
InvalidOperationException: GetSingleton<TerrainTileConfig>() requires that exactly one entity exists that match this query, but there are none.
```

## Root Cause
- `TerrainDistanceTrackingSystem.OnUpdate()` was calling `SystemAPI.GetSingleton<TerrainTileConfig>()` on line 28
- The system was missing an `OnCreate()` method with `RequireForUpdate<TerrainTileConfig>()`
- This caused the system to run immediately when the scene loaded, before the terrain SubScene was fully loaded
- SubScene loading via `SubSceneLoader.Instance.LoadScene()` is asynchronous, so singleton entities are not immediately available

## Solution
Added `OnCreate()` method to `TerrainDistanceTrackingSystem`:

```csharp
protected override void OnCreate()
{
    RequireForUpdate<TerrainTileConfig>();
}
```

This prevents the system from running until the `TerrainTileConfig` singleton entity exists in the world.

## Pattern
All terrain systems that access singleton configuration should follow this pattern:

**Already Correct:**
- ✅ `TerrainRenderingSystem` - Has `RequireForUpdate<TerrainTileConfig>()`
- ✅ `TerrainMeshGenerationSystem` - Has `RequireForUpdate<TerrainTileConfig>()`
- ✅ `TileSpawningSystem` - Has `RequireForUpdate<TerrainTileConfig>()`
- ✅ `TerrainColliderPreparationSystem` - Has `RequireForUpdate<TerrainTileConfig>()`
- ✅ `TerrainPhysicsSystem` - Has `RequireForUpdate<TerrainTileConfig>()`
- ✅ `TileScrollPositionSystem` - Has `RequireForUpdate<TerrainTileConfig>()`
- ✅ `FormationMovementSystem` - Has `RequireForUpdate<TerrainTileConfig>()`
- ✅ `ScrollTerrainSystem` - Has `RequireForUpdate<ScrollConfig>()` (correct for its needs)

**Fixed:**
- ✅ `TerrainDistanceTrackingSystem` - Added `RequireForUpdate<TerrainTileConfig>()`

## Files Modified
- `Assets/_App/Ace of Ages/Terrain/TerrainDistanceTrackingSystem.cs`

## Testing
After this fix, the Start Scene should load without errors. The terrain systems will wait until the SubScene containing `TerrainConfigAuthoring` is fully loaded before attempting to run.

