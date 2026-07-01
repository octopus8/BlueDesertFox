using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
/// <summary>
/// System that converts ECS mesh data to Unity Mesh instances and sets up rendering.
/// Must run on the main thread due to managed Mesh object access.
/// Uses frame budgeting to prevent performance spikes on Quest.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class TerrainRenderingSystem : SystemBase
{
    private Material _terrainMaterial;
    private EntityQuery _newTilesQuery;
    private NativeQueue<Entity> _pendingMeshCreation;
    private NativeHashSet<Entity> _queuedEntities;
    
    // ✅ Cached arrays to avoid GC allocations in CreateAndAssignMesh
    private Material[] _cachedMaterialArray;
    private Mesh[] _cachedMeshArray;
    
    /// <summary>
    /// Builds the tile entity query, allocates the pending-mesh queue and deduplication set,
    /// initialises cached material/mesh arrays for zero-GC mesh creation, and registers the
    /// <see cref="TerrainTileConfig"/> requirement.
    /// </summary>
    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileConfig>();
        // Query for tiles that have mesh data but no MeshReference yet
        _newTilesQuery = GetEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<VertexElement>(),
            ComponentType.ReadOnly<IndexElement>(),
            ComponentType.Exclude<MeshReference>()
        );
        _pendingMeshCreation = new NativeQueue<Entity>(Allocator.Persistent);
        _queuedEntities = new NativeHashSet<Entity>(64, Allocator.Persistent);
        
        // ✅ Initialize cached arrays once - reused for all mesh creation (zero GC)
        _cachedMaterialArray = new Material[1];
        _cachedMeshArray = new Mesh[1];
    }
    
    /// <summary>
    /// Resolves the terrain material from the <c>TerrainMaterialReference</c> component on
    /// first run, falling back to a Resources lookup and then to auto-generated materials.
    /// </summary>
    protected override void OnStartRunning()
    {
        // Try to get material from TerrainMaterialReference component first
        var configQuery = GetEntityQuery(typeof(TerrainMaterialReference));
        if (configQuery.CalculateEntityCount() > 0)
        {
            var configEntity = configQuery.GetSingletonEntity();
            var materialRef = EntityManager.GetComponentObject<TerrainMaterialReference>(configEntity);
            if (materialRef != null && materialRef.material != null)
            {
                _terrainMaterial = materialRef.material;
                return;
            }
        }
        // Fall back to loading from Resources
        _terrainMaterial = Resources.Load<Material>("TerrainMaterial");
        if (_terrainMaterial != null)
        {
            return;
        }
        // Last resort: create a debug material
        Debug.LogWarning("[TerrainRendering] No material assigned in TerrainConfigAuthoring and no TerrainMaterial found in Resources. Creating debug material.");
        // Try URP Lit shader first
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            // Fallback to standard shader
            shader = Shader.Find("Standard");
            if (shader == null)
            {
                // Last resort: unlit color
                shader = Shader.Find("Unlit/Color");
            }
        }
        if (shader != null)
        {
            _terrainMaterial = new Material(shader);
            _terrainMaterial.name = "TerrainMaterial_Generated";
            // Set a bright debug color so we can see if material is working
            if (shader.name.Contains("Universal Render Pipeline"))
            {
                _terrainMaterial.SetColor("_BaseColor", new Color(1f, 0.5f, 0.8f, 1f)); // Pink for debugging
                _terrainMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            }
            else if (shader.name == "Standard")
            {
                _terrainMaterial.SetColor("_Color", new Color(1f, 0.5f, 0.8f, 1f));
            }
            else
            {
                _terrainMaterial.SetColor("_Color", new Color(1f, 0.5f, 0.8f, 1f));
            }
        }
        else
        {
            Debug.LogError("[TerrainRendering] Failed to find any suitable shader!");
        }
    }
    /// <summary>
    /// Queues tiles with generated vertex data but no <c>MeshReference</c>, then creates Unity
    /// <see cref="Mesh"/> instances and assigns <c>RenderMeshArray</c> components within the
    /// per-frame budget. Always processes <c>MeshReference</c> creation for tree-spawning
    /// correctness even when visual rendering is disabled.
    /// </summary>
    protected override void OnUpdate()
    {
        // Always process tiles for MeshReference (needed for static object spawning)
        // But skip actual rendering setup if renderTerrain is disabled
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        bool shouldRender = config.renderTerrain;
        if (shouldRender && _terrainMaterial == null)
        {
            return;
        }
        // Add new tiles to the queue
        foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>()
            .WithAll<VertexElement>()
            .WithAll<NormalElement>()
            .WithAll<UVElement>()
            .WithAll<IndexElement>()
            .WithNone<MeshReference>()
            .WithEntityAccess())
        {
            if (tile.ValueRO.meshGenerated)
            {
                var vertices = EntityManager.GetBuffer<VertexElement>(entity);
                if (vertices.Length > 0)
                {
                    // Only add if not already queued (prevents duplicates!)
                    if (_queuedEntities.Add(entity))
                    {
                        _pendingMeshCreation.Enqueue(entity);
                    }
                }
            }
        }
        // Process up to maxMeshesPerFrame (use same budget as colliders - typically 3 for VR)
        int maxMeshesPerFrame = math.max(1, config.maxCollidersCreatedPerFrame);
        int meshesCreatedThisFrame = 0;
        while (_pendingMeshCreation.Count > 0 && meshesCreatedThisFrame < maxMeshesPerFrame)
        {
            Entity entity = _pendingMeshCreation.Dequeue();
            
            // Remove from queued set
            _queuedEntities.Remove(entity);
            
            // Verify entity still exists and needs processing
            if (!EntityManager.Exists(entity))
                continue;
            if (EntityManager.HasComponent<MeshReference>(entity))
                continue;
            var vertices = EntityManager.GetBuffer<VertexElement>(entity);
            var normals = EntityManager.GetBuffer<NormalElement>(entity);
            var uvs = EntityManager.GetBuffer<UVElement>(entity);
            var indices = EntityManager.GetBuffer<IndexElement>(entity);
            if (vertices.Length > 0 && indices.Length > 0)
            {
                CreateAndAssignMesh(entity, vertices, normals, uvs, indices, shouldRender);
                meshesCreatedThisFrame++;
            }
        }
    }
    /// <summary>
    /// Creates a Unity Mesh from buffer data using NativeArray API to avoid GC allocations.
    /// If shouldRender is false, only adds MeshReference (for static object spawning) but skips rendering setup.
    /// </summary>
    private void CreateAndAssignMesh(
        Entity entity,
        DynamicBuffer<VertexElement> vertexBuffer,
        DynamicBuffer<NormalElement> normalBuffer,
        DynamicBuffer<UVElement> uvBuffer,
        DynamicBuffer<IndexElement> indexBuffer,
        bool shouldRender)
    {
        // Create Unity Mesh
        Mesh mesh = new Mesh();
        mesh.name = $"TerrainTile_{entity.Index}";
        // Use NativeArray API to avoid GC allocations (Unity 2020.1+)
        // Reinterpret buffers as NativeArrays directly - ZERO GC for data copy
        var verticesNative = vertexBuffer.Reinterpret<float3>().AsNativeArray();
        var normalsNative = normalBuffer.Reinterpret<float3>().AsNativeArray();
        var uvsNative = uvBuffer.Reinterpret<float2>().AsNativeArray();
        var indicesNative = indexBuffer.Reinterpret<int>().AsNativeArray();
        // Set mesh data from NativeArrays (ZERO GC ALLOCATIONS)
        mesh.SetVertices(verticesNative);
        mesh.SetNormals(normalsNative);
        mesh.SetUVs(0, uvsNative);
        mesh.SetIndices(indicesNative, MeshTopology.Triangles, 0);
        // Recalculate bounds
        mesh.RecalculateBounds();
        // Always add MeshReference (needed for static object spawning system)
        EntityManager.AddComponentData(entity, new MeshReference { mesh = mesh });
        // Skip rendering setup if terrain rendering is disabled
        if (!shouldRender)
        {
            return;
        }
        // Register mesh and material with EntitiesGraphicsSystem
        var entitiesGraphicsSystem = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        if (entitiesGraphicsSystem == null)
        {
            #if UNITY_EDITOR
            Debug.LogError("[TerrainRendering] EntitiesGraphicsSystem not found!");
            #endif
            return;
        }
        // Register the mesh and material to get proper IDs
        var registeredMesh = entitiesGraphicsSystem.RegisterMesh(mesh);
        var registeredMaterial = entitiesGraphicsSystem.RegisterMaterial(_terrainMaterial);
        // Add rendering components using RenderMeshUtility
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: ShadowCastingMode.On,
            receiveShadows: true,
            layer: 0,
            renderingLayerMask: 1
        );
        try
        {
            // ✅ Use cached arrays - ZERO GC allocations (arrays created once in OnCreate)
            _cachedMaterialArray[0] = _terrainMaterial;
            _cachedMeshArray[0] = mesh;
            
            var renderMeshArray = new RenderMeshArray(_cachedMaterialArray, _cachedMeshArray);
            var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);
            RenderMeshUtility.AddComponents(
                entity,
                EntityManager,
                renderMeshDescription,
                renderMeshArray,
                materialMeshInfo
            );
        }
        catch (System.Exception e)
        {
            #if UNITY_EDITOR
            Debug.LogError($"[TerrainRendering] Failed to add render components: {e.Message}\n{e.StackTrace}");
            #endif
            return;
        }
        // Ensure LocalToWorld is present
        if (!EntityManager.HasComponent<LocalToWorld>(entity))
        {
            EntityManager.AddComponent<LocalToWorld>(entity);
        }
    }
    /// <summary>
    /// Disposes native collections and destroys all managed <see cref="Mesh"/> instances owned by
    /// terrain tile entities to prevent Unity memory leaks on domain reload or scene teardown.
    /// </summary>
    protected override void OnDestroy()
    {
        // Dispose queue and hash set
        if (_pendingMeshCreation.IsCreated)
            _pendingMeshCreation.Dispose();
        if (_queuedEntities.IsCreated)
            _queuedEntities.Dispose();
        
        // Clean up meshes - OnDestroy called once per session, GC allocation acceptable here
        var query = GetEntityQuery(ComponentType.ReadOnly<MeshReference>());
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        foreach (var entity in entities)
        {
            var meshRef = EntityManager.GetComponentData<MeshReference>(entity);
            if (meshRef.mesh != null)
            {
                Object.Destroy(meshRef.mesh);
            }
        }
        entities.Dispose();
    }
}
