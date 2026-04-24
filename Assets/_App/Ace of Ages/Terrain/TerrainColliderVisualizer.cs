using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Visualizes terrain physics colliders as colored wireframes in the Scene view.
/// Colors represent LOD levels: Green (Full Resolution), Yellow (Half Resolution), Orange (Quarter Resolution).
/// Draws the actual mesh geometry of each collider.
/// Add this MonoBehaviour to any GameObject in your scene to enable visualization.
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
    
    [Header("Info")]
    [SerializeField] private int _tilesWithColliders = 0;
    [SerializeField] private int _fullResolutionCount = 0;
    [SerializeField] private int _halfResolutionCount = 0;
    [SerializeField] private int _quarterResolutionCount = 0;
    
    private EntityManager _entityManager;
    
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
    
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || !enableVisualization)
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
        
        foreach (var entity in entities)
        {
            // Get LOD level to determine color
            var distanceData = em.GetComponentData<TerrainTileDistanceToPlayer>(entity);
            Color wireframeColor = GetColorForLOD(distanceData.lodLevel);
            
            // Get tile position
            var transform = em.GetComponentData<LocalTransform>(entity);
            float3 tilePosition = transform.Position;
            
            // Get mesh data
            var vertexBuffer = em.GetBuffer<VertexElement>(entity);
            var indexBuffer = em.GetBuffer<IndexElement>(entity);
            
            DrawColliderWireframe(vertexBuffer, indexBuffer, tilePosition, wireframeColor);
        }
        
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
    
    private void DrawColliderWireframe(
        DynamicBuffer<VertexElement> vertices,
        DynamicBuffer<IndexElement> indices,
        float3 tilePosition,
        Color color)
    {
        Gizmos.color = color;
        
        // Draw each triangle edge
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
            
            // Draw triangle edges
            Gizmos.DrawLine(v0, v1);
            Gizmos.DrawLine(v1, v2);
            Gizmos.DrawLine(v2, v0);
        }
    }
}


