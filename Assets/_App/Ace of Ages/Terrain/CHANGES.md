# Terrain System Changes - Floating Origin Removal

## Quick Reference

### What Was Removed
- ❌ Automatic world origin shifting when player moves >2000m from origin
- ❌ WorldOriginOffset singleton component (double3 precision tracking)
- ❌ FloatingOriginConfig singleton component (enabled flag, threshold)
- ❌ FloatingOriginEnabled tag component (marked entities for shifting)
- ❌ FloatingOriginSystem (monitored distance and triggered shifts)
- ❌ FloatingOriginEvents (event notifications for GameObject sync)
- ❌ FloatingOriginGameObjectShifter (MonoBehaviour for GameObject sync)
- ❌ FloatingOriginEnabledAuthoring (baking component)

### What Still Works
- ✅ Tile spawning around player position
- ✅ Tile despawning when player moves away
- ✅ Procedural terrain mesh generation (Perlin noise)
- ✅ Terrain rendering with materials
- ✅ Physics colliders with LOD system
- ✅ Collider caching and LRU eviction
- ✅ Frame budgeting for smooth performance
- ✅ Player tracking via PlayerTransformReference

### What Changed
- **Terrain generation**: Now uses absolute world coordinates (no offset correction)
- **Distance limitation**: Player should stay within ~1000-2000m of origin for best precision
- **Simplified code**: Fewer systems, components, and edge cases
- **No GameObject sync**: No events to subscribe to for world shifts

### Inspector Changes (TerrainConfigAuthoring)
**Before:**
```
[Tile Settings]
[Floating Origin]        ← REMOVED
  ☑ Floating Origin Enabled
  Shift Threshold: 2000
[Procedural Noise Settings]
[Physics Optimization]
```

**After:**
```
[Tile Settings]
[Procedural Noise Settings]
[Physics Optimization]
```

### Code Migration Examples

#### Spawning Entities (TileSpawningSystem)
**Before:**
```csharp
state.RequireForUpdate<WorldOriginOffset>();
ecb.AddComponent(tileEntity, new FloatingOriginEnabled());
```

**After:**
```csharp
// No WorldOriginOffset requirement
// No FloatingOriginEnabled tag
```

#### Mesh Generation (TerrainMeshGenerationSystem)
**Before:**
```csharp
var worldOffset = SystemAPI.GetSingleton<WorldOriginOffset>();
double3 tileWorldPos = new double3(...) + worldOffset.accumulatedOffset;
```

**After:**
```csharp
// No worldOffset retrieval
double3 tileWorldPos = new double3(...); // Direct grid coordinates
```

#### Physics (TerrainPhysicsSystem)
**Before:**
```csharp
FloatingOriginEvents.OnNonPlayerOriginShifted += OnOriginShifted;

private void OnOriginShifted(float3 offset)
{
    // Clear collider queue and re-prioritize
}
```

**After:**
```csharp
// No event subscription
// No OnOriginShifted method
// Collider queue never cleared due to origin shifts
```

## Testing After Changes

1. **Basic tile spawning**: Walk around, tiles should spawn/despawn
2. **Mesh rendering**: Terrain should be visible with correct material
3. **Physics collision**: Walk on terrain, should not fall through
4. **Performance**: Check frame time remains smooth (<16ms for 60 FPS)
5. **Console errors**: Should be clean (no missing component errors)

## Known Limitations

⚠️ **Float precision degrades beyond ~1000-2000m from world origin**
- Jitter in rendering and physics may occur
- Keep gameplay near origin for best results
- Consider teleporting player back to origin if they travel too far (manual implementation)

## Rollback

If you need to restore floating origin:
```bash
git revert <commit-hash>
```

Or manually restore:
1. Assets\_App\Ace of Ages\Terrain\FloatingOriginSystem.cs
2. Assets\_App\Ace of Ages\Terrain\FloatingOriginComponents.cs
3. Assets\_App\Ace of Ages\Terrain\FloatingOriginEvents.cs
4. Assets\_App\Ace of Ages\Terrain\FloatingOriginEnabledAuthoring.cs
5. Assets\_App\Ace of Ages\Terrain\FloatingOriginGameObjectShifter.cs

Plus revert changes to 7 modified files.

