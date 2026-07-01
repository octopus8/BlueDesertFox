# Rendering System - Entities Graphics Integration

Complete guide to how terrain tiles are rendered using Unity's Entities Graphics package.

## Overview

The `TerrainRenderingSystem` converts ECS mesh buffer data into Unity Mesh objects and configures them for rendering via Entities Graphics (Unity.Rendering.Hybrid).

**Key Features**:
- Zero-copy buffer transfer using `Reinterpret<T>().AsNativeArray()`
- Automatic material assignment
- Entities Graphics integration for batched rendering
- Proper bounds calculation for frustum culling

## How Rendering Works

### High-Level Flow

```
1. TerrainMeshGenerationSystem generates mesh data
   └─ Fills VertexElement, NormalElement, UVElement, IndexElement buffers

2. TerrainRenderingSystem queries tiles ready to render
   └─ Condition: Has mesh buffers + meshGenerated = true + NO MeshReference

3. For each tile:
   ├─ Create Unity Mesh object
   ├─ Copy buffer data (zero-copy via Reinterpret)
   ├─ Calculate bounds
   ├─ Add MeshReference component (holds Mesh object)
   ├─ Add MaterialMeshInfo (rendering metadata)
   └─ Add RenderBounds (frustum culling)

4. Entities Graphics system renders tiles
   └─ Uses MaterialMeshInfo + RenderBounds for culling and batching
```

### System Details

**File**: `TerrainRenderingSystem.cs`  
**Type**: SystemBase (requires main thread for Unity Mesh API)  
**Update Group**: PresentationSystemGroup  
**Performance**: ~0.5-1ms per tile

## Zero-Copy Buffer Transfer

### Traditional Approach (SLOW)

```csharp
// ❌ Allocates managed arrays, causes GC pressure
Vector3[] vertices = new Vector3[buffer.Length];
for (int i = 0; i < buffer.Length; i++)
{
    vertices[i] = buffer[i].value;
}
mesh.vertices = vertices;
```

### Zero-Copy Approach (FAST)

```csharp
// ✅ Zero allocations, direct memory access
var verticesNative = vertexBuffer.Reinterpret<float3>().AsNativeArray();
mesh.SetVertices(verticesNative);
```

**How it works**:
- `Reinterpret<T>()` treats buffer as different type (zero-cost)
- `.AsNativeArray()` creates view of buffer memory (zero-copy)
- `SetVertices(NativeArray)` directly copies from buffer

**Result**: No managed allocations, no GC pressure!

## Material System

### Material Loading

System tries to load material in this order:

1. **Resources folder**: `Resources/TerrainMaterial`
2. **URP Lit shader**: Creates material with `Universal Render Pipeline/Lit`
3. **Standard shader**: Fallback to `Standard`
4. **Unlit shader**: Last resort `Unlit/Color`

**Auto-Generated Material**:
- Pink color (1.0, 0.5, 0.8) for easy debugging
- Named "TerrainMaterial_Generated"
- Should replace with proper material

### Custom Material Setup

**Recommended Approach**:

1. Create Material: `Assets/_App/Ace of Ages/Terrain/Resources/TerrainMaterial.mat`
2. Set shader: `Universal Render Pipeline/Lit`
3. Assign textures:
   - Base Map: Your terrain texture
   - Normal Map: (optional) terrain normal map
4. Set tiling: Match terrain size if using world-space UVs

**Material Properties**:
```csharp
Shader: Universal Render Pipeline/Lit
Base Map: [Your texture]
Base Color: White (1, 1, 1, 1)
Metallic: 0
Smoothness: 0.3
Normal Map: (optional)
```

### Material Assignment

Material assigned to ALL tiles (shared):

```csharp
RenderMeshUtility.AddComponents(
    entity,
    EntityManager,
    new RenderMeshDescription(
        shadowCastingMode: ShadowCastingMode.On,
        receiveShadows: true,
        motionVectorMode: MotionVectorGenerationMode.Camera
    ),
    new RenderMeshArray(
        materials: new[] { _terrainMaterial },
        meshes: new[] { mesh }
    ),
    MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0)
);
```

**Key Point**: All tiles share material for efficient batching.

## Entities Graphics Components

### MaterialMeshInfo

```csharp
public struct MaterialMeshInfo : IComponentData
{
    public int MeshID;
    public int MaterialID;
    // ... other fields
}
```

**Purpose**: Identifies which mesh/material to render  
**Added by**: TerrainRenderingSystem  
**Used by**: Entities Graphics (automatic)

