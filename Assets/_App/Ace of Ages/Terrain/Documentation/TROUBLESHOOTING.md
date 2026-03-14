# Infinite Terrain System - Troubleshooting Guide

**Last Updated:** March 14, 2026

## Table of Contents
1. [Terrain Not Appearing](#terrain-not-appearing)
2. [Performance Issues](#performance-issues)
3. [Physics Problems](#physics-problems)
4. [Rendering Artifacts](#rendering-artifacts)
5. [System Not Running](#system-not-running)
6. [Floating Origin Issues](#floating-origin-issues)

---

## Terrain Not Appearing

### Symptom: Black/Empty Scene, No Terrain Visible

#### Check 1: Verify Systems Are Running

**Steps:**
1. Open Window → DOTS → Systems
2. Expand `SimulationSystemGroup`
3. Look for terrain systems with ✓ checkmark:
   - TileSpawningSystem
   - TerrainMeshGenerationSystem
   - TerrainPhysicsSystem
4. Expand `PresentationSystemGroup`
5. Look for:
   - TerrainRenderingSystem

**If systems are grayed out:**
- Missing required components (see below)
- Check Console for errors

**Fix:**
```
Required Components:
├─ PlayerTag (on player entity)
├─ TerrainTileConfig (singleton)
├─ WorldOriginOffset (singleton)
└─ FloatingOriginConfig (singleton)
```

All created by `TerrainConfigAuthoring` if properly set up.

---

#### Check 2: Verify Player Tag Exists

**Steps:**
1. Window → DOTS → Entities Hierarchy
2. Search for "PlayerTag"
3. Should find exactly one entity

**If not found:**
1. Select player GameObject in Hierarchy
2. Verify `PlayerTagAuthoring` component is attached
3. If in SubScene, ensure SubScene is closed (baked)
4. If not in SubScene, ensure GameObject has a Transform

**Fix:**
```csharp
// Add to player GameObject:
- PlayerTagAuthoring component
- FloatingOriginEnabledAuthoring component
```

---

#### Check 3: Console Error Analysis

**Common Errors:**

**"TerrainMaterial not found in Resources"**
```
Fix: Tools → Terrain → Create Terrain Material
Or: Wait for TerrainMaterialCreator to run on next Unity startup
```

**"Failed to find 'Universal Render Pipeline/Lit' shader!"**
```
Problem: URP not configured
Fix:
1. Edit → Project Settings → Graphics
2. Set "Scriptable Render Pipeline Settings" to URP asset
3. If no URP asset: Window → Package Manager → Install URP
4. Restart Unity
```

**"EntitiesGraphicsSystem not found!"**
```
Problem: Entities Graphics package not installed
Fix:
1. Window → Package Manager
2. Search "Entities Graphics"
3. Install if missing
4. Restart Unity
```

**"PlayerTag not found"**
```
Fix: Add PlayerTagAuthoring to player GameObject
```

---

#### Check 4: Camera Position

**Problem:** Camera might be far from terrain spawn location.

**Steps:**
1. Enter Play Mode
2. In Scene View, navigate to (0, 0, 0)
3. Look around for terrain

**If terrain is there but camera isn't:**
- Player spawns at wrong location
- Move player GameObject to near origin

**If terrain isn't there:**
- Check Console for tile spawning messages
- Verify systems are running (Check 1)

---

#### Check 5: Material/Shader Issues

**Steps:**
1. In Project, find: `Assets/Resources/TerrainMaterial.mat`
2. Select it
3. Check Inspector:
   - Shader should be: "Universal Render Pipeline/Lit" (or similar URP shader)
   - Should NOT be: "Standard" or error message

**If shader is wrong:**
```
Fix:
1. Select material
2. Shader dropdown → Universal Render Pipeline → Lit
3. Re-enter Play Mode
```

**If material doesn't exist:**
```
Fix: Tools → Terrain → Create Terrain Material
```

---

#### Check 6: SubScene Baking

**Problem:** SubScene not baked properly.

**Steps:**
1. In Hierarchy, find SubScene
2. Status should be: "Closed"
3. If "Open": Close it and wait for baking

**If baking fails:**
```
Fix:
1. Right-click SubScene → Rebuild
2. Wait for compilation
3. Check Console for baking errors
4. Fix any script errors
5. Try again
```

---

#### Check 7: Layer/Culling Mask

**Problem:** Camera can't see terrain layer.

**Steps:**
1. Select Main Camera
2. Check "Culling Mask" in Inspector
3. Ensure "Default" layer is checked (terrain uses layer 0)

**Fix:**
```
Camera Culling Mask: ☑ Default (and other layers)
```

---

### Symptom: Terrain Appears Then Disappears

**Problem:** Rendering bounds culling issue.

**Check Console for:**
```
[TerrainDebug] WorldRenderBounds: Center=NaN, Extents=NaN
```

**Fix:**
```csharp
// In TerrainRenderingSystem, ensure:
mesh.RecalculateBounds();  // This line must be present

// Verify mesh has valid bounds:
Debug.Log($"Mesh bounds: {mesh.bounds}");
// Should show: Center=(50,10,50), Extents=(50,10,50) or similar
```

**If bounds are still invalid:**
- Check vertex positions aren't NaN
- Ensure vertices are properly populated
- Verify mesh has >0 vertices

---

## Performance Issues

### Symptom: Low Frame Rate (<30 FPS)

#### Diagnosis: Profile First

**Steps:**
1. Window → Analysis → Profiler
2. Enter Play Mode
3. Look at CPU Usage
4. Find these markers:
   - TileSpawningSystem.OnUpdate
   - TerrainMeshGenerationSystem.OnUpdate
   - TerrainPhysicsSystem.OnUpdate
   - TerrainRenderingSystem.OnUpdate

**Identify bottleneck:**

| System | Expected | If Higher | Fix |
|--------|----------|-----------|-----|
| TileSpawning | <0.5ms | >2ms | Reduce view distance |
| MeshGeneration | <1ms | >5ms | Reduce verticesPerSide or octaves |
| Physics | <2ms | >10ms | Reduce verticesPerSide or disable physics |
| Rendering | <1ms | >5ms | Reduce active tile count |

---

#### Fix 1: Reduce Vertices Per Side

**Current: 64 → Change to: 32**

**Effect:**
- 4x fewer vertices (4096 → 1024)
- 4x fewer triangles
- 4x faster mesh generation
- Slightly less detailed terrain

**Recommendation:**
- VR: 16-24
- Desktop: 32-48
- Screenshots: 64-128

---

#### Fix 2: Reduce View Distance

**Current: 500m → Change to: 300m**

**Effect:**
- Fewer active tiles (~78 → ~28)
- Less memory usage
- Fewer tiles generated per second
- Smaller visible area

**Formula:**
```
Active tiles ≈ π * (viewDistance / tileSize)²
```

---

#### Fix 3: Reduce Noise Octaves

**Current: 6 → Change to: 3**

**Effect:**
- 2x faster noise sampling
- Less detailed terrain (smoother)
- Same overall shape

**Impact per octave:**
- Each octave: ~25% slower generation
- Visual quality: Diminishing returns after 4 octaves

---

#### Fix 4: Increase Tile Size

**Current: 50m → Change to: 100m**

**Effect:**
- 4x fewer tiles for same view distance
- 4x faster overall generation
- Less granular LOD (all-or-nothing per tile)

**Trade-off:**
- Larger tiles = more wasted vertices at edges
- Smaller tiles = more granular streaming = better performance

**Recommendation:** 100m is optimal for most cases

---

#### Fix 5: Disable Physics (If Not Needed)

**Comment out TerrainPhysicsSystem:**

```csharp
// In TerrainPhysicsSystem.cs:
protected override void OnUpdate()
{
    return;  // Disable physics generation
    
    // ... rest of code ...
}
```

**Effect:**
- Saves 1-2ms per tile
- Player will fall through terrain
- Only do this if terrain is visual only

---

### Symptom: Frame Spikes When Moving

**Cause:** Many tiles generating at once.

**Solution 1: Limit Tiles Per Frame**

Add to `TerrainMeshGenerationSystem`:

```csharp
public void OnUpdate(ref SystemState state)
{
    const int maxTilesPerFrame = 3;  // Process at most 3 tiles
    int processedCount = 0;
    
    foreach (var entity in entities)
    {
        if (processedCount >= maxTilesPerFrame)
            break;  // Stop processing this frame
        
        ref var tile = ref SystemAPI.GetComponentRW<TerrainTile>(entity).ValueRW;
        if (!tile.meshGenerated || tile.needsRegeneration)
        {
            GenerateTileMesh(...);
            processedCount++;
        }
    }
}
```

**Effect:** Spreads work across multiple frames, smoother but slower loading.

**Solution 2: Increase View Distance Gradually**

Start with small view distance, increase over time:

```csharp
// In TerrainConfigAuthoring or custom system:
float targetViewDistance = 500f;
float currentViewDistance = 100f;
float growthRate = 50f;  // meters per second

void Update()
{
    if (currentViewDistance < targetViewDistance)
    {
        currentViewDistance += growthRate * Time.deltaTime;
        // Update config singleton
    }
}
```

---

## Physics Problems

### Symptom: Player Falls Through Terrain

#### Check 1: Physics System Running?

**Steps:**
1. Window → DOTS → Systems
2. Find `TerrainPhysicsSystem`
3. Should be ✓ enabled

**If disabled:**
- Check required components (TerrainTileConfig)
- Check for errors in Console

---

#### Check 2: Colliders Being Created?

**Add debug logging:**

```csharp
// In TerrainPhysicsSystem.CreatePhysicsCollider():
Debug.Log($"Creating collider for entity {entity.Index}");

// After collider creation:
Debug.Log($"Collider created: {collider.Value.IsValid}");
```

**Expected:** Log message for each tile.

**If no messages:**
- System not running (see Check 1)
- Query not matching entities (missing components)

---

#### Check 3: Player Has Physics Components?

**Required on player:**
- Rigidbody (or PhysicsShape + PhysicsBody in DOTS)
- Collider (CapsuleCollider for character)

**Check:**
1. Select player GameObject
2. Verify components in Inspector

**If missing:**
- Add CharacterController or Rigidbody + CapsuleCollider
- Ensure gravity is enabled

---

#### Check 4: Collision Layers

**Check collision matrix:**
1. Edit → Project Settings → Physics
2. Layer Collision Matrix
3. Ensure player layer collides with Default layer (terrain uses layer 0)

**Fix:**
- Check box where player layer intersects Default layer

---

#### Check 5: Physics Not Updating

**Problem:** Tiles have colliders but player still falls through.

**Possible causes:**
- Physics timestep too low (check Time.fixedDeltaTime)
- Player moving too fast (tunneling through thin colliders)
- Collider data corrupt

**Fix:**
```
Edit → Project Settings → Time:
├─ Fixed Timestep: 0.02 (50 Hz) or 0.0111 (90 Hz)
└─ Maximum Allowed Timestep: 0.1

Edit → Project Settings → Physics:
├─ Default Solver Iterations: 6
└─ Default Solver Velocity Iterations: 1
```

---

## Rendering Artifacts

### Symptom: Seams Between Tiles

**Types of Seams:**

#### Geometry Seams (Vertex Misalignment)
**Visual:** Gaps or cracks between tiles, visible from any angle

**Cause:** Vertex positions don't match at tile boundaries.

**Why This Happens:**
- Floating-point rounding differences
- Different noise sampling between tiles

**Current System:** Should NOT have geometry seams (noise is deterministic and continuous).

**If you see geometry seams:**

**Check 1: Edge Vertices Match**
```csharp
// Debug: Compare edge vertices of adjacent tiles
var tile1Verts = GetBuffer<VertexElement>(tile1);
var tile2Verts = GetBuffer<VertexElement>(tile2);

// Tile 1 right edge should match Tile 2 left edge
for (int z = 0; z < verticesPerSide; z++)
{
    int tile1Index = z * verticesPerSide + (verticesPerSide - 1);  // Right edge
    int tile2Index = z * verticesPerSide + 0;                      // Left edge
    
    float3 v1 = tile1Verts[tile1Index].value + tile1Position;  // World space
    float3 v2 = tile2Verts[tile2Index].value + tile2Position;  // World space
    
    if (math.distance(v1, v2) > 0.01f)
    {
        Debug.LogError($"Geometry seam at z={z}: {math.distance(v1, v2)}m apart");
    }
}
```

**Fix:** Ensure `stepSize` calculation is identical for all tiles:
```csharp
float stepSize = config.tileSize / (verticesPerSide - 1);
// NOT: config.tileSize / verticesPerSide
```

#### Lighting Seams (Normal Discontinuity)
**Visual:** Hard lighting line at tile edge, most visible with grazing light angles

**Cause:** Edge vertex normals don't match between adjacent tiles.

**Why This Could Happen:**
- Old method: `CalculateNormal()` only looked at vertices within tile array, couldn't access neighbor tiles
- Edge vertices calculated normals from incomplete data

**Current System (Fixed March 2026):** ✅ Should NOT have lighting seams.

The system now uses `CalculateNormalFromHeightfield()` which:
- Samples heights directly from noise function at neighboring world positions
- Can sample beyond tile boundaries (e.g., at z=-stepSize for z=0 edge vertices)
- Produces identical normals for shared edge vertices between tiles
- See `EDGE_NORMAL_FIX.md` for details

**If you see lighting seams:**

**Check 1: Verify Normal Calculation Method**
```csharp
// In TerrainMeshGenerationSystem.cs, normal loop should use:
normals[index] = CalculateNormalFromHeightfield(worldX, worldZ, stepSize, config);

// NOT the old method:
// normals[index] = CalculateNormal(x, z, vertices, verticesPerSide); // ❌ DEPRECATED
```

**Check 2: Compare Edge Normals**
```csharp
// Edge normals should be nearly identical (within float precision)
var normal1 = tile1Normals[rightEdgeIndex].value;
var normal2 = tile2Normals[leftEdgeIndex].value;
float difference = math.distance(normal1, normal2);

if (difference > 0.001f)
{
    Debug.LogError($"Normal seam detected: difference = {difference}");
}
```

**Fix:** Ensure both tiles use the same `config` values (especially noise parameters).

---

### Symptom: Terrain Flickering or Z-Fighting

**Cause:** Multiple overlapping meshes.

**Check:**
1. Window → DOTS → Entities Hierarchy
2. Count entities with TerrainTile at same grid coordinate
3. Should be exactly 1 per coordinate

**If multiple:**
- TileSpawningSystem creating duplicates
- _activeTiles HashMap not preventing respawn

**Fix:**
```csharp
// In TileSpawningSystem:
if (!_activeTiles.ContainsKey(gridCoord))  // This check must be present
{
    SpawnTile(gridCoord);
}
```

---

### Symptom: Terrain is Pink/Magenta

**Cause:** Shader compilation error or missing shader.

**Check:**
1. Select terrain material in Project
2. Check Inspector for red error text
3. Check Console for shader errors

**Fix:**
```
Option 1: Reassign shader
├─ Material Inspector
├─ Shader dropdown
└─ Universal Render Pipeline → Lit

Option 2: Recreate material
└─ Tools → Terrain → Create Terrain Material
```

---

### Symptom: Terrain is Black (No Lighting)

**Cause:** Normals are wrong or lighting is missing.

**Check 1: Normals**
```csharp
// In Scene View, enable: Shaded → Display Normals
// Should see blue lines pointing up from terrain
```

**If normals point down or sideways:**
- Check triangle winding order (should be counter-clockwise)
- Check normal calculation logic

**Fix:**
```csharp
// In mesh index generation, ensure correct order:
indexBuffer.Add(baseIndex);
indexBuffer.Add(baseIndex + verticesPerSide);  // Top-left
indexBuffer.Add(baseIndex + 1);                // Bottom-right
// This creates counter-clockwise winding
```

**Check 2: Scene Lighting**
1. Check directional light exists in scene
2. Check light is enabled
3. Check light intensity >0

**Fix:** Add directional light:
```
GameObject → Light → Directional Light
```

---

### Symptom: Terrain Appears in Wrong Location

**Cause:** World origin offset or transform miscalculation.

**Debug:**
```csharp
// In TileSpawningSystem, add logging:
float3 tilePosition = new float3(
    gridCoord.x * config.tileSize,
    0,
    gridCoord.y * config.tileSize
);
Debug.Log($"Creating tile at grid {gridCoord}, world position {tilePosition}");
```

**Check:**
- Grid (0, 0) should be at world (0, 0, 0)
- Grid (1, 0) should be at world (tileSize, 0, 0)

**If positions are wrong:**
- Verify `config.tileSize` is set correctly
- Check for extra transforms on tile entities

---

## System Not Running

### Symptom: No Console Messages, No Tiles Spawning

#### Check 1: System Requirements Not Met

**Each system has requirements:**

**TileSpawningSystem:**
```csharp
state.RequireForUpdate<PlayerTag>();
state.RequireForUpdate<TerrainTileConfig>();
state.RequireForUpdate<WorldOriginOffset>();
```

**If any are missing, system won't run.**

**Verify in Entities Hierarchy:**
1. Window → DOTS → Entities Hierarchy
2. Search for "TerrainTileConfig" - should find 1 entity
3. Search for "WorldOriginOffset" - should find 1 entity
4. Search for "PlayerTag" - should find 1 entity

**If missing:**
- Ensure TerrainConfigAuthoring is in scene
- Ensure SubScene is closed (baked)
- Check Console for baking errors

---

#### Check 2: System Order Issues

**Problem:** System updates in wrong order or not at all.

**Check:**
```
Window → DOTS → Systems
└─ Show: All Systems
    ├─ SimulationSystemGroup
    │   ├─ TileSpawningSystem ✓
    │   ├─ TerrainMeshGenerationSystem ✓
    │   └─ TerrainPhysicsSystem ✓
    ├─ TransformSystemGroup
    │   └─ FloatingOriginSystem ✓
    └─ PresentationSystemGroup
        └─ TerrainRenderingSystem ✓
```

**If in wrong group:**
- Check `[UpdateInGroup]` attribute
- Check `[UpdateBefore]`/`[UpdateAfter]` attributes

---

#### Check 3: Script Compilation Errors

**Steps:**
1. Check Console for red errors
2. Fix all script errors
3. Wait for recompilation
4. Try again

**Common errors:**
- Missing `using` statements
- Typos in component names
- Assembly definition issues

---

## Floating Origin Issues

### Symptom: Terrain "Jumps" or Changes After Walking Far

**Cause:** Accumulated offset not being used in noise sampling.

**Check:**
```csharp
// In TerrainMeshGenerationSystem.GenerateTileMesh():
double3 tileWorldPos = new double3(
    tile.gridCoordinate.x * config.tileSize,
    0,
    tile.gridCoordinate.y * config.tileSize
) + worldOffset.accumulatedOffset;  // ← This line MUST be present
```

**If missing:**
- Terrain will regenerate differently after origin shift
- Tiles will "pop" to different heights

**Fix:** Ensure accumulated offset is added to all noise sampling coordinates.

---

### Symptom: Origin Shifts Too Often/Not Often Enough

**Tune shift threshold:**

**Too often (every few seconds):**
```
Increase shiftThreshold:
Current: 500
Recommended: 2000
```

**Not often enough (player at 10,000 units):**
```
Decrease shiftThreshold:
Current: 5000
Recommended: 1000-2000
```

**Optimal value:** 
- Large enough: Don't shift too frequently (disruptive)
- Small enough: Keep precision errors invisible (<1000-2000m)

---

### Symptom: Player Position "Snaps" During Shift

**Expected behavior:** Player should shift smoothly (imperceptibly).

**If visible snap:**

**Check 1: Player has FloatingOriginEnabled tag?**
```
Player Entity must have:
├─ PlayerTag
└─ FloatingOriginEnabled  ← Important!
```

**If missing:** Player won't shift with world, will appear to "teleport" to origin.

**Check 2: Camera parented to player?**
- Camera should be child of player
- Will inherit player's position shift
- Should not have FloatingOriginEnabled itself

---

### Symptom: Objects Don't Shift with World

**Cause:** Missing FloatingOriginEnabled tag.

**Fix:**
1. Add `FloatingOriginEnabledAuthoring` to GameObject
2. Rebake SubScene (if in SubScene)
3. Object will now shift with terrain

**Common objects that need tag:**
- Player
- Terrain tiles (auto-added)
- Trees/vegetation
- Buildings
- NPCs
- Any world-space object

**Objects that should NOT have tag:**
- UI elements (screen-space)
- Effects parented to camera
- Skybox

---

## Advanced Debugging

### Enable Detailed Logging

**In TileSpawningSystem.cs:**
```csharp
// Uncomment/add more Debug.Log statements:
foreach (var gridCoord in tilesToSpawn)
{
    Debug.Log($"[TileSpawn] Creating tile at grid {gridCoord}");
    Entity tileEntity = ecb.CreateEntity();
    // ...
}
```

**In TerrainMeshGenerationSystem.cs:**
```csharp
Debug.Log($"[MeshGen] Tile {tile.gridCoordinate}: {vertices.Length} verts, {indices.Length} indices");
```

---

### Use Gizmos for Visualization

**Enable in TerrainConfigAuthoring:**
```csharp
private void OnDrawGizmosSelected()
{
    // Draws view distance sphere and current tile
    // Select TerrainConfig GameObject to see
}
```

**Add to TerrainTileGizmoVisualizer:**
```csharp
// Already in project - shows tile bounds in Scene View
// Enable by adding to GameObject
```

---

### Check Entity Structure

**Steps:**
1. Enter Play Mode
2. Window → DOTS → Entities Hierarchy
3. Find an entity with TerrainTile
4. Click to inspect

**Should have:**
- ✓ TerrainTile
- ✓ LocalTransform
- ✓ LocalToWorld
- ✓ FloatingOriginEnabled
- ✓ VertexElement (buffer)
- ✓ NormalElement (buffer)
- ✓ UVElement (buffer)
- ✓ IndexElement (buffer)
- ✓ MeshReference (after 1-2 frames)
- ✓ MaterialMeshInfo (after rendering setup)
- ✓ RenderBounds (after rendering setup)
- ✓ PhysicsCollider (after physics setup)

**If components missing:**
- Check system execution order
- Check Console for errors during system updates

---

### Test with Simple Cube

**Use TestECSRenderingSystem:**

Already in project: `Assets/_App/Ace of Ages/Terrain/TestECSRenderingSystem.cs`

**Creates a red test cube at (10, 2, 10)**

**To enable:**
1. Uncomment system (if commented)
2. Enter Play Mode
3. Navigate to (10, 2, 10) in Scene View

**If cube visible:**
- Entities Graphics working ✓
- Problem is terrain-specific

**If cube not visible:**
- Fundamental Entities Graphics issue
- Check URP configuration
- Check Entities Graphics package installed

---

## Performance Profiling

### What to Measure

**Frame Time Budget (60 FPS = 16.67ms):**

| Category | Budget | Critical |
|----------|--------|----------|
| Terrain Systems | <5ms | ⚠️ If >10ms |
| Physics Simulation | <3ms | ⚠️ If >5ms |
| Rendering | <5ms | ⚠️ If >8ms |
| Other (Scripts, etc.) | <3ms | |

**Measure in Unity Profiler:**
1. Window → Analysis → Profiler
2. Click Play
3. Watch CPU Usage graph
4. Identify spikes

---

### Optimization Checklist

**If frame rate low:**
- [ ] Reduce verticesPerSide (32 → 16)
- [ ] Reduce viewDistance (500 → 300)
- [ ] Reduce noiseOctaves (4 → 2)
- [ ] Increase tileSize (100 → 150)
- [ ] Limit tiles processed per frame (add counter)
- [ ] Disable physics if not needed
- [ ] Use simpler material (Unlit shader)
- [ ] Reduce shadow quality (Project Settings → Quality)

**If memory high:**
- [ ] Reduce viewDistance (fewer active tiles)
- [ ] Reduce verticesPerSide (less data per tile)
- [ ] Implement tile mesh sharing (if identical)

---

## Known Issues & Limitations

### Issue 1: Can't Write Buffers in Parallel Jobs

**Problem:** DynamicBuffer writes not supported in IJobEntity.

**Current Solution:** Generate meshes sequentially on main thread.

**Workaround:** Use NativeArray for intermediate storage:
```csharp
[BurstCompile]
struct GenerateMeshJob : IJob
{
    public NativeArray<float3> vertices;
    // Generate into NativeArray
}

// Then copy to buffer on main thread
```

**Impact:** Mesh generation is parallelizable but buffer copying is sequential.

---

### Issue 2: Managed Components Break Burst

**Problem:** `MeshReference` is managed (class), can't use in Burst jobs.

**Current Solution:** `TerrainRenderingSystem` not Burst-compiled.

**Alternative:** Use NativeArray or BlobAsset for mesh data:
- More complex code
- Better performance
- Only worth it if rendering is bottleneck

---

### Issue 3: First Frame Stutter

**Problem:** Many tiles spawn on first frame = long pause.

**Workaround:** Fade in camera:
```csharp
1. Start with black screen
2. Generate initial tiles (1-2 frames)
3. Fade in camera
4. Player doesn't notice generation time
```

**Already implemented in project:**
```csharp
// In SceneStartup.cs:
await CameraFader.Instance.fadeCameraOut(0f);
// ... load terrain ...
await CameraFader.Instance.fadeCameraIn(1f);
```

---

### Issue 4: No LOD System

**Problem:** Distant tiles use same detail as near tiles.

**Impact:** Wastes GPU processing distant detail that's not visible.

**Workaround:** Reduce view distance so only near tiles are visible.

**Future Feature:** Implement LOD levels based on distance:
- 0-100m: 64 verts/side
- 100-300m: 32 verts/side
- 300-500m: 16 verts/side

---

## Diagnostic Commands

### Check System Status

```csharp
// In Unity Console, run:
Window → DOTS → Systems
Filter: "Terrain"

// Should show:
TileSpawningSystem [Update: Every Frame] ✓
TerrainMeshGenerationSystem [Update: Every Frame] ✓
TerrainPhysicsSystem [Update: Every Frame] ✓
TerrainRenderingSystem [Update: Every Frame] ✓
```

---

### Check Entity Count

```csharp
// In Unity Console, run:
Window → DOTS → Entities Hierarchy
Filter: "TerrainTile"

// Count entities shown
// Should match: π * (viewDistance/tileSize)²
// Example: 300m view, 100m tiles = ~28 tiles
```

---

### Check Memory Usage

```csharp
// In Unity Profiler:
Profiler → Memory → Take Sample

// Look for:
"Mesh.vertices" - terrain vertex data
"Physics.Colliders" - terrain colliders
"Entities" - ECS entity storage

// Expected (32x32 verts, 28 tiles):
Mesh data: ~1.5 MB
Colliders: ~800 KB
Entities: ~100 KB
Total: ~2.5 MB
```

---

## Recovery Procedures

### Full Reset

If completely stuck:

1. **Delete SubScene data:**
   ```
   Delete folder: Assets/SceneDependencyCache/
   ```

2. **Reimport scripts:**
   ```
   Assets → Reimport All
   ```

3. **Clear Library:**
   ```
   Close Unity
   Delete: Library/
   Reopen Unity (will rebuild)
   ```

4. **Recreate material:**
   ```
   Tools → Terrain → Create Terrain Material
   ```

5. **Verify settings:**
   - TerrainConfigAuthoring exists
   - Player has tags
   - SubScene is closed

6. **Test again**

---

### Minimal Working Setup

**To test if systems work at all:**

1. **Create new scene**
2. **Add:**
   - GameObject "TerrainConfig" with TerrainConfigAuthoring
   - GameObject "Player" (empty) with PlayerTagAuthoring + FloatingOriginEnabledAuthoring
   - Add Transform to Player
   - Position Player at (0, 1, 0)
3. **Put both in a SubScene**
4. **Close SubScene**
5. **Enter Play Mode**

**Expected:**
- Console: "[TileSpawning] Spawning X new tiles"
- Console: "[TerrainMeshGen] Generating mesh"
- Console: "[TerrainRendering] Processing X tiles"

**If this works:** Problem is in your main scene setup.  
**If this doesn't work:** Problem is system-level (packages, Unity version, etc.)

---

## Getting Help

### Information to Gather

When reporting issues, include:

1. **Unity version:** Help → About Unity
2. **Console logs:** Full Console output (right-click → Copy All)
3. **System status:** Window → DOTS → Systems (screenshot)
4. **Entity inspector:** Window → DOTS → Entities Hierarchy (screenshot of tile entity)
5. **Configuration:** TerrainConfigAuthoring Inspector (screenshot)
6. **Profiler data:** If performance issue (screenshot of Profiler)

### Where to Ask

- **Unity Forums:** DOTS subsection
- **Unity Discord:** #dots channel  
- **GitHub Issues:** (if project is on GitHub)

### Debug Information Script

Create this utility to dump system state:

```csharp
using Unity.Entities;
using UnityEngine;

public class TerrainDebugInfo : MonoBehaviour
{
    [ContextMenu("Print Terrain Status")]
    void PrintStatus()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        
        // Check singletons
        var configQuery = em.CreateEntityQuery(typeof(TerrainTileConfig));
        Debug.Log($"TerrainTileConfig entities: {configQuery.CalculateEntityCount()}");
        
        var playerQuery = em.CreateEntityQuery(typeof(PlayerTag));
        Debug.Log($"PlayerTag entities: {playerQuery.CalculateEntityCount()}");
        
        var tileQuery = em.CreateEntityQuery(typeof(TerrainTile));
        Debug.Log($"Active terrain tiles: {tileQuery.CalculateEntityCount()}");
        
        var meshQuery = em.CreateEntityQuery(typeof(MeshReference));
        Debug.Log($"Tiles with meshes: {meshQuery.CalculateEntityCount()}");
        
        var physicsQuery = em.CreateEntityQuery(typeof(Unity.Physics.PhysicsCollider));
        Debug.Log($"Tiles with physics: {physicsQuery.CalculateEntityCount()}");
        
        // Check world offset
        if (configQuery.CalculateEntityCount() > 0)
        {
            var configEntity = configQuery.GetSingletonEntity();
            if (em.HasComponent<WorldOriginOffset>(configEntity))
            {
                var offset = em.GetComponentData<WorldOriginOffset>(configEntity);
                Debug.Log($"World offset: {offset.accumulatedOffset}");
            }
        }
    }
}
```

**Usage:**
1. Add to any GameObject
2. Right-click component in Inspector
3. Click "Print Terrain Status"
4. Check Console for report

---

## Emergency Fixes

### Fix 1: Force Tile Regeneration

```csharp
// Create temporary component on GameObject in scene:
public class ForceRegenerateAllTiles : MonoBehaviour
{
    void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile));
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        foreach (var entity in entities)
        {
            var tile = em.GetComponentData<TerrainTile>(entity);
            tile.needsRegeneration = true;
            em.SetComponentData(entity, tile);
        }
        
        entities.Dispose();
        Debug.Log("Marked all tiles for regeneration");
        Destroy(this);  // Remove self
    }
}
```

---

### Fix 2: Clear All Tiles and Restart

```csharp
public class ClearAllTerrain : MonoBehaviour
{
    [ContextMenu("Clear All Tiles")]
    void ClearTiles()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile));
        
        em.DestroyEntity(query);
        
        Debug.Log("Destroyed all terrain tiles - will respawn next frame");
    }
}
```

---

### Fix 3: Reset World Offset

```csharp
public class ResetWorldOffset : MonoBehaviour
{
    [ContextMenu("Reset World Offset")]
    void ResetOffset()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(WorldOriginOffset));
        
        if (query.CalculateEntityCount() > 0)
        {
            var entity = query.GetSingletonEntity();
            var offset = em.GetComponentData<WorldOriginOffset>(entity);
            offset.accumulatedOffset = Unity.Mathematics.double3.zero;
            em.SetComponentData(entity, offset);
            Debug.Log("Reset world offset to zero");
        }
    }
}
```

---

## Contact & Support

For issues not covered here, see:
- [COMPLETE_SOLUTION_SUMMARY.md](../COMPLETE_SOLUTION_SUMMARY.md) - Rendering fixes applied
- [API_REFERENCE.md](API_REFERENCE.md) - Component/system details
- [TECHNICAL_DETAILS.md](TECHNICAL_DETAILS.md) - Deep implementation knowledge

**Remember:** The system is complex but modular. Isolate which system is failing, then focus debugging there.

