# Performance Optimization Guide
**Version:** 3.0  
**Last Updated:** May 4, 2026

Complete guide to optimizing terrain system performance for VR and desktop applications.

## Performance Targets

### VR Requirements
**Frame Rate**: 90 fps (11.1ms per frame)  
**Terrain Budget**: 5-8ms per frame maximum  
**Critical**: No frame spikes (causes motion sickness)

### Desktop Requirements
**Frame Rate**: 60 fps (16.6ms per frame)  
**Terrain Budget**: 10-12ms per frame acceptable  
**Tolerance**: Brief spikes acceptable (<100ms)

---

## Quick Optimization Checklist

For immediate performance improvements:

### VR Optimization (90fps)
```
✅ Vertices Per Side: 48 OK with low BVH budget (Escape Mountain full-res)
✅ View Distance: 300-400m (not 500m+)
✅ Noise Octaves: 3-4 (not 5+)
✅ Max Colliders Created Per Frame: 4-6 (mesh generation)
✅ Max Physics Colliders Per Frame: 1-2 (BVH batch — critical on Quest)
✅ Max Collider Distance: 220-400m
✅ Max Collider Cache Memory: 32-53 MB
```

### Desktop Optimization (60fps)
```
✅ Vertices Per Side: 32-48
✅ View Distance: 500-800m
✅ Noise Octaves: 4-5
✅ Max Colliders Created Per Frame: 8-12
✅ Max Physics Colliders Per Frame: 6-8
✅ Max Collider Distance: 500-600m
```

---

## Profiling Workflow

### Step 1: Identify Bottleneck

**Open Unity Profiler**:
```
Window → Analysis → Profiler
Enable "Deep Profile"
Play scene
Move player to trigger tile spawning
```

**Check These Markers**:
- `TerrainMesh.Schedule` / `TerrainMesh.Complete` - Mesh generation scheduling and buffer copy
- `TerrainMesh.TrailLUTBuild` - Per-tile trail centerline LUT build (Editor only)
- `TerrainMesh.TrailInfluence` - LUT-based trail height blending per vertex (Editor only)
- `TerrainMesh.BaseNoise` - Base terrain octave noise per vertex (Editor only)
- `TerrainPhysics.BvhSchedule` / `TerrainPhysics.BvhComplete` - BVH construction time
- `BuildTerrainMeshColliderJob` - Per-tile MeshCollider.Create on worker threads
- `StaticObjectSpawner.Instantiation` - Static object ECB instantiation batch
- `EndSimulationEntityCommandBufferSystem` - Deferred structural changes playback (spawn + chunk assign)
- CPU markers for overall frame time

**Typical Bottlenecks**:
1. **TerrainMesh.Generation > 10ms**: Too many vertices or octaves
2. **TerrainPhysics.BvhComplete > 8ms**: `maxPhysicsCollidersCreatedPerFrame` too high for 48×48 tiles on Quest — lower to 1-2
3. **BuildTerrainMeshColliderJob long tail**: Multiple parallel BVHs in one batch — reduce physics budget
4. **EntityCommandBuffer.Playback > 2ms**: Static object spawn/despawn budget too high for density, or SubScene prefabs not re-baked after component baking changes
5. **GPU time > CPU time**: Too many tiles or vertices rendering

---

### Step 2: Apply Targeted Optimizations

Based on bottleneck identified:

#### If Mesh Generation is Slow

**Reduce Vertex Count**:
```
Vertices Per Side: 16 (was 32)
Impact: 4× fewer vertices, ~4× faster
```

**Reduce Noise Detail**:
```
Noise Octaves: 3 (was 4)
Impact: 25% faster mesh generation
```

**Increase Frame Budget** (trade smoothness for speed):
```
Max Colliders Per Frame: 5 (was 3)
Impact: Faster completion, may cause brief spikes
```

**Tune Trail LUT Step** (when three trails are enabled):
```
Trail LUT Step Meters: 1.0 (default)
Impact: Lower values (0.5) sharpen blend edges but increase LUT build cost;
        higher values (2.0) reduce LUT samples with softer trail shoulders.
```

**Disable unused trails**:
```
Trail 2/3 Enabled: false
Impact: Skips LUT build and influence lookup for disabled trails entirely.
```

