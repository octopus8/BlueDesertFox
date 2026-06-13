using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Debug utility to verify the GameObject tracking implementation is working correctly.
/// Attach to any GameObject in your scene and use the context menu items.
/// </summary>
public class TerrainTrackingDebugger : MonoBehaviour
{
    [Header("Status Display")]
    [SerializeField] private bool showGUI = true;
    
    [Header("Debug Settings")]
    [SerializeField] private bool logEveryFrame = false;
    [SerializeField] private float logInterval = 2f;
    
    private float _lastLogTime;
    private bool _trackingValid;
    private Vector3 _playerPosition;
    private string _playerName;
    private int _activeTileCount;
    
    /// <summary>
    /// Queries the ECS world for <see cref="PlayerTrackingSearch"/>, <see cref="PlayerTransformReference"/>,
    /// <see cref="TerrainTileConfig"/>, and active <see cref="TerrainTile"/> entities, then logs a
    /// comprehensive status report to the Console. Available via the component context menu.
    /// </summary>
    [ContextMenu("Check Tracking Status")]
    public void CheckTrackingStatus()
    {
        Debug.Log("=== Terrain Tracking Status ===");
        
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            Debug.LogError("❌ No default ECS world found! Is the scene running?");
            return;
        }
        
        var em = world.EntityManager;
        
        // Check for PlayerTrackingSearch
        var searchQuery = em.CreateEntityQuery(typeof(PlayerTrackingSearch));
        if (searchQuery.CalculateEntityCount() == 0)
        {
            Debug.LogError("❌ No PlayerTrackingSearch singleton found!");
            Debug.LogError("   Make sure TerrainConfigAuthoring is in the subscene and baked.");
            searchQuery.Dispose();
            return;
        }
        
        var searchEntity = searchQuery.GetSingletonEntity();
        var search = em.GetComponentData<PlayerTrackingSearch>(searchEntity);
        Debug.Log($"🔍 Search Mode: {search.mode}");
        Debug.Log($"🔍 Search String: '{search.searchString}'");
        Debug.Log($"🔍 Initialized: {search.initialized}");
        searchQuery.Dispose();
        
        // Check for PlayerTransformReference
        var playerQuery = em.CreateEntityQuery(typeof(PlayerTransformReference));
        if (playerQuery.CalculateEntityCount() == 0)
        {
            Debug.LogError("❌ No PlayerTransformReference singleton found!");
            Debug.LogError("   Make sure TerrainConfigAuthoring is in the scene and baked.");
            playerQuery.Dispose();
            return;
        }
        
        var entity = playerQuery.GetSingletonEntity();
        var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
        
        if (playerRef == null || playerRef.playerTransform == null)
        {
            Debug.LogWarning("⚠️ PlayerTransformReference exists but Transform is null!");
            if (!search.initialized)
            {
                Debug.LogWarning("   Player search has not completed yet. Wait a frame or check PlayerTrackingInitSystem.");
            }
            else
            {
                Debug.LogWarning("   Player search completed but failed to find GameObject.");
                Debug.LogWarning($"   Check that a GameObject matching search mode '{search.mode}' exists.");
            }
            playerQuery.Dispose();
            return;
        }
        
        Debug.Log($"✅ Tracking: {playerRef.playerTransform.name}");
        Debug.Log($"   GameObject: {playerRef.playerTransform.gameObject.name}");
        Debug.Log($"   Position: {playerRef.playerTransform.position}");
        Debug.Log($"   Active: {playerRef.playerTransform.gameObject.activeInHierarchy}");
        
        playerQuery.Dispose();
        
        // Check for config components
        CheckConfigComponent<TerrainTileConfig>(em, "TerrainTileConfig");
        
        // Check for terrain tiles
        var tileQuery = em.CreateEntityQuery(typeof(TerrainTile));
        int tileCount = tileQuery.CalculateEntityCount();
        Debug.Log($"📦 Active Terrain Tiles: {tileCount}");
        
        if (tileCount == 0)
        {
            Debug.LogWarning("⚠️ No terrain tiles spawned yet.");
            Debug.LogWarning("   If player is assigned and scene is running, check systems are executing.");
        }
        
        tileQuery.Dispose();
        
