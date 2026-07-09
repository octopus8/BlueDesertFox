using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;
using Material = UnityEngine.Material;

/// <summary>
/// Visualizes terrain physics colliders as solid colored triangle meshes during gameplay (VR compatible).
/// Draws the actual mesh geometry of each collider using pooled MeshRenderer components.
/// Fully compatible with Quest 3 and all VR platforms.
/// </summary>
public class TerrainColliderVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [Tooltip("Enable collider mesh visualization")]
    public bool enableVisualization = true;

    [Tooltip("Color for terrain colliders (all use full-resolution geometry)")]
    public Color colliderColor = Color.green;

    [Header("Performance (Quest 3 Optimization)")]
    [Tooltip("Maximum tiles to render per frame. Quest 2: 20, Quest 3: 40, Desktop VR: -1 (unlimited)")]
    public int maxTilesToRenderPerFrame = 40;

    [Tooltip("Maximum distance from player to render collider visualization (meters). 0 = unlimited")]
    public float maxVisualizationDistance = 500f;

    [Header("Info")]
    [SerializeField] private int _tilesWithColliders = 0;
    [SerializeField] private int _tilesRenderedLastFrame = 0;

    private EntityManager _entityManager;
    private GameObject _meshContainer;
    private List<TileMeshEntry> _meshPool = new List<TileMeshEntry>();
    private int _activeMeshes = 0;
    private Material _meshMaterial;
    private List<Vector3> _vertexScratch = new List<Vector3>();

    private sealed class TileMeshEntry
    {
        public GameObject gameObject;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Mesh mesh;
    }

    /// <summary>Creates the world-origin mesh container and the visualization material.</summary>
    private void Awake()
    {
        _meshContainer = new GameObject("ColliderVisualizationMeshes");
        // Detach from this GameObject so the world-space vertices we build are not transformed
        // again by the host transform (which may be offset from the world origin).
        _meshContainer.transform.SetParent(null);
        _meshContainer.transform.position = Vector3.zero;
        _meshContainer.transform.rotation = Quaternion.identity;
        _meshContainer.transform.localScale = Vector3.one;

        CreateMeshMaterial();
    }

    /// <summary>Destroys the pooled visualization material and the mesh container to prevent memory leaks.</summary>
    private void OnDestroy()
    {
        if (_meshMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_meshMaterial);
            else
                DestroyImmediate(_meshMaterial);
        }

        if (_meshContainer != null)
        {
            if (Application.isPlaying)
                Destroy(_meshContainer);
            else
                DestroyImmediate(_meshContainer);
        }
    }

    /// <summary>Caches the <see cref="EntityManager"/> reference and refreshes Inspector tile counters each frame.</summary>
    void Update()
    {
        if (!Application.isPlaying)
            return;

        if (World.DefaultGameObjectInjectionWorld == null)
            return;

        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        UpdateCounts();
    }

    /// <summary>Each frame, applies the current collider color to the material and calls <see cref="RenderColliderMeshes"/> to update pooled mesh renderers.</summary>
    void LateUpdate()
    {
        if (!enableVisualization || !Application.isPlaying)
        {
            HideAllMeshes();
            return;
        }

        if (_meshMaterial == null)
            return;

        if (World.DefaultGameObjectInjectionWorld == null)
            return;

        ApplyMaterialColor(colliderColor);
        RenderColliderMeshes();
    }

    /// <summary>Queries the ECS world for tiles with physics collider data and updates the Inspector-visible <c>_tilesWithColliders</c> count.</summary>
    void UpdateCounts()
    {
        var query = _entityManager.CreateEntityQuery(
            typeof(PhysicsCollider),
            typeof(TerrainTileDistanceToPlayer),
            typeof(VertexElement),
            typeof(IndexElement)
        );
        _tilesWithColliders = query.CalculateEntityCount();
        query.Dispose();
    }

    /// <summary>Creates a new URP Unlit (or fallback Unlit/Color) material for the collider mesh overlay.</summary>
    private void CreateMeshMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
        {
            Debug.LogError("[TerrainColliderVisualizer] Could not find shader for mesh rendering!");
            return;
        }

        _meshMaterial = new Material(shader);
        _meshMaterial.name = "TerrainColliderVisualizerMeshMaterial";
        _meshMaterial.hideFlags = HideFlags.HideAndDontSave;
        ApplyMaterialColor(colliderColor);
        _meshMaterial.SetFloat("_Cull", (float)CullMode.Off);
    }

    /// <summary>Sets the material's <c>_BaseColor</c> (URP) or <c>_Color</c> (legacy) property to <paramref name="color"/>.</summary>
    private void ApplyMaterialColor(Color color)
    {
        if (_meshMaterial.HasProperty("_BaseColor"))
            _meshMaterial.SetColor("_BaseColor", color);
        else if (_meshMaterial.HasProperty("_Color"))
            _meshMaterial.SetColor("_Color", color);
    }

    /// <summary>Returns the next available <see cref="TileMeshEntry"/> from the pool, activating it, or creates and adds a new one if the pool is exhausted.</summary>
    private TileMeshEntry GetOrCreateMeshEntry()
    {
        if (_activeMeshes < _meshPool.Count)
        {
            var existing = _meshPool[_activeMeshes];
            existing.gameObject.SetActive(true);
            _activeMeshes++;
            return existing;
        }

        var go = new GameObject($"TileMesh{_meshPool.Count}");
        go.transform.SetParent(_meshContainer.transform);
        go.transform.localPosition = Vector3.zero;

        var meshFilter = go.AddComponent<MeshFilter>();
        var meshRenderer = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh { name = $"ColliderVisMesh{_meshPool.Count}" };
        mesh.hideFlags = HideFlags.HideAndDontSave;
        mesh.MarkDynamic();

        meshFilter.sharedMesh = mesh;
        meshRenderer.sharedMaterial = _meshMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        var newEntry = new TileMeshEntry
        {
            gameObject = go,
            meshFilter = meshFilter,
            meshRenderer = meshRenderer,
            mesh = mesh
        };

        _meshPool.Add(newEntry);
        _activeMeshes++;
        return newEntry;
    }

    /// <summary>Deactivates all pooled tile mesh GameObjects and resets the active-mesh counter to zero.</summary>
    private void HideAllMeshes()
    {
        for (int i = 0; i < _meshPool.Count; i++)
            _meshPool[i].gameObject.SetActive(false);

        _activeMeshes = 0;
    }

    /// <summary>
    /// Queries all terrain tiles with prepared collider vertex/triangle data, filters by distance
    /// (up to <see cref="maxVisualizationDistance"/>), and uploads the geometry to pooled
    /// <see cref="MeshRenderer"/> GameObjects via <see cref="GetOrCreateMeshEntry"/>.
    /// </summary>
    private void RenderColliderMeshes()
    {
        _activeMeshes = 0;

        var em = World.DefaultGameObjectInjectionWorld.EntityManager;

        var query = em.CreateEntityQuery(
            ComponentType.ReadOnly<PhysicsCollider>(),
            ComponentType.ReadOnly<TerrainTileDistanceToPlayer>(),
            ComponentType.ReadOnly<VertexElement>(),
            ComponentType.ReadOnly<IndexElement>(),
            ComponentType.ReadOnly<LocalTransform>()
        );

        var entities = query.ToEntityArray(Allocator.Temp);

        float3 playerPos = float3.zero;
        bool hasPlayerPos = false;

        var playerQuery = em.CreateEntityQuery(typeof(PlayerTransformReference));
        if (!playerQuery.IsEmpty)
        {
            var playerRef = playerQuery.GetSingleton<PlayerTransformReference>();
            if (playerRef.playerTransform != null)
            {
                playerPos = playerRef.playerTransform.position;
                hasPlayerPos = true;
            }
        }

        int tilesRendered = 0;

        foreach (var entity in entities)
        {
            if (maxTilesToRenderPerFrame > 0 && tilesRendered >= maxTilesToRenderPerFrame)
                break;

            var tileTransform = em.GetComponentData<LocalTransform>(entity);
            float3 tilePosition = tileTransform.Position;

            if (hasPlayerPos && maxVisualizationDistance > 0)
            {
                float distToPlayer = math.distance(tilePosition, playerPos);
                if (distToPlayer > maxVisualizationDistance)
                    continue;
            }

            var vertexBuffer = em.GetBuffer<VertexElement>(entity);
            var indexBuffer = em.GetBuffer<IndexElement>(entity);

            var entry = GetOrCreateMeshEntry();
            UpdateTileMesh(entry.mesh, vertexBuffer, indexBuffer, tilePosition);

            tilesRendered++;
        }

        _tilesRenderedLastFrame = tilesRendered;
        entities.Dispose();

        for (int i = _activeMeshes; i < _meshPool.Count; i++)
            _meshPool[i].gameObject.SetActive(false);
    }

    /// <summary>Uploads the collider vertex and triangle buffers from the ECS entity to the given Unity <see cref="Mesh"/>, applying the tile's world-space position offset.</summary>
    private void UpdateTileMesh(
        Mesh mesh,
        DynamicBuffer<VertexElement> vertices,
        DynamicBuffer<IndexElement> indices,
        float3 tilePosition)
    {
        if (vertices.Length == 0 || indices.Length == 0)
        {
            mesh.Clear();
            return;
        }

        if (_vertexScratch.Capacity < vertices.Length)
            _vertexScratch.Capacity = vertices.Length;

        _vertexScratch.Clear();
        for (int i = 0; i < vertices.Length; i++)
            _vertexScratch.Add((Vector3)(tilePosition + vertices[i].value));

        mesh.SetVertices(_vertexScratch);
        mesh.SetIndices(indices.Reinterpret<int>().AsNativeArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateBounds();
    }
}

