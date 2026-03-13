using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Creates a simple test cube using ECS rendering to verify Entities Graphics is working.
/// If this cube renders, the terrain should too. If not, there's a deeper Entities Graphics issue.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class TestECSRenderingSystem : SystemBase
{
    private bool _testCreated = false;

    protected override void OnUpdate()
    {
        if (_testCreated)
        {
            // Only run once
            Enabled = false;
            return;
        }

        _testCreated = true;

        Debug.Log("[TestECSRendering] Creating test cube at position (10, 2, 10)...");

        // Create a simple cube mesh
        Mesh cubeMesh = CreateCubeMesh();
        
        // Load or create material
        Material testMaterial = Resources.Load<Material>("TerrainMaterial");
        if (testMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                testMaterial = new Material(shader);
                testMaterial.name = "TestMaterial";
                testMaterial.SetColor("_BaseColor", Color.red);
                Debug.Log("[TestECSRendering] Created red test material");
            }
            else
            {
                Debug.LogError("[TestECSRendering] Cannot find URP Lit shader!");
                return;
            }
        }
        else
        {
            Debug.Log("[TestECSRendering] Using TerrainMaterial for test");
        }

        // Create entity
        Entity testEntity = EntityManager.CreateEntity();
        EntityManager.SetName(testEntity, "TestRenderCube");

        // Add transform
        EntityManager.AddComponentData(testEntity, new LocalTransform
        {
            Position = new float3(10, 2, 10),
            Rotation = quaternion.identity,
            Scale = 2.0f
        });

        // Add rendering components
        var renderMeshDescription = new RenderMeshDescription(
            shadowCastingMode: UnityEngine.Rendering.ShadowCastingMode.On,
            receiveShadows: true,
            layer: 0,
            renderingLayerMask: 1
        );

        var renderMeshArray = new RenderMeshArray(new[] { testMaterial }, new[] { cubeMesh });
        var materialMeshInfo = MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0);

        RenderMeshUtility.AddComponents(
            testEntity,
            EntityManager,
            renderMeshDescription,
            renderMeshArray,
            materialMeshInfo
        );

        Debug.Log("[TestECSRendering] ✓ Test cube created");
        Debug.Log("[TestECSRendering] If you see a red/green cube at (10, 2, 10), Entities Graphics is working!");
        Debug.Log("[TestECSRendering] If you don't see the cube, there's a fundamental Entities Graphics issue.");

        // Disable this system after creation
        Enabled = false;
    }

    private Mesh CreateCubeMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "TestCube";

        // Cube vertices
        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f),
            new Vector3(-0.5f,  0.5f,  0.5f)
        };

        // Cube triangles
        int[] triangles = new int[]
        {
            0, 2, 1, 0, 3, 2, // Front
            1, 2, 6, 1, 6, 5, // Right
            4, 5, 6, 4, 6, 7, // Back
            0, 7, 3, 0, 4, 7, // Left
            3, 7, 6, 3, 6, 2, // Top
            0, 1, 5, 0, 5, 4  // Bottom
        };

        // Normals (simple, per-face)
        Vector3[] normals = new Vector3[vertices.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = vertices[i].normalized;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        return mesh;
    }
}


