# Camera-Based Prioritization - Compilation Fix

## Issue
After implementing camera-based prioritization, a compilation error occurred:
```
Assets\_App\Ace of Ages\Terrain\TerrainPhysicsSystem.cs(404,8): error CS0101: 
The namespace '<global namespace>' already contains a definition for 'EntityWithPriority'
```

## Root Cause
Both `TerrainPhysicsSystem.cs` and `TerrainMeshGenerationSystem.cs` had structs named `EntityWithPriority`, causing a naming conflict in the global namespace.

## Solution
Renamed the struct in `TerrainMeshGenerationSystem.cs` to `MeshTileWithPriority` to differentiate it from the physics system's version.

### Changes Made

**File:** `Assets\_App\Ace of Ages\Terrain\TerrainMeshGenerationSystem.cs`

1. **Renamed struct:**
   - `EntityWithPriority` → `MeshTileWithPriority`
   
2. **Updated comparer:**
   - `TilePriorityComparer : IComparer<EntityWithPriority>` 
   - → `TilePriorityComparer : IComparer<MeshTileWithPriority>`

3. **Updated usage in OnUpdate():**
   - `new NativeList<EntityWithPriority>(...)`
   - → `new NativeList<MeshTileWithPriority>(...)`
   
   - `new EntityWithPriority { entity = entity, priority = priority }`
   - → `new MeshTileWithPriority { entity = entity, priority = priority }`

## Verification
✅ Compilation successful - no errors
⚠️ Only minor warnings remain (naming conventions, unused variables)

## Status
**RESOLVED** - The camera-based prioritization implementation is now fully functional and ready for testing.

Both systems now compile successfully:
- `TerrainColliderPreparationSystem.cs` ✅
- `TerrainMeshGenerationSystem.cs` ✅
- `TerrainPhysicsSystem.cs` ✅ (uses original `EntityWithPriority`)

## Next Steps
1. Test in Unity editor
2. Verify terrain tiles in front of camera generate first during origin shift
3. Monitor performance using profiler markers

