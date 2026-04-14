# Buffer Invalidation Fix - TerrainTreeSpawningSystem

## Problem

**Error 1**: `ObjectDisposedException: Attempted to access BufferTypeHandle<SpawnedTreeReference> which has been invalidated by a structural change.`

**Error 2**: `ObjectDisposedException: Attempted to access BufferTypeHandle<VertexElement> which has been invalidated by a structural change.`

**Cause**: Multiple structural changes were invalidating buffer references at different points in the code.

### Issue 1: SpawnedTreeReference Buffer
Adding the `Parent` component to tree entities invalidates the `SpawnedTreeReference` buffer.

### Issue 2: VertexElement/NormalElement Buffers  
Adding the `SpawnedTreeReference` buffer (if it doesn't exist) invalidates the `vertices` and `normals` buffers that were obtained earlier.

## Solution

**Two-part fix**:

1. **Ensure buffer exists BEFORE accessing other buffers** - Do all structural changes first
2. **Copy buffer data to native arrays** - Prevents invalidation during subsequent structural changes

## Solution

**Two-part fix**:

1. **Ensure buffer exists BEFORE accessing other buffers** - Do all structural changes first
2. **Copy buffer data to native arrays** - Prevents invalidation during subsequent structural changes

### Before (Broken)
```csharp
// Get buffers
var vertices = GetBuffer<VertexElement>(tile);
var normals = GetBuffer<NormalElement>(tile);

// Later: Add buffer if missing (STRUCTURAL CHANGE - invalidates vertices/normals!)
if (!HasBuffer<SpawnedTreeReference>(tile)) {
    AddBuffer<SpawnedTreeReference>(tile);
}

// Get spawned trees buffer
var spawnedTreesBuffer = GetBuffer<SpawnedTreeReference>(tile);

while (spawning trees) {
    // Access vertices (CRASH - buffer invalidated!)
    int index = random.NextInt(0, vertices.Length);
    float3 pos = vertices[index].value;
    
    // Add Parent (STRUCTURAL CHANGE - invalidates spawnedTreesBuffer!)
    AddComponent(tree, new Parent { ... });
    
    // Try to use buffer (CRASH - buffer invalidated!)
    spawnedTreesBuffer.Add(...);
}
```

### After (Fixed)
```csharp
// FIRST: Ensure SpawnedTreeReference buffer exists (do structural changes first!)
if (!HasBuffer<SpawnedTreeReference>(tile)) {
    AddBuffer<SpawnedTreeReference>(tile);
}

// NOW get buffers (after structural change)
var vertices = GetBuffer<VertexElement>(tile);
var normals = GetBuffer<NormalElement>(tile);

// Copy data to native arrays (protects from invalidation)
var vertexPositions = new NativeArray<float3>(vertices.Length, Allocator.Temp);
var vertexNormals = new NativeArray<float3>(normals.Length, Allocator.Temp);
for (int i = 0; i < vertices.Length; i++) {
    vertexPositions[i] = vertices[i].value;
    vertexNormals[i] = normals[i].value;
}

// Temporary list to collect tree entities
var tempSpawnedTrees = new NativeList<Entity>(count, Allocator.Temp);

while (spawning trees) {
    // Use copied data (safe from invalidation)
    int index = random.NextInt(0, vertexPositions.Length);
    float3 pos = vertexPositions[index];
    float3 normal = vertexNormals[index];
    
    // Add Parent (structural change - but we don't access buffers here)
    AddComponent(tree, new Parent { ... });
    
    // Store in temp list
    tempSpawnedTrees.Add(tree);
}

// AFTER all structural changes, get fresh buffer and add all at once
var spawnedTreesBuffer = GetBuffer<SpawnedTreeReference>(tile);
foreach (var tree in tempSpawnedTrees) {
    spawnedTreesBuffer.Add(new SpawnedTreeReference { treeEntity = tree });
}

// Cleanup
vertexPositions.Dispose();
vertexNormals.Dispose();
tempSpawnedTrees.Dispose();
```

## Key Points

### Why Structural Changes Invalidate Buffers

In Unity ECS, **structural changes** include:
- Adding/removing components
- Adding/removing entities
- Creating/destroying entities

These operations can cause:
- Memory reallocation
- Archetype changes (entities moving between archetypes)
- Internal data structure reorganization

Therefore, any `DynamicBuffer` references or `ComponentData` references become **invalid** after a structural change.

### Best Practice: Batch Structural Changes

**Option 1**: Use EntityCommandBuffer (for jobs or deferred changes)
```csharp
var ecb = new EntityCommandBuffer(Allocator.Temp);
// Queue up changes
ecb.AddComponent(entity, new Parent { Value = parent });
// Execute all at once
ecb.Playback(EntityManager);
ecb.Dispose();
```

**Option 2**: Collect entities, then process (for immediate mode)
```csharp
var entities = new NativeList<Entity>(Allocator.Temp);
// Collect entities
foreach (spawn) { entities.Add(newEntity); }
// After all structural changes, process
var buffer = EntityManager.GetBuffer<T>(targetEntity);
foreach (var e in entities) { buffer.Add(...); }
entities.Dispose();
```

**Option 3**: Re-get buffer after each structural change (least efficient)
```csharp
while (spawning) {
    EntityManager.AddComponent(...); // Structural change
    var buffer = EntityManager.GetBuffer<T>(entity); // Re-get after change
    buffer.Add(...);
}
```

## Implementation

The fix uses a **multi-layered approach**:

### Layer 1: Order Structural Changes First
```csharp
// Do ALL structural changes before getting buffers
if (!EntityManager.HasBuffer<SpawnedTreeReference>(tileEntity))
{
    EntityManager.AddBuffer<SpawnedTreeReference>(tileEntity);
}

// NOW safe to get other buffers
var vertices = EntityManager.GetBuffer<VertexElement>(tileEntity);
var normals = EntityManager.GetBuffer<NormalElement>(tileEntity);
```

### Layer 2: Copy Data to Prevent Invalidation
```csharp
// Copy buffer data to native arrays
var vertexPositions = new NativeArray<float3>(vertices.Length, Allocator.Temp);
var vertexNormals = new NativeArray<float3>(normals.Length, Allocator.Temp);

for (int i = 0; i < vertices.Length; i++)
{
    vertexPositions[i] = vertices[i].value;
    vertexNormals[i] = normals[i].value;
}

// Now use copied data in loop (immune to buffer invalidation)
while (spawning) {
    float3 pos = vertexPositions[randomIndex];  // Safe!
    float3 normal = vertexNormals[randomIndex]; // Safe!
}
```

### Layer 3: Batch Updates After Structural Changes
```csharp
// Collect tree entities during spawning
var tempSpawnedTrees = new NativeList<Entity>(Allocator.Temp);

// Spawn all trees and add Parent components
while (spawning) {
    Entity tree = Instantiate(prefab);
    AddComponent(tree, new Parent { ... }); // Structural change
    tempSpawnedTrees.Add(tree);
}

// After all structural changes, update buffer
var buffer = GetBuffer<SpawnedTreeReference>(tile);
foreach (var tree in tempSpawnedTrees) {
    buffer.Add(new SpawnedTreeReference { treeEntity = tree });
}
```

**Benefits**:
- No buffer invalidation issues
- Clean separation of concerns
- Better performance (fewer buffer lookups)
- Safe and predictable

## Testing

✅ Code compiles without errors  
✅ No buffer invalidation errors  
✅ Trees spawn correctly with parent-child relationship  
✅ `SpawnedTreeReference` buffer populated for debugging  

## Related Documentation

- Unity ECS Manual: Structural Changes
- AGENTS.md: Zero-GC Pattern for ECS
- TREE_SPAWNING_SYSTEM.md: Tree spawning system overview

