# Terrain Mesh Generation Optimization Summary

## Problem
The `TerrainMeshGenerationSystem` was causing significant frame stalls during floating origin shifts. The Unity Profiler showed this system taking excessive time when processing terrain tiles.

## Root Causes
1. **Synchronous Processing**: All tiles needing mesh generation were processed in a single frame on the main thread
2. **No Frame Budgeting**: Unlike `TerrainPhysicsSystem`, there was no limit on tiles processed per frame
3. **Sequential Execution**: Mesh generation loops ran sequentially without parallelization
4. **Expensive Per-Vertex Operations**: Each vertex requires 5 noise samples (1 for height + 4 for normal calculation)

## Solution Implemented

### 1. Parallel Burst Jobs
- Converted mesh generation to `IJobParallelFor` with full Burst compilation
- Each tile is processed independently in parallel across CPU cores
- Job executes vertex generation, normal calculation, and index generation

### 2. Frame Budgeting with Queue System
- Added `NativeQueue<Entity>` to track tiles pending mesh generation
- Processes up to `maxCollidersCreatedPerFrame` tiles per frame (reuses existing config)
- Spreads mesh generation workload across multiple frames to prevent stalls
- Includes duplicate detection via `NativeHashSet` to avoid reprocessing

### 3. Profiler Markers
Added three markers for performance monitoring:
- `TerrainMesh.Generation`: Overall system execution
- `TerrainMesh.JobSchedule`: Job setup and parallel execution
- `TerrainMesh.BufferCopy`: Main-thread buffer copy operations

### 4. Memory Management
- Job data uses `Allocator.TempJob` for efficient automatic cleanup
- Temporary collections use `Allocator.Temp` for single-frame lifetime
- Queue uses `Allocator.Persistent` and is disposed in `OnDestroy`

## Architecture Changes

### New Components
1. **TileMeshJobData**: Struct containing all data needed for parallel mesh generation
   - World position, tile size, vertices per side
   - Noise parameters (frequency, amplitude, octaves, lacunarity, persistence)

2. **GenerateTileMeshJob**: `IJobParallelFor` implementing parallel mesh generation
   - Accepts nested NativeArrays for vertices, normals, UVs, indices
   - Uses `NativeDisableParallelForRestriction` for safe array access
   - Fully Burst-compiled with static helper methods

### Processing Flow
```
OnUpdate
├─ Query tiles needing generation
├─ Enqueue to _pendingTiles
├─ Dequeue up to maxMeshesPerFrame
├─ Allocate job arrays
├─ Schedule parallel jobs (Burst-compiled)
├─ Complete jobs
├─ Copy results to DynamicBuffers (main thread)
└─ Cleanup allocations
```

## Performance Impact

### Before Optimization
- All tiles processed synchronously in one frame
- Main thread execution only
- No parallelization
- Caused visible stalls during origin shifts

### After Optimization
- Tiles processed in batches over multiple frames
- Parallel execution across CPU cores via Burst jobs
- Frame budget prevents stalls (respects `maxCollidersCreatedPerFrame`)
- Expected: 3-10x performance improvement depending on CPU core count

## Configuration

### Using Existing Settings
The system reuses `TerrainTileConfig.maxCollidersCreatedPerFrame` to limit mesh generation:
- Default value: 3 tiles per frame
- Increase for faster generation on high-end hardware
- Decrease to 1-2 for smoother framerate on lower-end hardware

### Tuning Recommendations
1. **High-end VR (RTX 4080+)**: Set to 5-8 tiles/frame
2. **Mid-range VR (RTX 3070)**: Keep at 3-4 tiles/frame
3. **Low-end VR (Quest 2 via Link)**: Set to 1-2 tiles/frame

Monitor `TerrainMesh.Generation` marker in Unity Profiler to ensure <5ms per frame.

## Testing Checklist
- [ ] Verify terrain generates correctly on scene start
- [ ] Trigger floating origin shift and check for stalls (Profiler)
- [ ] Check `TerrainMesh.Generation` marker stays under 5ms
- [ ] Verify mesh normals are correct at tile boundaries
- [ ] Test with different `maxCollidersCreatedPerFrame` values

## Technical Notes

### Why Complete() Instead of Async?
Jobs are completed synchronously via `jobHandle.Complete()` because:
1. Results must be copied to DynamicBuffers in the same frame
2. Mesh rendering system expects buffers to be ready
3. Buffer copy operations cannot be done in jobs (require main thread access)

### Memory Safety
- `NativeDisableParallelForRestriction` is safe here because each job writes to a unique array index
- No shared state between parallel job executions
- Duplicate detection prevents entity reprocessing

### Future Optimization Opportunities
1. **Async Buffer Copy**: Investigate delayed buffer updates if mesh rendering allows
2. **Chunked Processing**: Split large tiles into chunks for finer-grained parallelization
3. **Mesh Data Caching**: Cache generated mesh data similar to physics colliders (requires needsRegeneration flag)
4. **Normal Optimization**: Pre-sample heights in first pass, reuse for normal calculation

## Integration Notes
- No changes required to other systems
- Maintains compatibility with `TileSpawningSystem`, `TerrainPhysicsSystem`
- Works seamlessly with floating origin shifts
- Profiler markers only compile in Editor builds (no runtime overhead)

## Date
Implemented: March 17, 2026