### RenderBounds

```csharp
public struct RenderBounds : IComponentData
{
    public AABB Value; // Axis-aligned bounding box
}
```

**Purpose**: Frustum culling - only render tiles in camera view  
**Calculation**: `GeometryUtility.CalculateBounds()` from vertices  
**Effect**: Off-screen tiles skipped, improves performance

### RenderFilterSettings

```csharp
public struct RenderFilterSettings : ISharedComponentData
{
    public int Layer;
    public int RenderingLayerMask;
    public byte MotionMode;
    public ShadowCastingMode ShadowCastingMode;
    public bool ReceiveShadows;
    public bool StaticShadowCaster;
}
```

**Purpose**: Rendering configuration (shadows, layers, etc.)  
**Added by**: TerrainRenderingSystem via `RenderMeshUtility.AddComponents()`

## Mesh Configuration

### UV Mapping

The system generates UVs based on world-space XZ coordinates:

```csharp
// In mesh generation job
float uvScale = 1.0f / tileConfig.tileSize;

for each vertex at position (x, y, z):
{
    float u = x * uvScale;
    float v = z * uvScale;
    uvBuffer.Add(new UVElement { value = new float2(u, v) });
}
```

**Result**: World-space UVs, seamless across tiles  
**Scale**: 1 UV unit = tile size in world units

**To adjust tiling**:
1. Modify `uvScale` in `TerrainMeshGenerationSystem`
2. Or use material tiling settings

### Normal Calculation

System calculates smooth normals using cross-product method:

```csharp
// For each vertex, average normals of adjacent triangles
for each triangle (v0, v1, v2):
{
    float3 edge1 = v1 - v0;
    float3 edge2 = v2 - v0;
    float3 normal = math.normalize(math.cross(edge1, edge2));
    
    // Accumulate to vertex normals
    vertexNormals[i0] += normal;
    vertexNormals[i1] += normal;
    vertexNormals[i2] += normal;
}

// Normalize accumulated normals
for each vertex:
    normals[i] = math.normalize(accumulatedNormal[i]);
```

**Effect**: Smooth shading across terrain  
**Edge handling**: Tiles calculate normals independently (may have seams)

### Mesh Attributes

Generated meshes have:
- **Vertices**: `float3` positions
- **Normals**: `float3` normalized vectors
- **UVs**: `float2` texture coordinates
- **Triangles**: Int indices (counter-clockwise winding)
- **Bounds**: Calculated automatically
- **Topology**: Triangle list

**Not included** (can add if needed):
- Tangents (for normal mapping)
- Vertex colors
- Secondary UVs
- Bone weights

## Rendering Performance

### CPU Cost

**Per Tile Setup**: ~0.5-1ms
- Mesh object creation: ~0.2ms
- Buffer copy: ~0.2ms (zero-copy, just copying pointers)
- Component setup: ~0.1ms

**Per Frame**: Depends on tiles rendered
- 3 new tiles: ~3ms total

### GPU Cost

**Rendering**: Handled by Entities Graphics
- Automatic batching of tiles with same material
- Frustum culling via RenderBounds
- Typical: <2ms for 50 tiles

### Memory Cost

**Per Tile**: ~50KB (for 32×32 mesh)
- Vertices: 1024 × 12 bytes = 12KB
- Normals: 1024 × 12 bytes = 12KB
- UVs: 1024 × 8 bytes = 8KB
- Triangles: 2048 × 6 bytes = 12KB
- Unity Mesh overhead: ~6KB

**25 Tiles**: ~1.25MB mesh data

## Frustum Culling

### How It Works

Entities Graphics uses `RenderBounds` for frustum culling:

```
1. Camera calculates view frustum planes
2. For each entity with RenderBounds:
   ├─ Check if AABB intersects frustum
   ├─ If inside: add to render batch
   └─ If outside: skip rendering
3. Render batched entities
```

**Result**: Only visible tiles rendered, off-screen tiles skipped.

### Bounds Calculation

```csharp
// Calculate AABB from vertex positions
Bounds bounds = GeometryUtility.CalculateBounds(
    vertices.ToArray(), 
    Matrix4x4.identity
);

// Convert to RenderBounds component
EntityManager.AddComponentData(entity, new RenderBounds 
{ 
    Value = new AABB 
    { 
        Center = bounds.center, 
        Extents = bounds.extents 
    } 
});
```

**Importance**: Incorrect bounds = tiles culled when they shouldn't be!

## Shadow Casting

