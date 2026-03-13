# Terrain Visibility Troubleshooting Guide

## Issue: Tiles Created But Not Visible

If you see console messages indicating tiles are being spawned but don't see any terrain, follow these steps:

### Step 1: Check Console Output

With the debug logging now added, you should see these messages when entering Play Mode:

```
[TileSpawning] Spawning X new tiles
[TileSpawning] Created tile at grid (0, 0), world position (0, 0, 0)
[TerrainMeshGen] Generating mesh for tile at (0, 0)
[TerrainMeshGen] ✓ Mesh generated: 1024 vertices, 1922 triangles for tile at (0, 0)
[TerrainRendering] Processing X tiles for rendering setup
[TerrainRendering] Creating mesh for entity X
[TerrainRendering] Mesh created: 1024 verts, 1922 tris, bounds=...
[TerrainRendering] ✓ Mesh setup complete for entity X
[TerrainDebug] ========== Terrain Tile Analysis ==========
```

### Step 2: Check the Debug System Output

The `TerrainRenderingDebugSystem` will log detailed information every 2 seconds. Look for:

**✅ Good Signs:**
- "Tiles with rendering components: X" matches "Total tiles: X"
- "Tiles with LocalToWorld: X" matches total
- "Tiles with RenderBounds: X" matches total
- First tile has MaterialMeshInfo
- Mesh is not null

**❌ Problem Signs:**
- "Missing MaterialMeshInfo!" - Rendering not set up
- "Missing LocalToWorld!" - Transform system issue
- "Missing RenderBounds!" - Culling will fail
- "mesh is null!" - Mesh creation failed

### Step 3: Common Issues & Fixes

#### Issue 1: Material is Missing
**Symptom:** Error about TerrainMaterial not found, or material is null

**Fix:**
1. Check Console for: `[TerrainRendering] Created default URP Lit material`
2. If you see "material is null" errors:
   - Verify URP is properly installed
   - Check that "Universal Render Pipeline/Lit" shader exists
   - Manually create a material and reference it in TerrainConfigAuthoring

#### Issue 2: Entities Graphics Not Working
**Symptom:** "Failed to add render components" error

**Fix:**
1. Verify Unity.Rendering package is installed (should be via Unity.Entities.Graphics)
2. Check Project Settings → Graphics → Scriptable Render Pipeline Settings is set to URP asset
3. Ensure you're using URP, not Built-in or HDRP

#### Issue 3: Tiles Are Below Camera/Player
**Symptom:** Tiles created but camera is above them

**Fix:**
1. Check debug output for tile positions
2. If tiles are at Y=0 but player is at Y=1.5:
   - Tiles might be below player
   - Try adjusting camera angle to look down
   - Or modify `noiseAmplitude` to make terrain taller

#### Issue 4: Render Bounds Too Small
**Symptom:** Tiles disappear when camera moves

**Fix:**
1. Check RenderBounds in debug output
2. If extents are very small, tiles might be culled incorrectly
3. This shouldn't happen with proper mesh bounds calculation

#### Issue 5: LocalToWorld Not Updating
**Symptom:** Tiles exist but LocalToWorld is zero/invalid

**Fix:**
1. Ensure TransformSystemGroup is running
2. Check that LocalTransform is set correctly when tile is spawned
3. LocalToWorld should be automatically calculated from LocalTransform

### Step 4: Manual Verification Steps

#### A. Check Scene View (Not Game View)
1. Enter Play Mode
2. Switch to Scene view (not Game view)
3. Look for gray terrain tiles around origin (0,0,0)
4. If visible in Scene but not Game: Camera position/culling issue

#### B. Check Entities Hierarchy Window
1. Window → Entities → Hierarchy
2. Find entities with TerrainTile component
3. Inspect components - should have:
   - LocalTransform
   - LocalToWorld
   - MaterialMeshInfo
   - RenderBounds
   - MeshReference (managed)

#### C. Check Frame Debugger
1. Window → Analysis → Frame Debugger
2. Enable it
3. Look for draw calls for terrain
4. If no draw calls: Rendering not set up
5. If draw calls but empty: Mesh data issue

### Step 5: Quick Fixes to Try

#### Fix 1: Increase Noise Amplitude
Make terrain much taller so it's obvious:
```csharp
// In TerrainConfigAuthoring:
noiseAmplitude = 100; // Very tall mountains
```

#### Fix 2: Move Player Up
Ensure player is above terrain:
```csharp
// Set player Y position to 50
transform.position = new Vector3(0, 50, 0);
```

#### Fix 3: Disable Culling Temporarily
To rule out culling issues, modify RenderBounds to be huge:
```csharp
// In TerrainRenderingSystem.CreateAndAssignMesh:
var renderBounds = new RenderBounds
{
    Value = new AABB
    {
        Center = float3.zero,
        Extents = new float3(10000, 10000, 10000) // Huge bounds
    }
};
```

#### Fix 4: Check Material in Frame Debugger
1. Enable Frame Debugger
2. Find terrain draw call
3. Check material properties
4. Verify shader is correct (URP/Lit)
5. Check if material has proper textures/colors

### Step 6: Expected Visual Result

With default settings, you should see:
- Gray terrain tiles in a circular pattern around player
- Subtle height variation from noise
- Tiles at Y=0 to Y=20 (amplitude = 20)
- Each tile is 100m × 100m
- Tiles extend 300m from player

### Step 7: Test with Simple Cube

To verify rendering is working at all, create a test:

1. Create a simple cube entity with rendering
2. If cube renders but terrain doesn't: Terrain-specific issue
3. If cube also doesn't render: Entities Graphics setup issue

### Step 8: Check Unity Version Compatibility

This system was built for:
- Unity 2023.3+ (Unity 6)
- Entities 1.0+
- Entities.Graphics 1.0+
- URP 17+

If using different versions, API might have changed.

### Getting More Information

Run the project and post these console logs:
1. All [TileSpawning] messages
2. All [TerrainMeshGen] messages
3. All [TerrainRendering] messages
4. All [TerrainDebug] messages
5. Any error messages (red in Console)

### Emergency Fallback: Test Without Entities Graphics

If all else fails, create a test MonoBehaviour:

```csharp
public class TerrainTileVisualizer : MonoBehaviour
{
    void Update()
    {
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile), typeof(MeshReference));
        var entities = query.ToEntityArray(Allocator.Temp);
        
        foreach (var entity in entities)
        {
            var meshRef = em.GetComponentData<MeshReference>(entity);
            var transform = em.GetComponentData<LocalTransform>(entity);
            
            if (meshRef.mesh != null)
            {
                Graphics.DrawMesh(
                    meshRef.mesh,
                    Matrix4x4.TRS(transform.Position, transform.Rotation, Vector3.one * transform.Scale),
                    Resources.Load<Material>("TerrainMaterial"),
                    0
                );
            }
        }
        
        entities.Dispose();
    }
}
```

This will force render the tiles using Graphics.DrawMesh, bypassing Entities Graphics entirely.

---

## Next Steps After Fixing

Once tiles are visible:
1. Remove debug logging (comment out Debug.Log lines)
2. Remove TerrainRenderingDebugSystem (or disable it)
3. Tune performance settings
4. Add custom material with texture
5. Test floating origin by moving far from origin