---

#### If Collider Creation is Slow

**Reduce Collider Budget** (trade speed for smoothness):
```
Max Physics Colliders Per Frame: 1 (was 2-6)
Impact: Slower first-time collider completion, bounded frame spikes on Quest
```

**Reduce collider coverage**:
```
Max Collider Distance: 200m (was 450m)
Vertices Per Side: 24 (was 32)
Impact: Fewer tiles with colliders, lower triangle count
```

---

#### If GPU Rendering is Slow

**Reduce View Distance**:
```
View Distance: 400m (was 500m)
Impact: Quadratic reduction in tiles (~36% fewer)
```

**Disable Shadows**:
```csharp
// In TerrainRenderingSystem.cs
shadowCastingMode: ShadowCastingMode.Off
```

**Optimize Material**:
```
Use simpler shader (Unlit instead of Lit)
Reduce texture resolution
Disable unnecessary material features
```

---

## CPU Optimization Strategies

### Strategy 1: Reduce Tile Count

**Method**: Increase tile size or reduce view distance

**Example**:
```
Before:
  Tile Size: 100m
  View Distance: 500m
  Active Tiles: ~25

After:
  Tile Size: 200m (2× larger)
  View Distance: 500m
  Active Tiles: ~7 (72% reduction!)

OR:
  Tile Size: 100m
  View Distance: 350m (30% smaller)
  Active Tiles: ~12 (52% reduction)
```

**Impact**: Linear reduction in per-frame system overhead.

---

### Strategy 2: Reduce Vertex Density

**Method**: Lower vertices per side

**Example**:
```
Before:
  Vertices Per Side: 32
  Total Vertices: 1024
  Mesh Gen Time: 8ms

After:
  Vertices Per Side: 16
  Total Vertices: 256 (75% reduction)
  Mesh Gen Time: 2ms (75% faster)
```

**Trade-off**: Lower visual quality, less smooth terrain.

---

### Strategy 3: Optimize Noise Generation

**Method A**: Reduce octaves
```
Noise Octaves: 3 (was 4)
Impact: 25% faster noise calculation
Visual: Less fine detail
```

**Method B**: Use single-octave for distant tiles
```csharp
// In mesh generation job
int octaves = (distance < 200f) ? config.noiseOctaves : 1;
```

**Method C**: Lower sampling resolution
```
Sample noise at lower resolution, interpolate between samples
Advanced technique, requires code modification
```

**Method D**: Trail centerline LUT (implemented)
```
Trail LUT Step Meters: 1.0 on TerrainConfigAuthoring
Impact: Replaces per-vertex 48-sample snoise search with one LUT build per tile
        (~900 snoise/tile vs ~330k with three trails at 48×48 vertices).
Tiles outside trail corridors skip LUT build and trail influence entirely.
Height/mesh/normals jobs run in parallel (batch size 64) even when maxCollidersCreatedPerFrame = 1;
        halo heights eliminate edge re-sampling in the normals pass.
```

---

### Strategy 4: Optimize Frame Budgets

**Balance**: Speed vs. Smoothness

**For Smooth Frame Times (VR)**:
```
Max Colliders Per Frame: 2-3
Result: Slower tile completion, no spikes
```

**For Fast Tile Completion (Desktop)**:
```
Max Colliders Per Frame: 10
Result: Faster completion, potential brief spikes
```

**Adaptive Budgeting** (advanced):
```csharp
int budget = (Application.targetFrameRate >= 90) ? 3 : 10;
```

---

### Strategy 5: Physics Distance Culling

**Method**: Reduce `maxColliderDistance` to limit how many tiles have colliders

**Aggressive culling** (maximum performance):
```
Max Collider Distance: 150m
Vertices Per Side: 24

Result: Fewer tiles with colliders, lower triangle count
```

**Conservative culling** (maximum coverage):
```
Max Collider Distance: 600m
Vertices Per Side: 48

Result: More tiles with full-resolution colliders
```

**Balanced** (recommended for VR):
```
Max Collider Distance: 300-450m
Vertices Per Side: 32
Max Physics Colliders Per Frame: 4
```

---

