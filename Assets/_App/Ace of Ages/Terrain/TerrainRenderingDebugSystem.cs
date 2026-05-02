using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Debug system to inspect terrain tile entities and their rendering components.
/// Add this temporarily to diagnose rendering issues.
/// Enable/disable via TerrainConfigAuthoring.enableRenderingDebug flag.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(TerrainRenderingSystem))]
public partial class TerrainRenderingDebugSystem : SystemBase
{
    private double _lastLogTime;
   
    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileConfig>();
    }
    
    protected override void OnUpdate()
    {
        // Early exit if debug logging is disabled
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        if (!config.enableRenderingDebug)
            return;
        
        // Log every 10 seconds
        if (SystemAPI.Time.ElapsedTime - _lastLogTime < 10.0)
            return;
            
        _lastLogTime = SystemAPI.Time.ElapsedTime;
        
        // Count terrain tiles
        var tileQuery = GetEntityQuery(ComponentType.ReadOnly<TerrainTile>());
        int totalTiles = tileQuery.CalculateEntityCount();
        
        if (totalTiles == 0)
        {
            Debug.Log("[TerrainDebug] No terrain tiles found");
            return;
        }
        
        Debug.Log($"[TerrainDebug] ========== Terrain Tile Analysis ==========");
        Debug.Log($"[TerrainDebug] Total tiles: {totalTiles}");
        
        // Log camera information
        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Debug.Log($"[TerrainDebug] Camera position: {mainCamera.transform.position}");
            Debug.Log($"[TerrainDebug] Camera culling mask: {mainCamera.cullingMask}");
            Debug.Log($"[TerrainDebug] Camera far clip: {mainCamera.farClipPlane}");
        }
        else
        {
            Debug.LogWarning($"[TerrainDebug] No main camera found!");
        }
        
        // Count tiles with mesh data
        var tilesWithMeshQuery = GetEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<VertexElement>()
        );
        int tilesWithMesh = tilesWithMeshQuery.CalculateEntityCount();
        Debug.Log($"[TerrainDebug] Tiles with mesh data: {tilesWithMesh}");
        
        // Count tiles with rendering components
        var tilesWithRenderingQuery = GetEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<MaterialMeshInfo>()
        );
        int tilesWithRendering = tilesWithRenderingQuery.CalculateEntityCount();
        Debug.Log($"[TerrainDebug] Tiles with rendering components: {tilesWithRendering}");
        
        // Count tiles with LocalToWorld
        var tilesWithLocalToWorldQuery = GetEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<LocalToWorld>()
        );
        int tilesWithL2W = tilesWithLocalToWorldQuery.CalculateEntityCount();
        Debug.Log($"[TerrainDebug] Tiles with LocalToWorld: {tilesWithL2W}");
        
        // Count tiles with RenderBounds
        var tilesWithBoundsQuery = GetEntityQuery(
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadOnly<RenderBounds>()
        );
        int tilesWithBounds = tilesWithBoundsQuery.CalculateEntityCount();
        Debug.Log($"[TerrainDebug] Tiles with RenderBounds: {tilesWithBounds}");
        
        // Inspect first tile in detail
        var entities = tileQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        if (entities.Length > 0)
        {
            var entity = entities[0];
            var tile = EntityManager.GetComponentData<TerrainTile>(entity);
            
            Debug.Log($"[TerrainDebug] --- First Tile Detail (Entity {entity.Index}:{entity.Version}) ---");
            Debug.Log($"[TerrainDebug]   Grid: {tile.gridCoordinate}");
            Debug.Log($"[TerrainDebug]   MeshGenerated: {tile.meshGenerated}");
            
            if (EntityManager.HasComponent<LocalTransform>(entity))
            {
                var transform = EntityManager.GetComponentData<LocalTransform>(entity);
                Debug.Log($"[TerrainDebug]   Position: {transform.Position}");
                Debug.Log($"[TerrainDebug]   Scale: {transform.Scale}");
            }
            else
            {
                Debug.LogWarning($"[TerrainDebug]   Missing LocalTransform!");
            }
            
            if (EntityManager.HasComponent<LocalToWorld>(entity))
            {
                var l2w = EntityManager.GetComponentData<LocalToWorld>(entity);
                Debug.Log($"[TerrainDebug]   LocalToWorld.Position: {l2w.Position}");
            }
            else
            {
                Debug.LogWarning($"[TerrainDebug]   Missing LocalToWorld!");
            }
            
            if (EntityManager.HasComponent<MaterialMeshInfo>(entity))
            {
                try
                {
                    var mmi = EntityManager.GetComponentData<MaterialMeshInfo>(entity);
                    Debug.Log($"[TerrainDebug]   MaterialMeshInfo: Mesh={mmi.MeshID}, Material={mmi.MaterialID}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[TerrainDebug]   MaterialMeshInfo exists but has invalid state: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[TerrainDebug]   Missing MaterialMeshInfo!");
            }
            
            if (EntityManager.HasComponent<RenderBounds>(entity))
            {
                var bounds = EntityManager.GetComponentData<RenderBounds>(entity);
                Debug.Log($"[TerrainDebug]   RenderBounds: Center={bounds.Value.Center}, Extents={bounds.Value.Extents}");
            }
            else
            {
                Debug.LogWarning($"[TerrainDebug]   Missing RenderBounds!");
            }
            
            if (EntityManager.HasComponent<WorldRenderBounds>(entity))
            {
                var worldBounds = EntityManager.GetComponentData<WorldRenderBounds>(entity);
                Debug.Log($"[TerrainDebug]   WorldRenderBounds: Center={worldBounds.Value.Center}, Extents={worldBounds.Value.Extents}");
            }
            else
            {
                Debug.LogWarning($"[TerrainDebug]   Missing WorldRenderBounds!");
            }
            
            if (EntityManager.HasComponent<RenderFilterSettings>(entity))
            {
                var filterSettings = EntityManager.GetSharedComponentManaged<RenderFilterSettings>(entity);
                Debug.Log($"[TerrainDebug]   RenderFilterSettings: Layer={filterSettings.Layer}, RenderingLayerMask={filterSettings.RenderingLayerMask}, MotionMode={filterSettings.MotionMode}");
            }
            else
            {
                Debug.LogWarning($"[TerrainDebug]   Missing RenderFilterSettings!");
            }
            
            if (EntityManager.HasComponent<MeshReference>(entity))
            {
                var meshRef = EntityManager.GetComponentData<MeshReference>(entity);
                if (meshRef.mesh != null)
                {
                    Debug.Log($"[TerrainDebug]   Mesh: {meshRef.mesh.name}, verts={meshRef.mesh.vertexCount}, tris={meshRef.mesh.triangles.Length/3}");
                    Debug.Log($"[TerrainDebug]   Mesh Bounds: {meshRef.mesh.bounds}");
                }
                else
                {
                    Debug.LogWarning($"[TerrainDebug]   MeshReference exists but mesh is null!");
                }
            }
            else
            {
                Debug.LogWarning($"[TerrainDebug]   Missing MeshReference!");
            }
            
            // Check for buffers
            if (EntityManager.HasBuffer<VertexElement>(entity))
            {
                var vertBuffer = EntityManager.GetBuffer<VertexElement>(entity);
                Debug.Log($"[TerrainDebug]   VertexBuffer: {vertBuffer.Length} vertices");
            }
        }
        
        entities.Dispose();
        
        Debug.Log($"[TerrainDebug] ==========================================");
    }
}

