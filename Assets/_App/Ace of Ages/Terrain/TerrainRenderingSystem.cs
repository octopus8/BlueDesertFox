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
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class TerrainRenderingSystem : SystemBase
{
    private Material _terrainMaterial;
    private EntityQuery _newTilesQuery;

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
    }

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
                Debug.Log($"[TerrainRendering] Using material from TerrainConfigAuthoring: {_terrainMaterial.name}");
                return;
            }
        }
        
        // Fall back to loading from Resources
        _terrainMaterial = Resources.Load<Material>("TerrainMaterial");
        
        if (_terrainMaterial != null)
        {
            Debug.Log($"[TerrainRendering] Loaded material from Resources: {_terrainMaterial.name}");
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

    protected override void OnUpdate()
    {
        // Always process tiles for MeshReference (needed for tree spawning)
        // But skip actual rendering setup if renderTerrain is disabled
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        bool shouldRender = config.renderTerrain;
        
        if (shouldRender && _terrainMaterial == null)
        {
            return;
        }

        // Collect entities that need mesh creation (ZERO GC ALLOCATIONS)
        var entitiesToProcess = new NativeList<Entity>(16, Allocator.Temp);
        
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
                    entitiesToProcess.Add(entity);
                }
            }
        }
        
        // Process collected entities (structural changes allowed after iteration)
        foreach (var entity in entitiesToProcess)
        {
            var vertices = EntityManager.GetBuffer<VertexElement>(entity);
            var normals = EntityManager.GetBuffer<NormalElement>(entity);
            var uvs = EntityManager.GetBuffer<UVElement>(entity);
            var indices = EntityManager.GetBuffer<IndexElement>(entity);
            
            if (vertices.Length > 0 && indices.Length > 0)
            {
                CreateAndAssignMesh(entity, vertices, normals, uvs, indices, shouldRender);
            }
        }
        
        entitiesToProcess.Dispose();
    }

    /// <summary>
    /// Creates a Unity Mesh from buffer data using NativeArray API to avoid GC allocations.
    /// If shouldRender is false, only adds MeshReference (for tree spawning) but skips rendering setup.
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
        // Reinterpret buffers as NativeArrays directly - ZERO GC
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
        
        // Always add MeshReference (needed for tree spawning system)
        EntityManager.AddComponentData(entity, new MeshReference { mesh = mesh });
        
        // Skip rendering setup if terrain rendering is disabled
        if (!shouldRender)
        {
#if UNITY_EDITOR
            Debug.Log($"[TerrainRendering] Added MeshReference for tile {entity.Index} but skipped rendering setup (renderTerrain=false)");
#endif
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
            var renderMeshArray = new RenderMeshArray(new[] { _terrainMaterial }, new[] { mesh });
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

    protected override void OnDestroy()
    {
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