## GPU Optimization Strategies

### Strategy 1: Reduce Draw Calls

**Check Batching**:
```
Window → Analysis → Frame Debugger
Look for "Entities Graphics" section
Count draws for terrain tiles
```

**Optimize Batching**:
```
✅ All tiles use same material
✅ All tiles have same scale (1.0)
✅ No per-tile material properties
❌ Different materials per tile
❌ Different scales per tile
```

**Expected**: 1-5 draw calls for all tiles (excellent batching).

---

### Strategy 2: Frustum Culling

**Verify Culling Working**:
```
Frame Debugger → Look for "Culled" annotation
RenderBounds should cull off-screen tiles
```

**If Not Culling**:
```
Problem: RenderBounds too large or incorrect
Check: TerrainRenderingSystem bounds calculation
Fix: Ensure bounds match mesh extents
```

---

### Strategy 3: Reduce Overdraw

**Problem**: Multiple tiles drawing on same screen pixels

**Check Overdraw**:
```
Scene view → Shading Mode → Overdraw
Red areas = high overdraw
```

**Solutions**:
```
Solution 1: Reduce view distance (fewer tiles)
Solution 2: Use occlusion culling (future feature)
Solution 3: Optimize camera angle (less overlapping tiles)
```

---

### Strategy 4: Texture Optimization

**Material Textures**:
```
Base Map: Use compressed format (DXT5/BC7)
Resolution: 1024×1024 max (not 4096×4096)
Mipmaps: Enable (improves far distance performance)
Filtering: Trilinear with anisotropic (quality/performance balance)
```

**Texture Settings**:
```
Max Size: 1024
Compression: High Quality
Mipmaps: ✅
Anisotropic Level: 4
```

---

### Strategy 5: Shader Optimization

**Use Simpler Shaders**:
```
Expensive: Standard shader (complex lighting)
Moderate: URP Lit (balanced)
Fast: URP Simple Lit (fewer features)
Fastest: Unlit (no lighting calculations)
```

**For VR**: Use URP Lit with minimal features
**For Desktop**: Can use Standard or full URP Lit

---

## Memory Optimization

### Strategy 1: Reduce Active Tiles

**Method**: Smaller view distance

```
View Distance: 300m (was 500m)
Active Tiles: 9 (was 25) = 64% reduction
Memory: 600KB (was 1.7MB)
```

---

### Strategy 2: Optimize Collider Cache

**Method**: Tune cache size for your usage

**High Movement** (player moves frequently):
```
Max Collider Cache: 100MB
Result: Better hit rate, less creation
```

**Low Movement** (player mostly stationary):
```
Max Collider Cache: 25MB
Result: Less memory usage, cache still effective
```

---

### Strategy 3: Unload Distant Resources

**Method**: Destroy meshes for very distant tiles

```csharp
// Custom system to destroy meshes beyond rendering distance
if (distance > config.viewDistance * 1.5f)
{
    if (em.HasComponent<MeshReference>(entity))
    {
        var meshRef = em.GetComponentObject<MeshReference>(entity);
        Object.Destroy(meshRef.mesh);
        em.RemoveComponent<MeshReference>(entity);
    }
}
```

**Impact**: Reduces mesh memory for tiles outside view.

---

## Build Optimization

Runtime debug visualizers and verbose logging were removed from source. Editor-only profiler markers and `#if UNITY_EDITOR` blocks are stripped automatically in release builds.

### Build Settings

**Player Settings → Other Settings**:
```
Managed Stripping Level: High
Strip Engine Code: ✅
Optimize Mesh Data: ✅
```

### DOTS Settings

**DOTS → Build Configuration**:
```
Burst Compilation: Release
IL2CPP Code Generation: Faster Runtime
```

---

## Platform-Specific Optimization

### Quest 2 / Mobile VR

```
Vertices Per Side: 12-16 (very low poly)
View Distance: 250m
Noise Octaves: 2-3
Max Colliders Per Frame: 2
Texture Resolution: 512×512 max
Shadows: Off
```

### PC VR (Index, Rift S)

```
Vertices Per Side: 24-32
View Distance: 400m
Noise Octaves: 3-4
Max Colliders Per Frame: 3-5
Texture Resolution: 1024×1024
Shadows: On (soft)
```

