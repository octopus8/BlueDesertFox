using System;
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
/// Colors represent LOD levels: Green (Full Resolution), Yellow (Half Resolution), Orange (Quarter Resolution).
/// Draws the actual mesh geometry of each collider using GL immediate mode rendering.
/// Works in VR on Quest 3 and all platforms.
/// </summary>
public class TerrainColliderVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [Tooltip("Enable collider wireframe visualization")]
    public bool enableVisualization = true;
    
    [Header("LOD Colors")]
    [Tooltip("Color for full-resolution colliders (all vertices)")]
    public Color fullResolutionColor = Color.green;
    
    [Tooltip("Color for half-resolution colliders (every 2nd vertex)")]
    public Color halfResolutionColor = Color.yellow;
    
    [Tooltip("Color for quarter-resolution colliders (every 4th vertex)")]
    public Color quarterResolutionColor = new Color(1f, 0.5f, 0f); // Orange
    
    [Header("Performance (Quest 3 Optimization)")]
    [Tooltip("Maximum tiles to render per frame. Quest 2: 20, Quest 3: 40, Desktop VR: -1 (unlimited)")]
    public int maxTilesToRenderPerFrame = 40;
    
    [Tooltip("Maximum distance from player to render collider visualization (meters). 0 = unlimited")]
    public float maxVisualizationDistance = 500f;
    
    [Header("Info")]
    [SerializeField] private int _tilesWithColliders = 0;
    [SerializeField] private int _fullResolutionCount = 0;
    [SerializeField] private int _halfResolutionCount = 0;
    [SerializeField] private int _quarterResolutionCount = 0;
    [SerializeField] private int _tilesRenderedLastFrame = 0;
    
    private EntityManager _entityManager;
    private Material _lineMaterial;

    private void Awake()
    {
        // Create material for GL rendering
        CreateLineMaterial();
    }

    private void OnEnable()
    {
        // Subscribe to render pipeline events for runtime rendering
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        // Unsubscribe from render pipeline events
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
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
    
    void UpdateCounts()
    {
        var query = _entityManager.CreateEntityQuery(
            typeof(PhysicsCollider),
            typeof(TerrainTileDistanceToPlayer)
        );
        _tilesWithColliders = query.CalculateEntityCount();
        
        // Count by LOD level
        _fullResolutionCount = 0;
        _halfResolutionCount = 0;
        _quarterResolutionCount = 0;
        
        var entities = query.ToEntityArray(Allocator.Temp);
        foreach (var entity in entities)
        {
            if (_entityManager.HasComponent<TerrainTileDistanceToPlayer>(entity))
            {
                var distanceData = _entityManager.GetComponentData<TerrainTileDistanceToPlayer>(entity);
                switch (distanceData.lodLevel)
                {
                    case TerrainPhysicsLODLevel.FullResolution:
                        _fullResolutionCount++;
                        break;
                    case TerrainPhysicsLODLevel.HalfResolution:
                        _halfResolutionCount++;
                        break;
                    case TerrainPhysicsLODLevel.QuarterResolution:
                        _quarterResolutionCount++;
                        break;
                }
            }
        }
        entities.Dispose();
    }
    
    private void CreateLineMaterial()
    {
        // Try to find the Internal-Colored shader (works on all platforms including VR)
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        
        if (shader == null)
        {
            // Fallback to Unlit/Color if Internal-Colored not found
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
        
        // Configure material for proper rendering
        _lineMaterial.SetInt("_ZWrite", 0); // No depth writing
        _lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual); // Proper depth testing for VR
        _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off); // Draw both sides
    }
    
    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!enableVisualization || !Application.isPlaying)
            return;
            
        if (_lineMaterial == null)
            return;
            
        if (World.DefaultGameObjectInjectionWorld == null)
            return;
        
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
        
        // Apply material for GL rendering
        _lineMaterial.SetPass(0);
        
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity); // Use world space
        GL.Begin(GL.LINES);
        
        int tilesRendered = 0;
        
        foreach (var entity in entities)
        {
            // Check frame budget
            if (maxTilesToRenderPerFrame > 0 && tilesRendered >= maxTilesToRenderPerFrame)
                break;
            
            // Get tile position
            var transform = em.GetComponentData<LocalTransform>(entity);
            float3 tilePosition = transform.Position;
            
            // Distance culling
            if (hasPlayerPos && maxVisualizationDistance > 0)
            {
                float distToPlayer = math.distance(tilePosition, playerPos);
                if (distToPlayer > maxVisualizationDistance)
                    continue;
            }
            
            // Get LOD level to determine color
            var distanceData = em.GetComponentData<TerrainTileDistanceToPlayer>(entity);
            Color wireframeColor = GetColorForLOD(distanceData.lodLevel);
            
            // Get mesh data
            var vertexBuffer = em.GetBuffer<VertexElement>(entity);
            var indexBuffer = em.GetBuffer<IndexElement>(entity);
            
            // Draw wireframe using GL
            DrawColliderWireframeGL(vertexBuffer, indexBuffer, tilePosition, wireframeColor);
            
            tilesRendered++;
        }
        
        GL.End();
        GL.PopMatrix();
        
        _tilesRenderedLastFrame = tilesRendered;
        
        entities.Dispose();
    }
    
    private Color GetColorForLOD(TerrainPhysicsLODLevel lodLevel)
    {
        switch (lodLevel)
        {
            case TerrainPhysicsLODLevel.FullResolution:
                return fullResolutionColor;
            case TerrainPhysicsLODLevel.HalfResolution:
                return halfResolutionColor;
            case TerrainPhysicsLODLevel.QuarterResolution:
                return quarterResolutionColor;
            default:
                return Color.gray;
        }
    }
    
    private void DrawColliderWireframeGL(
        DynamicBuffer<VertexElement> vertices,
        DynamicBuffer<IndexElement> indices,
        float3 tilePosition,
        Color color)
    {
        GL.Color(color);
        
        // Draw each triangle edge using GL.LINES mode
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
            
            // Draw triangle edges as lines
            GL.Vertex3(v0.x, v0.y, v0.z);
            GL.Vertex3(v1.x, v1.y, v1.z);
            
            GL.Vertex3(v1.x, v1.y, v1.z);
            GL.Vertex3(v2.x, v2.y, v2.z);
            
            GL.Vertex3(v2.x, v2.y, v2.z);
            GL.Vertex3(v0.x, v0.y, v0.z);
        }
    }
}


