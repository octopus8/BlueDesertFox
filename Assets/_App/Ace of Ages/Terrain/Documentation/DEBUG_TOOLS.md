# Debug Tools - Diagnostic Utilities

Guide to diagnosing terrain issues after runtime debug visualizers were removed. Production systems log **warnings and errors only** for misconfiguration and anomalies; routine success tracing was stripped to reduce console spam.

## Quick Reference

| Tool | Type | When to Use |
|------|------|-------------|
| **Terrain Status Inspector** | Editor window | First stop — material, URP, packages, play-mode entity counts |
| **Console warnings/errors** | Runtime logs | Player tracking failures, bake errors, pool exhaustion, orphan objects |
| **Unity Profiler** | Profiler markers | Frame cost of mesh, physics, LOD, static-object systems |
| **Authoring gizmos** | Scene view (edit mode) | Preview view distance and tile grid on `TerrainConfigAuthoring` |
| **Feature toggles** | Inspector | `renderTerrain` / `enablePhysicsColliders` on `TerrainConfigAuthoring` |

---

## Terrain Status Inspector

**File**: `Editor/TerrainStatusInspector.cs`  
**Menu**: `Window → Terrain → Status Inspector`

### What It Checks

**Edit mode:**
- `TerrainMaterial` exists in `Resources`
- URP is configured
- Required Entities packages are present

**Play mode (additional):**
- ECS world and terrain singletons
- Active tile / rendering entity counts
- Player tracking initialization state

### Actions

- **Create TerrainMaterial** — runs `Tools → Terrain → Create Terrain Material`
- **Setup Physics Layers** — runs `Tools → Terrain → Setup Physics Layer`

Use this instead of the removed runtime `TerrainTrackingDebugger` overlay.

---

## Runtime Validation (`StaticObjectCleanupDebugSystem`)

**File**: `StaticObjectCleanupDebugSystem.cs`  
**Type**: ECS system (`TreeCleanupDebugSystem`)  
**Update**: Every 2 seconds after tile spawning

Automatically detects static objects whose parent tile entity no longer exists (cleanup leak):

```
[StaticObjectCleanup] Found N orphaned static objects!
Objects exist but their parent tiles have been destroyed.
```

No Inspector flag — always active when the terrain subscene is loaded. If you see this warning, investigate tile despawn / static-object cleanup in `TileSpawningSystem`.

---

## Console Diagnostics

Filter the Console with `[Terrain`, `[PlayerTracking`, `[StaticObject`, or `[BulletPool`.

### Expected on Success

Normal play should be **quiet**. You should **not** see success-path `Debug.Log` spam on startup.

### Warnings to Investigate

| Message prefix | Likely cause |
|----------------|--------------|
| `[PlayerTrackingInitSystem] Could not find player` | Wrong search mode/name/tag; player inactive at init |
| `[WorldOriginTrackingInitSystem] Could not find world origin` | Missing world-origin GameObject |
| `[TerrainRendering] No material assigned` | Missing `TerrainMaterial` in Resources |
| `[StaticObjectSpawner]` (bake) | Missing LOD prefabs or invalid spawner config |
| `[BulletPoolSystem] Pool exhausted` | Increase pool size or reduce fire rate |
| `[StaticObjectCleanup] Found N orphaned` | Tile despawn not destroying static objects |

### Errors

Treat all `Debug.LogError` as blocking — terrain, spawning, or rendering will not work correctly until resolved.

---

## Unity Profiler Markers

Editor and development builds include zero-cost-in-release profiler scopes:

| Marker | System |
|--------|--------|
| `TerrainMesh.Generation` | `TerrainMeshGenerationSystem` |
| `TerrainMesh.PrioritySort` | Mesh generation priority pass |
| `TerrainPhysics.ColliderCreation` | Physics collider pipeline |
| `TreeLOD.Update` | `StaticObjectLODUpdateSystem` |
| `TreeLOD.VelocityCalc` | Player velocity for LOD throttling |
| `TreeLOD.ChunkFilter` | Active chunk set construction |

Open **Window → Analysis → Profiler**, enable **Deep Profiling** only if needed (expensive in VR).

---

## Editor Setup Tools

### Create Terrain Material

**Menu**: `Tools → Terrain → Create Terrain Material`  
**File**: `Editor/TerrainMaterialCreator.cs`

Creates `Assets/Resources/TerrainMaterial.mat` (URP/Lit) if missing. Also runs automatically on editor load when the material is absent.

### Setup Physics Layers

**Menu**: `Tools → Terrain → Setup Physics Layer`  
**File**: `Editor/SetupTerrainPhysicsLayers.cs`

Configures **Terrain** and **TerrainLowDetail** physics layers and collision matrix entries.

---

## Authoring Gizmos (Edit Mode)

### TerrainConfigAuthoring

Select the terrain config GameObject in the SubScene. **Selected** gizmos show:
- View distance sphere around the config origin
- Tile grid preview for the current `tileSize` / `viewDistance`

Useful for verifying coverage without entering play mode.

### TerrainAnchorTagAuthoring

Shows anchor sphere and axis when selected — confirms anchor placement for scroll-following entities.

---

## Feature Toggles (Testing)

On `TerrainConfigAuthoring` under **Debug/Testing**:

| Field | Default | Purpose |
|-------|---------|---------|
| `renderTerrain` | true | Disable to test static-object rendering in isolation |
| `enablePhysicsColliders` | true | Disable to profile mesh/rendering without physics cost |

These are functional switches, not console debug flags.

---

## Removed Tools (v3.1+)

The following were removed to reduce runtime overhead and console noise. Use the replacements above.

| Removed | Replacement |
|---------|-------------|
| `TerrainTrackingDebugger` | Terrain Status Inspector + console warnings from `PlayerTrackingInitSystem` |
| `TerrainTileGizmoVisualizer` | Terrain Status Inspector play-mode tile counts; `TerrainConfigAuthoring` gizmos |
| `TerrainColliderVisualizer` | Profiler physics markers; disable `enablePhysicsColliders` to A/B test |
| `TerrainRenderingDebugSystem` | Terrain Status Inspector; console errors from `TerrainRenderingSystem` |
| `StaticObjectLODDebugSystem` | Profiler `TreeLOD.*` markers |
| `TransformFollowerDebugger` | Console warnings from `TransformFollowerInitSystem` |
| `enableRenderingDebug` / `enableSpawnerDebug` / `enableObjectLODDebug` | Removed — use Profiler and console warnings |

---

## Common Workflows

### Player tracking not working

1. Open **Terrain Status Inspector** in play mode
2. Check Console for `[PlayerTrackingInitSystem]` warnings
3. Verify `TerrainConfigAuthoring` player search mode matches your scene setup  
   → [Player Tracking Setup](PLAYER_TRACKING.md)

### Terrain not visible

1. Terrain Status Inspector → confirm material and URP
2. Console: `[TerrainRendering]` errors
3. Camera far clip > `viewDistance`; culling mask includes terrain layer  
   → [Troubleshooting — Not Rendering](TROUBLESHOOTING.md#issue-2-terrain-not-rendering)

### Performance investigation

1. Profiler → filter `Terrain` / `TreeLOD`
2. Temporarily disable `enablePhysicsColliders` or reduce `verticesPerSide`  
   → [Performance Optimization](PERFORMANCE.md)

### Static object leaks

Watch Console for `[StaticObjectCleanup] Found N orphaned static objects` during scroll/despawn testing.

---

**Back to**: [Documentation Hub](README.md)