### Desktop PC

```
Vertices Per Side: 48-64
View Distance: 800m
Noise Octaves: 5-6
Max Colliders Per Frame: 10
Texture Resolution: 2048×2048
Shadows: On (high quality)
```

---

## Monitoring Performance

### Runtime Performance Overlay

```csharp
public class TerrainPerformanceUI : MonoBehaviour
{
    void OnGUI()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        var em = world?.EntityManager;
        if (em == null) return;
        
        var tileQuery = em.CreateEntityQuery(typeof(TerrainTile));
        int tileCount = tileQuery.CalculateEntityCount();
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label($"FPS: {1f / Time.deltaTime:F0}");
        GUILayout.Label($"Tiles: {tileCount}");
        GUILayout.Label($"Frame Time: {Time.deltaTime * 1000:F1}ms");
        GUILayout.EndArea();
        
        tileQuery.Dispose();
    }
}
```

### Logging Performance Stats

```csharp
[ContextMenu("Log Performance Stats")]
public void LogPerformanceStats()
{
    var world = World.DefaultGameObjectInjectionWorld;
    var em = world.EntityManager;
    
    // Tile counts
    var tileQuery = em.CreateEntityQuery(typeof(TerrainTile));
    var meshQuery = em.CreateEntityQuery(typeof(TerrainTile), typeof(MeshReference));
    var physicsQuery = em.CreateEntityQuery(typeof(TerrainTile), typeof(Unity.Physics.PhysicsCollider));
    
    Debug.Log($"=== Terrain Performance ===");
    Debug.Log($"Total Tiles: {tileQuery.CalculateEntityCount()}");
    Debug.Log($"Rendered Tiles: {meshQuery.CalculateEntityCount()}");
    Debug.Log($"Physics Tiles: {physicsQuery.CalculateEntityCount()}");
    Debug.Log($"Frame Time: {Time.deltaTime * 1000:F1}ms");
    Debug.Log($"FPS: {1f / Time.deltaTime:F0}");
    
    tileQuery.Dispose();
    meshQuery.Dispose();
    physicsQuery.Dispose();
}
```

---

## Optimization Presets

### Preset 1: VR Ultra Performance (Quest 2)

**Target**: 90fps stable on mobile VR

```
Tile Size: 150
View Distance: 300
Vertices Per Side: 16
Noise Frequency: 0.01
Noise Amplitude: 15
Noise Octaves: 3
Noise Lacunarity: 2.0
Noise Persistence: 0.4

Max Colliders Created Per Frame: 4
Max Physics Colliders Per Frame: 3
Max Collider Distance: 250m

Scroll Enabled: ✅ (if needed)
Scroll Speed: 8.0
```

**Expected Performance**:
- Active Tiles: ~4-9
- Mesh Gen: <3ms per frame
- Collider Creation: <6ms per frame
- Total: <10ms per frame

---

### Preset 2: VR Balanced (PC VR)

**Target**: 90fps on PC VR (Index, Quest 3)

```
Tile Size: 100
View Distance: 400
Vertices Per Side: 24
Noise Frequency: 0.01
Noise Amplitude: 20
Noise Octaves: 4
Noise Lacunarity: 2.0
Noise Persistence: 0.5

Max Colliders Created Per Frame: 3
Max Physics Colliders Per Frame: 3
Max Collider Distance: 250m

Scroll Enabled: ✅
Scroll Speed: 10.0
```

**Expected Performance**:
- Active Tiles: ~16
- Mesh Gen: ~5ms per frame
- Collider Creation: ~8ms per frame
- Total: ~13ms peak (usually <10ms)

---

### Preset 3: Desktop High Quality

**Target**: 60fps on desktop PC

```
Tile Size: 100
View Distance: 600
Vertices Per Side: 48
Noise Frequency: 0.01
Noise Amplitude: 35
Noise Octaves: 5
Noise Lacunarity: 2.0
Noise Persistence: 0.6

Max Colliders Created Per Frame: 8
Max Physics Colliders Per Frame: 6
Max Collider Distance: 550m

Scroll Enabled: ✅
Scroll Speed: 15.0
```

