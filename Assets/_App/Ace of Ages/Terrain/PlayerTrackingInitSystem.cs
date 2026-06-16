using Unity.Entities;
using UnityEngine;

/// <summary>
/// System that initializes PlayerTransformReference at runtime.
/// This runs at startup to find the player GameObject and assign it to the terrain system.
/// </summary>
/// <remarks>
/// This is necessary because:
/// 1. TerrainConfigAuthoring is in an ECS subscene
/// 2. Player GameObject (XR Origin) is in the main scene
/// 3. We can't reference GameObjects across scenes during baking
/// 4. We need to find and assign the player Transform at runtime after all scenes load
/// 
/// This system looks for entities with PlayerTrackingSearch but uninitialized PlayerTransformReference,
/// and populates the PlayerTransformReference by searching for the target GameObject.
/// </remarks>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class PlayerTrackingInitSystem : SystemBase
{
    private bool _hasLoggedAttempt = false;
    
    /// <summary>Registers <see cref="PlayerTrackingSearch"/> and <see cref="PlayerTransformReference"/> requirements.</summary>
    protected override void OnCreate()
    {
        // Require both components to exist
        RequireForUpdate<PlayerTrackingSearch>();
        RequireForUpdate<PlayerTransformReference>();
    }
    
    /// <summary>
    /// Checks all <see cref="PlayerTrackingSearch"/> components for uninitialized entries and, for each,
    /// calls <see cref="FindPlayer"/> to locate the player <see cref="Transform"/> and stores it in the
    /// <see cref="PlayerTransformReference"/> singleton. Marks each search as initialized on success.
    /// </summary>
    protected override void OnUpdate()
    {
        // Check if any entities need initialization
        bool needsInit = false;
        
        foreach (var search in SystemAPI.Query<RefRO<PlayerTrackingSearch>>())
        {
            if (!search.ValueRO.initialized)
            {
                needsInit = true;
                break;
            }
        }
        
        if (!needsInit)
        {
            // All entities are initialized, no work to do
            return;
        }
        
        if (!_hasLoggedAttempt)
        {
            Debug.Log("[PlayerTrackingInitSystem] Attempting to find player GameObject...");
            _hasLoggedAttempt = true;
        }
        
        // Process uninitialized entities
        foreach (var (search, entity) in SystemAPI.Query<RefRW<PlayerTrackingSearch>>().WithEntityAccess())
        {
            // Skip if already initialized
            if (search.ValueRO.initialized)
            {
                continue;
            }
            
            Debug.Log($"[PlayerTrackingInitSystem] Searching for player. Mode: {search.ValueRO.mode}, Search: '{search.ValueRO.searchString}'");
            
            // Find the target transform
            Transform playerTransform = FindPlayer(search.ValueRO);
            
            if (playerTransform == null)
            {
                Debug.LogWarning($"[PlayerTrackingInitSystem] Could not find player GameObject! " +
                    $"Mode: {search.ValueRO.mode}, Search: '{search.ValueRO.searchString}'\n" +
                    $"The terrain system will not work until a player is found.");
                continue;
            }
            
            Debug.Log($"[PlayerTrackingInitSystem] ✅ Found player: {playerTransform.name} at position {playerTransform.position}");
            
            // Update the PlayerTransformReference
            var playerRef = EntityManager.GetComponentObject<PlayerTransformReference>(entity);
            if (playerRef != null)
            {
                playerRef.playerTransform = playerTransform;
                Debug.Log($"[PlayerTrackingInitSystem] ✅ PlayerTransformReference updated successfully");
            }
            else
            {
                Debug.LogError("[PlayerTrackingInitSystem] PlayerTransformReference component is null!");
            }
            
            // Mark as initialized
            search.ValueRW.initialized = true;
        }
    }
    
    /// <summary>
    /// Searches for the player <see cref="Transform"/> using the mode and search string stored in
    /// <paramref name="searchParams"/>. Supports FindByName, FindByTag, and FindMainCamera modes.
    /// Returns <c>null</c> and logs a warning when the target is not found.
    /// </summary>
    private Transform FindPlayer(PlayerTrackingSearch searchParams)
    {
        switch (searchParams.mode)
        {
            case PlayerTrackingSearch.Mode.FindByName:
                string name = searchParams.searchString.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogError("[PlayerTrackingInitSystem] FindByName mode requires a search string!");
                    return null;
                }
                
                var foundByName = GameObject.Find(name);
                if (foundByName != null)
                {
                    Debug.Log($"[PlayerTrackingInitSystem] Found GameObject by name: '{name}'");
                    return foundByName.transform;
                }
                else
                {
                    Debug.LogWarning($"[PlayerTrackingInitSystem] Could not find GameObject named '{name}'\n" +
                        $"Make sure the GameObject exists and is active in the hierarchy.");
                    
                    // Debug: List similar GameObjects
                    var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                    Debug.Log($"[PlayerTrackingInitSystem] Searching through {allObjects.Length} GameObjects...");
                    foreach (var obj in allObjects)
                    {
                        if (obj.name.Contains("XR") || obj.name.Contains("Origin") || obj.name.Contains("Rig"))
                        {
                            Debug.Log($"  - Found similar: '{obj.name}' (active: {obj.activeInHierarchy})");
                        }
                    }
                }
                break;
                
            case PlayerTrackingSearch.Mode.FindByTag:
                string tag = searchParams.searchString.ToString();
                if (string.IsNullOrEmpty(tag))
                {
                    Debug.LogError("[PlayerTrackingInitSystem] FindByTag mode requires a search string!");
                    return null;
                }
                
                try
                {
                    var foundByTag = GameObject.FindGameObjectWithTag(tag);
                    if (foundByTag != null)
                    {
                        Debug.Log($"[PlayerTrackingInitSystem] Found GameObject by tag: '{tag}'");
                        return foundByTag.transform;
                    }
                    else
                    {
                        Debug.LogWarning($"[PlayerTrackingInitSystem] Could not find GameObject with tag '{tag}'");
                    }
                }
                catch (UnityException e)
                {
                    Debug.LogError($"[PlayerTrackingInitSystem] Tag '{tag}' does not exist! {e.Message}");
                }
                break;
                
            case PlayerTrackingSearch.Mode.FindMainCamera:
                if (Camera.main != null)
                {
                    Debug.Log($"[PlayerTrackingInitSystem] Found Main Camera: '{Camera.main.name}'");
                    return Camera.main.transform;
                }
                else
                {
                    Debug.LogWarning("[PlayerTrackingInitSystem] No camera tagged as MainCamera found");
                }
                break;
        }
        
        return null;
    }
}


