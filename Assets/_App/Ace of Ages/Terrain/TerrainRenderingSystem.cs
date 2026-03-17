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
        // Load or create terrain material
        // Try to load from Resources first
        _terrainMaterial = Resources.Load<Material>("TerrainMaterial");
        
        if (_terrainMaterial == null)
        {
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
    }

    protected override void OnUpdate()
    {
        if (_terrainMaterial == null)
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
                CreateAndAssignMesh(entity, vertices, normals, uvs, indices);
            }
        }
        
        entitiesToProcess.Dispose();
    }

    /// <summary>
    /// Creates a Unity Mesh from buffer data and assigns it to the entity with rendering components.
    /// </summary>
    private void CreateAndAssignMesh(
        Entity entity,
        DynamicBuffer<VertexElement> vertexBuffer,
        DynamicBuffer<NormalElement> normalBuffer,
        DynamicBuffer<UVElement> uvBuffer,
        DynamicBuffer<IndexElement> indexBuffer)
    {
        // Create Unity Mesh
        Mesh mesh = new Mesh();
        mesh.name = $"TerrainTile_{entity.Index}";
        
        // Convert buffers to arrays
        Vector3[] vertices = new Vector3[vertexBuffer.Length];
        for (int i = 0; i < vertexBuffer.Length; i++)
        {
            vertices[i] = vertexBuffer[i].value;
        }
        
        Vector3[] normals = new Vector3[normalBuffer.Length];
        for (int i = 0; i < normalBuffer.Length; i++)
        {
            normals[i] = normalBuffer[i].value;
        }
        
        Vector2[] uvs = new Vector2[uvBuffer.Length];
        for (int i = 0; i < uvBuffer.Length; i++)
        {
            uvs[i] = uvBuffer[i].value;
        }
        
        int[] indices = new int[indexBuffer.Length];
        for (int i = 0; i < indexBuffer.Length; i++)
        {
            indices[i] = indexBuffer[i].value;
        }
        
        // Assign to mesh
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = indices;
        
        // Recalculate bounds
        mesh.RecalculateBounds();
        
        // Add managed MeshReference component
        EntityManager.AddComponentData(entity, new MeshReference { mesh = mesh });
        
        // Register mesh and material with EntitiesGraphicsSystem
        var entitiesGraphicsSystem = World.GetExistingSystemManaged<EntitiesGraphicsSystem>();
        if (entitiesGraphicsSystem == null)
        {
            Debug.LogError("[TerrainRendering] EntitiesGraphicsSystem not found!");
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
            Debug.LogError($"[TerrainRendering] Failed to add render components: {e.Message}\n{e.StackTrace}");
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