**Expected Performance**:
- Active Tiles: ~36
- Mesh Gen: ~10ms per frame
- Collider Creation: ~12ms per frame
- Total: ~15ms peak

---

### Preset 4: Desktop Ultra (High-end PC)

**Target**: 60fps, maximum visual quality

```
Tile Size: 100
View Distance: 1000
Vertices Per Side: 64
Noise Frequency: 0.008
Noise Amplitude: 60
Noise Octaves: 7
Noise Lacunarity: 2.2
Noise Persistence: 0.65

Max Colliders Created Per Frame: 12
Max Physics Colliders Per Frame: 8
Max Collider Distance: 900m
```

**Expected Performance**:
- Active Tiles: ~100
- Mesh Gen: ~15ms per frame (budgeted)
- Total: ~25-30ms peak (acceptable for 60fps)

---

## Advanced Optimizations

### Optimization 1: Adaptive Frame Budgets

Adjust budgets based on frame time:

```csharp
public class AdaptiveBudget : MonoBehaviour
{
    private float _targetFrameTime = 0.011f; // 90fps
    
    void Update()
    {
        float currentFrameTime = Time.deltaTime;
        
        if (currentFrameTime < _targetFrameTime * 0.8f)
        {
            // Frame time good, can increase budget
            IncreaseBudget();
        }
        else if (currentFrameTime > _targetFrameTime * 1.2f)
        {
            // Frame time bad, decrease budget
            DecreaseBudget();
        }
    }
}
```

---

### Optimization 2: Async Mesh Creation

**Future Enhancement**: Use Mesh.MeshDataArray for parallel creation

```csharp
// Unity 2020.2+ API
var meshDataArray = Mesh.AllocateWritableMeshData(meshCount);

// Fill in parallel job
var job = new FillMeshDataJob { meshData = meshDataArray };
job.Schedule(meshCount, 1).Complete();

// Apply to meshes
Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, meshes);
```

**Benefit**: Create multiple meshes in parallel (currently main-thread bottleneck).

---

### Optimization 3: Incremental Collider Updates

Instead of full recreation, update only changed portions:

```csharp
// Future optimization idea
if (tile.partialUpdate)
{
    // Update only affected vertices in existing collider
    UpdateColliderRegion(collider, changedVertices);
}
else
{
    // Full recreation
    CreateNewCollider();
}
```

---

### Optimization 4: GPU Instancing

**For Repeated Patterns**: Use GPU instancing for identical terrain features

```csharp
// Not currently implemented
// Future: Spawn rocks, trees, grass via GPU instancing
// Significant GPU performance improvement
```

---

## Measurement & Benchmarking

### Benchmark Test

```csharp
public class TerrainBenchmark : MonoBehaviour
{
    public int testDurationSeconds = 60;
    
    [ContextMenu("Run Benchmark")]
    public void RunBenchmark()
    {
        StartCoroutine(BenchmarkRoutine());
    }
    
    IEnumerator BenchmarkRoutine()
    {
        float startTime = Time.realtimeSinceStartup;
        float elapsed = 0f;
        int frameCount = 0;
        float minFPS = float.MaxValue;
        float maxFPS = 0f;
        float totalFPS = 0f;
        
        while (elapsed < testDurationSeconds)
        {
            float fps = 1f / Time.deltaTime;
            minFPS = Mathf.Min(minFPS, fps);
            maxFPS = Mathf.Max(maxFPS, fps);
            totalFPS += fps;
            frameCount++;
            
            elapsed = Time.realtimeSinceStartup - startTime;
            yield return null;
        }
        
        float avgFPS = totalFPS / frameCount;
        
        Debug.Log($"=== Terrain Benchmark Results ({testDurationSeconds}s) ===");
        Debug.Log($"Average FPS: {avgFPS:F1}");
        Debug.Log($"Min FPS: {minFPS:F1}");
        Debug.Log($"Max FPS: {maxFPS:F1}");
        Debug.Log($"Frame Count: {frameCount}");
    }
}
```

---

### Stress Test

Test worst-case scenario (teleport player far):