        Debug.Log("=== End Status ===");
    }
    
    /// <summary>Logs a Console message showing whether exactly one entity with component <typeparamref name="T"/> exists, to verify ECS config singleton presence.</summary>
    private void CheckConfigComponent<T>(EntityManager em, string name) where T : struct, IComponentData
    {
        var query = em.CreateEntityQuery(typeof(T));
        int count = query.CalculateEntityCount();
        
        if (count == 0)
        {
            Debug.LogWarning($"⚠️ {name} not found!");
        }
        else
        {
            Debug.Log($"✅ {name} present");
        }
        
        query.Dispose();
    }
    
    /// <summary>
    /// Logs the tracked player's current world position and distance from the world origin to the Console.
    /// Useful for diagnosing floating-point precision warnings at extreme distances.
    /// Available via the component context menu.
    /// </summary>
    [ContextMenu("Get Player Position")]
    public void GetPlayerPosition()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            Debug.LogError("❌ No ECS world");
            return;
        }
        
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(PlayerTransformReference));
        
        if (query.CalculateEntityCount() > 0)
        {
            var entity = query.GetSingletonEntity();
            var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
            
            if (playerRef?.playerTransform != null)
            {
                Vector3 pos = playerRef.playerTransform.position;
                float distanceFromOrigin = pos.magnitude;
                
                Debug.Log($"Player Position: {pos}");
                Debug.Log($"Distance from Origin: {distanceFromOrigin:F2}m");
            }
            else
            {
                Debug.LogWarning("⚠️ Player transform is null!");
            }
        }
        else
        {
            Debug.LogError("❌ No PlayerTransformReference found!");
        }
        
        query.Dispose();
    }
    
    /// <summary>
    /// Logs the grid coordinates and mesh-generation state of up to 10 active terrain tile entities
    /// to the Console, along with the total count. Available via the component context menu.
    /// </summary>
    [ContextMenu("List All Terrain Tiles")]
    public void ListTerrainTiles()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null) return;
        
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(TerrainTile));
        var tiles = query.ToComponentDataArray<TerrainTile>(Unity.Collections.Allocator.Temp);
        
        Debug.Log($"=== {tiles.Length} Terrain Tiles ===");
        
        for (int i = 0; i < Mathf.Min(tiles.Length, 10); i++) // Show first 10
        {
            var tile = tiles[i];
            Debug.Log($"Tile {i}: Grid {tile.gridCoordinate}, Mesh: {tile.meshGenerated}");
        }
        
        if (tiles.Length > 10)
        {
            Debug.Log($"... and {tiles.Length - 10} more tiles");
        }
        
        tiles.Dispose();
        query.Dispose();
    }
    
    /// <summary>Calls <see cref="UpdateStatus"/> each frame to keep the on-screen GUI overlay current, and re-runs <see cref="CheckTrackingStatus"/> when <c>autoRefresh</c> is enabled.</summary>
    private void Update()
    {
        // Update status for GUI
        UpdateStatus();
        
        // Log periodically
        if (logEveryFrame || Time.time - _lastLogTime > logInterval)
        {
            if (logEveryFrame || _lastLogTime > 0) // Skip first frame
            {
                LogStatus();
            }
            _lastLogTime = Time.time;
        }
    }
    
    /// <summary>Polls the ECS world for the <see cref="PlayerTransformReference"/> singleton and updates the <c>_trackingValid</c> flag and status string used by the on-screen GUI.</summary>
    private void UpdateStatus()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
        {
            _trackingValid = false;
            return;
        }
        
        var em = world.EntityManager;
        var query = em.CreateEntityQuery(typeof(PlayerTransformReference));
        
        if (query.CalculateEntityCount() > 0)
        {
            var entity = query.GetSingletonEntity();
            var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
            
            if (playerRef?.playerTransform != null)
            {
                _trackingValid = true;
                _playerPosition = playerRef.playerTransform.position;
                _playerName = playerRef.playerTransform.name;
            }
            else
            {
                _trackingValid = false;
            }
        }
        else
        {
            _trackingValid = false;
        }
        
        query.Dispose();
        
        // Count tiles
        var tileQuery = em.CreateEntityQuery(typeof(TerrainTile));
        _activeTileCount = tileQuery.CalculateEntityCount();
        tileQuery.Dispose();
    }
    
    /// <summary>Logs the current tracking valid/invalid status and player position (if available) to the Console.</summary>
    private void LogStatus()
    {
        if (_trackingValid)
        {
            Debug.Log($"[Terrain Tracking] Player: {_playerName} at {_playerPosition}, Tiles: {_activeTileCount}");
        }
        else
        {
            Debug.LogWarning("[Terrain Tracking] Not tracking any player!");
        }
    }
    
    /// <summary>Renders an overlay box showing tracking status and player position in the Game view when <c>showGUI</c> is enabled.</summary>
    private void OnGUI()
    {
        if (!showGUI) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 400, 200));
        GUILayout.Box("Terrain Tracking Status");
        
        if (_trackingValid)
        {
            GUILayout.Label($"✅ Tracking: {_playerName}");
            GUILayout.Label($"Position: {_playerPosition}");
            GUILayout.Label($"Distance: {_playerPosition.magnitude:F2}m from origin");
            GUILayout.Label($"Active Tiles: {_activeTileCount}");
        }
        else
        {
            GUILayout.Label("❌ Not Tracking");
            GUILayout.Label("Check TerrainConfigAuthoring setup");
        }
        
        if (GUILayout.Button("Check Status (Console)"))
        {
            CheckTrackingStatus();
        }
        
        if (GUILayout.Button("Get Player Position"))
        {
            GetPlayerPosition();
        }
        
        GUILayout.EndArea();
    }
}