### Configuration

Shadows configured during component setup:

```csharp
shadowCastingMode: ShadowCastingMode.On,
receiveShadows: true,
```

**Options**:
- `On` - Cast shadows (default)
- `Off` - No shadows (performance optimization)
- `TwoSided` - Double-sided shadows
- `ShadowsOnly` - Invisible but casts shadows

### Performance Impact

**Shadow Casting On**:
- Adds shadow map render pass
- ~30% GPU cost increase
- Visible shadows improve realism

**Shadow Casting Off**:
- Faster rendering
- Acceptable for stylized or distant terrain

**Recommendation**: Keep shadows on for near tiles, off for distant tiles (future LOD feature).

## Rendering Debug Tools

Use **[Debug Tools](DEBUG_TOOLS.md)** and the **Terrain Status Inspector** (`Window → Terrain → Status Inspector`) to diagnose rendering issues in play mode.

### Terrain Not Visible — Checklist

**Check 1: Are meshes generated?**
```
Terrain Status Inspector → play-mode tile / mesh counts
Profiler → TerrainMesh.Generation marker
```

**Check 2: Does rendering system run?**
```
Console: [TerrainRendering] warnings or errors
Confirm TerrainMaterial is assigned or in Resources
```

**Check 3: Are bounds correct?**
```
Terrain Status Inspector → tiles with rendering components
Profiler → PresentationSystemGroup
```

**Check 4: Camera culling**
```
Select Main Camera
Check Culling Mask includes "Default" layer
Check Far Clip Plane > View Distance
```

### Pink Terrain (Debug Color)

**Cause**: No terrain material found, system auto-generated debug material

**Solution**:
1. Create material: `Resources/TerrainMaterial.mat`
2. Use URP Lit shader
3. Assign proper texture
4. Restart scene

### Black Terrain

**Cause**: Normals incorrect or lighting issue

**Check**:
1. Verify scene has directional light
2. Check normals in mesh (should point upward)
3. Test with unlit shader to isolate lighting

### Flickering Terrain

**Cause**: Z-fighting or bounds issues

**Solutions**:
1. Check camera near plane (should be > 0.1)
2. Verify terrain tiles don't overlap positions
3. Check RenderBounds are correct size

### Terrain Culled Incorrectly

**Cause**: RenderBounds too small or incorrect

**Debug**:
1. Select tile entity in Entity Debugger
2. Check RenderBounds.Value
3. Should encompass entire tile mesh

**Fix**: Ensure bounds calculated from all vertices, not just base.

## Custom Materials

### Creating Custom Terrain Material

**Step 1**: Create Material
```
Assets/Create → Material
Name: TerrainMaterial
Location: Assets/_App/Ace of Ages/Terrain/Resources/
```

**Step 2**: Configure Shader
```
Shader: Universal Render Pipeline/Lit
Workflow Mode: Metallic
Surface Type: Opaque
Render Face: Front
```

**Step 3**: Assign Textures
```
Base Map: Your terrain albedo texture
Normal Map: Your terrain normal map (optional)
Metallic: 0
Smoothness: 0.3-0.5
```

**Step 4**: Test
- Play scene
- Terrain should use your material
- If still pink, check file location

### Material with Triplanar Mapping

For seamless texturing without UV stretching:

```shader
// Custom shader with triplanar projection
Shader "Custom/TerrainTriplanar"
{
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}
        _TileScale ("Tile Scale", Float) = 10.0
    }
    
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            float4 TriplanarSample(float3 worldPos, float3 normal)
            {
                // Sample texture from three planes
                float4 xProj = tex2D(_BaseMap, worldPos.yz * _TileScale);
                float4 yProj = tex2D(_BaseMap, worldPos.xz * _TileScale);
                float4 zProj = tex2D(_BaseMap, worldPos.xy * _TileScale);
                
                // Blend based on normal direction
                float3 blendWeights = abs(normal);
                blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z);
                
                return xProj * blendWeights.x + 
                       yProj * blendWeights.y + 
                       zProj * blendWeights.z;
            }
            ENDHLSL
        }
    }
}
```

## Entities Graphics Integration

### Required Components

For a tile to render, it needs:

```csharp
// Transform (added by spawning system)
LocalTransform
LocalToWorld

// Mesh data (added by generation system)
VertexElement [buffer]
NormalElement [buffer]
UVElement [buffer]
IndexElement [buffer]

// Rendering (added by rendering system)
MeshReference (managed)
MaterialMeshInfo
RenderBounds
RenderFilterSettings (shared)
```