```csharp
[ContextMenu("Stress Test - Teleport")]
public void StressTeleport()
{
    var player = GetTrackedPlayer();
    if (player == null) return;
    
    // Teleport 5000m away (forces respawn of all tiles)
    player.position = new Vector3(5000, 0, 5000);
    
    Debug.Log("Stress test: Monitor frame time for next 5 seconds");
}
```

**Monitor**: Frame time should recover within 1-2 seconds.

---

## Performance Checklist

Before deploying, verify:

```
✅ Profiler shows terrain < 8ms per frame (VR) or < 12ms (desktop)
✅ No frame spikes > 20ms during normal gameplay
✅ Cache hit rate > 80% (check logs)
✅ FPS stable at target (90fps VR, 60fps desktop)
✅ Memory usage stable (no continuous growth)
✅ GPU time < CPU time (balanced workload)
✅ Draw calls < 10 for all terrain tiles
✅ No GC allocations during gameplay (check Profiler)
```

---

## Troubleshooting Performance Issues

### Issue: Frame Rate Below Target

**Step 1**: Profile and identify bottleneck (see above)  
**Step 2**: Apply optimization preset for your platform  
**Step 3**: Incrementally adjust parameters  
**Step 4**: Re-profile to measure improvement

---

### Issue: Inconsistent Frame Times

**Causes**:
- GC spikes (check for managed allocations)
- Budget too high (brief overload)
- Other systems interfering

**Solutions**:
1. Verify zero GC (Profiler → GC.Alloc should be 0)
2. Reduce budgets for smoother frame times
3. Profile other systems (disable terrain temporarily)

---

### Issue: High Memory Usage

**Causes**:
- Too many active tiles
- Collider cache too large
- Meshes not disposing

**Solutions**:
1. Reduce view distance
2. Reduce collider cache limit
3. Check entity cleanup on despawn

---

## Performance Monitoring Dashboard

### Real-Time Performance Display

```csharp
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

public class TerrainPerformanceDashboard : MonoBehaviour
{
    private ProfilerRecorder _meshGenRecorder;
    private ProfilerRecorder _physicsRecorder;
    private ProfilerRecorder _frameTimeRecorder;
    
    void OnEnable()
    {
        _meshGenRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TerrainMesh.Generation");
        _physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TerrainPhysics.ColliderCreation");
        _frameTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
    }
    
    void OnDisable()
    {
        _meshGenRecorder.Dispose();
        _physicsRecorder.Dispose();
        _frameTimeRecorder.Dispose();
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 150));
        GUILayout.Box("=== Terrain Performance ===");
        
        float meshMs = _meshGenRecorder.LastValue / 1e6;
        float physicsMs = _physicsRecorder.LastValue / 1e6;
        float frameMs = GetAverageFrameTime();
        float fps = 1000f / frameMs;
        
        GUILayout.Label($"FPS: {fps:F0}");
        GUILayout.Label($"Frame Time: {frameMs:F1}ms");
        GUILayout.Label($"Mesh Gen: {meshMs:F2}ms");
        GUILayout.Label($"Physics: {physicsMs:F2}ms");
        
        // Color-coded performance
        string status = (fps >= 85) ? "✅ GOOD" : (fps >= 60) ? "⚠️ OK" : "❌ BAD";
        GUILayout.Label($"Status: {status}");
        
        GUILayout.EndArea();
    }
    
    float GetAverageFrameTime()
    {
        var samplesCount = _frameTimeRecorder.Count;
        if (samplesCount == 0) return 0;
        
        double total = 0;
        unsafe
        {
            var samples = stackalloc ProfilerRecorderSample[samplesCount];
            _frameTimeRecorder.CopyTo(samples, samplesCount);
            for (var i = 0; i < samplesCount; ++i)
                total += samples[i].Value;
        }
        
        return (float)(total / samplesCount / 1e6);
    }
}
```

---

## Related Documentation

- **[Troubleshooting Guide](TROUBLESHOOTING.md)** - Solving specific issues
- **[Technical Details](TECHNICAL_DETAILS.md)** - Implementation details
- **[System Pipeline](SYSTEM_PIPELINE.md)** - System execution order
- **[Configuration Reference](CONFIGURATION.md)** - All parameters

---

**Back to**: [Documentation Hub](README.md)

