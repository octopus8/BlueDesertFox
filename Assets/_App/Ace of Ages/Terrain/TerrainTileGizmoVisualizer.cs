using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Add this MonoBehaviour to a GameObject in your scene to visualize terrain tiles with Gizmos.
/// Useful for debugging when tiles aren't rendering.
/// </summary>
public class TerrainTileGizmoVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [Tooltip("Draw wireframe boxes for each tile")]
    public bool drawTileBounds = true;
    
    [Tooltip("Draw tile grid coordinates as text")]
    public bool drawGridCoordinates = true;
    
    [Tooltip("Color for tile bounds")]
    public Color tileColor = Color.green;
    
    [Tooltip("Color for tiles with mesh data")]
    public Color tileWithMeshColor = Color.yellow;
    
    [Tooltip("Color for tiles with rendering")]
    public Color tileWithRenderingColor = Color.cyan;
    
    [Header("Info")]
    [SerializeField] private int _totalTiles = 0;
    [SerializeField] private int _tilesWithMesh = 0;
    [SerializeField] private int _tilesWithRendering = 0;
    
    private EntityManager _entityManager;
    
    void Update()
    {
        if (World.DefaultGameObjectInjectionWorld == null)
            return;
            
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        // Update counts for inspector display
        UpdateCounts();
    }
    
    void UpdateCounts()
    {
        var query = _entityManager.CreateEntityQuery(typeof(TerrainTile));
        _totalTiles = query.CalculateEntityCount();
        
        var meshQuery = _entityManager.CreateEntityQuery(typeof(TerrainTile), typeof(VertexElement));
        _tilesWithMesh = meshQuery.CalculateEntityCount();
        
        var renderQuery = _entityManager.CreateEntityQuery(typeof(TerrainTile), typeof(MaterialMeshInfo));
        _tilesWithRendering = renderQuery.CalculateEntityCount();
    }
    
    void OnDrawGizmos()
    {
        if (!Application.isPlaying)
            return;
            
        if (World.DefaultGameObjectInjectionWorld == null)
            return;
        
        var em = World.DefaultGameObjectInjectionWorld.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile));
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        // Get tile config if available
        float tileSize = 100f;
        var configQuery = em.CreateEntityQuery(typeof(TerrainTileConfig));
        if (configQuery.CalculateEntityCount() > 0)
        {
            var configs = configQuery.ToComponentDataArray<TerrainTileConfig>(Unity.Collections.Allocator.Temp);
            if (configs.Length > 0)
            {
                tileSize = configs[0].tileSize;
            }
            configs.Dispose();
        }
        
        foreach (var entity in entities)
        {
            var tile = em.GetComponentData<TerrainTile>(entity);
            
            if (!drawTileBounds)
                continue;
            
            // Determine color based on tile state
            Color color = tileColor;
            if (em.HasComponent<MaterialMeshInfo>(entity))
            {
                color = tileWithRenderingColor;
            }
            else if (em.HasComponent<VertexElement>(entity))
            {
                color = tileWithMeshColor;
            }
            
            Gizmos.color = color;
            
            // Get tile position
            float3 position = float3.zero;
            if (em.HasComponent<LocalTransform>(entity))
            {
                position = em.GetComponentData<LocalTransform>(entity).Position;
            }
            else
            {
                // Calculate from grid coordinate if no transform
                position = new float3(
                    tile.gridCoordinate.x * tileSize,
                    0,
                    tile.gridCoordinate.y * tileSize
                );
            }
            
            // Draw tile bounds as wireframe cube
            Vector3 size = new Vector3(tileSize, 20f, tileSize); // Assume max height of 20
            Vector3 center = new Vector3(position.x + tileSize * 0.5f, 10f, position.z + tileSize * 0.5f);
            Gizmos.DrawWireCube(center, size);
            
            // Draw grid coordinates
            if (drawGridCoordinates)
            {
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(
                    center + Vector3.up * 12f,
                    $"({tile.gridCoordinate.x}, {tile.gridCoordinate.y})",
                    new GUIStyle
                    {
                        normal = new GUIStyleState { textColor = color },
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 10
                    }
                );
                #endif
            }
            
            // If tile has mesh data, draw the actual bounds
            if (em.HasComponent<RenderBounds>(entity))
            {
                var renderBounds = em.GetComponentData<RenderBounds>(entity);
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(
                    position + renderBounds.Value.Center,
                    (Vector3)renderBounds.Value.Extents * 2f
                );
            }
        }
        
        entities.Dispose();
    }
}