### Batching Behavior

Entities Graphics batches tiles with:
- Same material
- Same shader
- Compatible transform scales
- In view frustum

**Optimization**: All terrain tiles batch together (very efficient).

### LOD Groups

The rendering system doesn't currently implement rendering LOD (separate from physics LOD).

**Future Enhancement**: Could add mesh LOD based on distance
- Near: High poly mesh
- Far: Low poly mesh
- Transition: Smooth fade

## Render Bounds Calculation

### AABB Generation

```csharp
// Calculate min/max from vertices
float3 min = vertices[0];
float3 max = vertices[0];

foreach (var vertex in vertices)
{
    min = math.min(min, vertex);
    max = math.max(max, vertex);
}

AABB bounds = new AABB
{
    Center = (min + max) * 0.5f,
    Extents = (max - min) * 0.5f
};
```

**Important**: Bounds are in local space (relative to tile position).

### Culling Distance

**Camera Far Clip Plane** must be > View Distance!

Example:
```
View Distance: 500m
Camera Far Clip: 1000m ✅

View Distance: 500m
Camera Far Clip: 300m ❌ Tiles culled prematurely!
```

## Render Quality Settings

### Mesh Quality

Controlled by `Vertices Per Side`:
- 16 vertices: Low quality, fast
- 32 vertices: Medium quality (default)
- 64 vertices: High quality, slower
- 128 vertices: Ultra quality, very slow

**Visual Impact**: Higher = smoother terrain surface, better slopes

### Shadow Quality

Controlled by Unity Quality Settings:
- Shadow Distance: Should match or exceed view distance
- Shadow Cascades: 2-4 cascades recommended
- Shadow Resolution: High or Very High for VR

### Lighting Quality

**Recommended for Terrain**:
- Lighting Mode: Realtime
- Ambient Source: Gradient or Color
- Reflection Probes: At least one in scene

## Performance Optimization

### Reduce Draw Calls

**Problem**: Many draw calls if batching breaks

**Solutions**:
1. Ensure all tiles use SAME material
2. Keep tile scales uniform (scale = 1.0)
3. Avoid per-tile material property changes

**Check**: Open Frame Debugger (Window → Analysis → Frame Debugger)
- Look for "Entities Graphics" batches
- Should see large batches (10-20 tiles per batch)

### GPU Overdraw

**Problem**: Multiple tiles drawing on same pixels

**Solutions**:
1. Reduce view distance (fewer overlapping tiles)
2. Use occlusion culling (future enhancement)
3. Optimize camera far clip plane

### Vertex Throughput

**Problem**: Too many vertices for GPU

**Solutions**:
1. Reduce `Vertices Per Side`
2. Implement rendering LOD (near detailed, far simple)
3. Reduce view distance

## Custom Rendering Features

### Adding Vertex Colors

Modify mesh generation to include colors:

```csharp
// In TerrainMeshGenerationSystem
var colors = new NativeArray<Color32>(vertexCount, Allocator.Temp);

for (int i = 0; i < vertexCount; i++)
{
    float height = vertices[i].y;
    colors[i] = GetColorForHeight(height);
}

// In TerrainRenderingSystem
mesh.SetColors(colors);
colors.Dispose();
```

### Adding Tangents

For normal mapping support:

```csharp
// Calculate tangents from UVs
var tangents = new NativeArray<Vector4>(vertexCount, Allocator.Temp);
// ... tangent calculation logic
mesh.SetTangents(tangents);
tangents.Dispose();
```

### Multiple Materials (Advanced)

To support multiple materials per tile:

**Modify TerrainRenderingSystem**:
```csharp
// Create material array
Material[] materials = new Material[] 
{ 
    grassMaterial, 
    rockMaterial, 
    snowMaterial 
};

// Use submeshes based on height
if (vertex.y < 10f) 
    submeshIndex = 0; // Grass
else if (vertex.y < 50f) 
    submeshIndex = 1; // Rock
else 
    submeshIndex = 2; // Snow
```

**Note**: Breaks batching, reduces performance!

## Related Documentation

- **[Technical Details](TECHNICAL_DETAILS.md)** - Mesh generation algorithms
- **[Performance Optimization](PERFORMANCE.md)** - Rendering optimization strategies
- **[API Reference](API_REFERENCE.md)** - MeshReference and buffer components
- **[Troubleshooting](TROUBLESHOOTING.md)** - Solving rendering issues

---

**Back to**: [Documentation Hub](README.md)

