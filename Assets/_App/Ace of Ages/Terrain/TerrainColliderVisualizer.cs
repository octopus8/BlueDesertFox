using System;
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
/// Visualizes terrain physics colliders as colored wireframes during gameplay (VR compatible).
/// Draws the actual mesh geometry of each collider using Unity LineRenderer components.
/// All terrain uses full-resolution colliders - displayed in green.
/// Fully compatible with Quest 3 and all VR platforms.
/// </summary>
public class TerrainColliderVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [Tooltip("Enable collider wireframe visualization")]
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
    private GameObject _lineRendererContainer;
    private List<LineRenderer> _lineRendererPool = new List<LineRenderer>();
    private int _activeLineRenderers = 0;
    private Material _lineMaterial;

    private void Awake()
    {
        // Create container for line renderers
        _lineRendererContainer = new GameObject("ColliderVisualizationLines");
        _lineRendererContainer.transform.SetParent(transform);
        _lineRendererContainer.transform.localPosition = Vector3.zero;
        
        // Create material for line rendering
        CreateLineMaterial();
    }

    private void OnDestroy()
    {
        // Cleanup material
        if (_lineMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_lineMaterial);
            else
                DestroyImmediate(_lineMaterial);
        }
        
        // Cleanup line renderer container
        if (_lineRendererContainer != null)
        {
            if (Application.isPlaying)
                Destroy(_lineRendererContainer);
            else
                DestroyImmediate(_lineRendererContainer);
        }
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;
            
        if (World.DefaultGameObjectInjectionWorld == null)
            return;
            
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        // Update counts for inspector display
        UpdateCounts();
    }
    
    void LateUpdate()
    {
        if (!enableVisualization || !Application.isPlaying)
        {
            // Hide all line renderers when disabled
            HideAllLineRenderers();
            return;
        }
            
        if (_lineMaterial == null)
            return;
            
        if (World.DefaultGameObjectInjectionWorld == null)
            return;
        
        // Build line visualization
        RenderColliderLines();
    }
    
    void UpdateCounts()
    {
        var query = _entityManager.CreateEntityQuery(
            typeof(PhysicsCollider),
            typeof(TerrainTileDistanceToPlayer)
        );
        _tilesWithColliders = query.CalculateEntityCount();
        query.Dispose();
    }
    
    private void CreateLineMaterial()
    {
        // Create a simple unlit material with vertex colors for Quest 3 compatibility
        Shader shader = Shader.Find("Sprites/Default");
        
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }
        
        if (shader == null)
        {
            Debug.LogError("[TerrainColliderVisualizer] Could not find shader for line rendering!");
            return;
        }
        
        _lineMaterial = new Material(shader);
        _lineMaterial.name = "TerrainColliderVisualizerLineMaterial";
        _lineMaterial.hideFlags = HideFlags.HideAndDontSave;
    }
    
    private LineRenderer GetOrCreateLineRenderer()
    {
        // Reuse existing line renderer if available
        if (_activeLineRenderers < _lineRendererPool.Count)
        {
            var lr = _lineRendererPool[_activeLineRenderers];
            lr.gameObject.SetActive(true);
            _activeLineRenderers++;
            return lr;
        }
        
        // Create new line renderer
        var go = new GameObject($"Line{_lineRendererPool.Count}");
        go.transform.SetParent(_lineRendererContainer.transform);
        go.transform.localPosition = Vector3.zero;
        
        var lineRenderer = go.AddComponent<LineRenderer>();
        lineRenderer.material = _lineMaterial;
        lineRenderer.startWidth = 0.05f; // 5cm lines - visible in VR
        lineRenderer.endWidth = 0.05f;
        lineRenderer.numCapVertices = 0;
        lineRenderer.numCornerVertices = 0;
        lineRenderer.useWorldSpace = true;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        
        _lineRendererPool.Add(lineRenderer);
        _activeLineRenderers++;
        return lineRenderer;
    }
    
    private void HideAllLineRenderers()
    {
        for (int i = 0; i < _lineRendererPool.Count; i++)
        {
            _lineRendererPool[i].gameObject.SetActive(false);
        }
        _activeLineRenderers = 0;
    }
    
    private void RenderColliderLines()
    {
        // Reset active line renderer count
        _activeLineRenderers = 0;
        
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        // Query entities with physics colliders
        var query = em.CreateEntityQuery(
            ComponentType.ReadOnly<PhysicsCollider>(),
            ComponentType.ReadOnly<TerrainTileDistanceToPlayer>(),
            ComponentType.ReadOnly<VertexElement>(),
            ComponentType.ReadOnly<IndexElement>(),
            ComponentType.ReadOnly<LocalTransform>()
        );
        
        var entities = query.ToEntityArray(Allocator.Temp);
        
        // Get player position for distance culling
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
            // Check frame budget
            if (maxTilesToRenderPerFrame > 0 && tilesRendered >= maxTilesToRenderPerFrame)
                break;
            
            // Get tile position
            var tileTransform = em.GetComponentData<LocalTransform>(entity);
            float3 tilePosition = tileTransform.Position;
            
            // Distance culling
            if (hasPlayerPos && maxVisualizationDistance > 0)
            {
                float distToPlayer = math.distance(tilePosition, playerPos);
                if (distToPlayer > maxVisualizationDistance)
                    continue;
            }
            
            // Get mesh data
            var vertexBuffer = em.GetBuffer<VertexElement>(entity);
            var indexBuffer = em.GetBuffer<IndexElement>(entity);
            
            // Draw wireframe lines using LineRenderers (all terrain uses same color)
            DrawWireframeWithLineRenderers(vertexBuffer, indexBuffer, tilePosition, colliderColor);
            
            tilesRendered++;
        }
        
        _tilesRenderedLastFrame = tilesRendered;
        
        entities.Dispose();
        
        // Hide unused line renderers
        for (int i = _activeLineRenderers; i < _lineRendererPool.Count; i++)
        {
            _lineRendererPool[i].gameObject.SetActive(false);
        }
    }
    
    private void DrawWireframeWithLineRenderers(
        DynamicBuffer<VertexElement> vertices,
        DynamicBuffer<IndexElement> indices,
        float3 tilePosition,
        Color color)
    {
        // Draw each triangle edge as a line
        // Indices are stored as sequential triplets: [0,1,2], [3,4,5], etc.
        for (int i = 0; i < indices.Length; i += 3)
        {
            if (i + 2 >= indices.Length)
                break;
            
            int idx0 = indices[i].value;
            int idx1 = indices[i + 1].value;
            int idx2 = indices[i + 2].value;
            
            // Safety check
            if (idx0 >= vertices.Length || idx1 >= vertices.Length || idx2 >= vertices.Length)
                continue;
            
            Vector3 v0 = (Vector3)(tilePosition + vertices[idx0].value);
            Vector3 v1 = (Vector3)(tilePosition + vertices[idx1].value);
            Vector3 v2 = (Vector3)(tilePosition + vertices[idx2].value);
            
            // Draw 3 edges of the triangle
            DrawLine(v0, v1, color);
            DrawLine(v1, v2, color);
            DrawLine(v2, v0, color);
        }
    }
    
    private void DrawLine(Vector3 start, Vector3 end, Color color)
    {
        var lr = GetOrCreateLineRenderer();
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.startColor = color;
        lr.endColor = color;
    }
}




