using Unity.Entities;
using UnityEngine;

/// <summary>
/// System that initializes WorldOriginTransformReference at runtime.
/// This runs at startup to find the world origin GameObject and assign it to the terrain system for rotation tracking.
/// </summary>
/// <remarks>
/// This system looks for entities with WorldOriginTrackingSearch but uninitialized WorldOriginTransformReference,
/// and populates the WorldOriginTransformReference by searching for the target GameObject.
/// </remarks>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class WorldOriginTrackingInitSystem : SystemBase
{
    private bool _hasLoggedAttempt = false;
    
    /// <summary>Registers <see cref="WorldOriginTrackingSearch"/> and <see cref="WorldOriginTransformReference"/> requirements.</summary>
    protected override void OnCreate()
    {
        // Require both components to exist
        RequireForUpdate<WorldOriginTrackingSearch>();
        RequireForUpdate<WorldOriginTransformReference>();
    }
    
    /// <summary>
    /// Checks all <see cref="WorldOriginTrackingSearch"/> components for uninitialized entries, calls
    /// <see cref="FindWorldOrigin"/> for each, and stores the result in <see cref="WorldOriginTransformReference"/>.
    /// Marks each search as initialized on success.
    /// </summary>
    protected override void OnUpdate()
    {
        // Check if any entities need initialization
        bool needsInit = false;
        
        foreach (var search in SystemAPI.Query<RefRO<WorldOriginTrackingSearch>>())
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
            Debug.Log("[WorldOriginTrackingInitSystem] Attempting to find world origin GameObject...");
            _hasLoggedAttempt = true;
        }
        
        // Process uninitialized entities
        foreach (var (search, entity) in SystemAPI.Query<RefRW<WorldOriginTrackingSearch>>().WithEntityAccess())
        {
            // Skip if already initialized
            if (search.ValueRO.initialized)
            {
                continue;
            }
            
            Debug.Log($"[WorldOriginTrackingInitSystem] Searching for world origin. Mode: {search.ValueRO.mode}, Search: '{search.ValueRO.searchString}'");
            
            // Find the target transform
            Transform worldOriginTransform = FindWorldOrigin(search.ValueRO);
            
            if (worldOriginTransform == null)
            {
                Debug.LogError($"[WorldOriginTrackingInitSystem] Could not find world origin GameObject! " +
                    $"Mode: {search.ValueRO.mode}, Search: '{search.ValueRO.searchString}'\n" +
                    $"World origin tracking rotation will not work until a world origin is found.");
                continue;
            }
            
            Debug.Log($"[WorldOriginTrackingInitSystem] ✅ Found world origin: {worldOriginTransform.name} at position {worldOriginTransform.position}");
            
            // Update the WorldOriginTransformReference
            var worldOriginRef = EntityManager.GetComponentObject<WorldOriginTransformReference>(entity);
            if (worldOriginRef != null)
            {
                worldOriginRef.worldOriginTransform = worldOriginTransform;
                Debug.Log($"[WorldOriginTrackingInitSystem] ✅ WorldOriginTransformReference updated successfully");
            }
            else
            {
                Debug.LogError("[WorldOriginTrackingInitSystem] WorldOriginTransformReference component is null!");
            }
            
            // Mark as initialized
            search.ValueRW.initialized = true;
        }
    }
    
    /// <summary>
    /// Searches for the world-origin <see cref="Transform"/> using the search mode and string in
    /// <paramref name="searchParams"/>. Supports FindByName and FindByTag modes.
    /// Returns <c>null</c> and logs a warning if the target is not found.
    /// </summary>
    private Transform FindWorldOrigin(WorldOriginTrackingSearch searchParams)
    {
        switch (searchParams.mode)
        {
            case WorldOriginTrackingSearch.Mode.FindByName:
                string name = searchParams.searchString.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogError("[WorldOriginTrackingInitSystem] FindByName mode requires a search string!");
                    return null;
                }
                
                var foundByName = GameObject.Find(name);
                if (foundByName != null)
                {
                    Debug.Log($"[WorldOriginTrackingInitSystem] Found GameObject by name: '{name}'");
                    return foundByName.transform;
                }
                else
                {
                    Debug.LogWarning($"[WorldOriginTrackingInitSystem] Could not find GameObject named '{name}'\n" +
                        $"Make sure the GameObject exists and is active in the hierarchy.");
                }
                break;
                
            case WorldOriginTrackingSearch.Mode.FindByTag:
                string tag = searchParams.searchString.ToString();
                if (string.IsNullOrEmpty(tag))
                {
                    Debug.LogError("[WorldOriginTrackingInitSystem] FindByTag mode requires a search string!");
                    return null;
                }
                
                try
                {
                    var foundByTag = GameObject.FindGameObjectWithTag(tag);
                    if (foundByTag != null)
                    {
                        Debug.Log($"[WorldOriginTrackingInitSystem] Found GameObject by tag: '{tag}'");
                        return foundByTag.transform;
                    }
                    else
                    {
                        Debug.LogWarning($"[WorldOriginTrackingInitSystem] Could not find GameObject with tag '{tag}'");
                    }
                }
                catch (UnityException e)
                {
                    Debug.LogError($"[WorldOriginTrackingInitSystem] Tag '{tag}' does not exist! {e.Message}");
                }
                break;
                
            case WorldOriginTrackingSearch.Mode.FindMainCamera:
                if (Camera.main != null)
                {
                    Debug.Log($"[WorldOriginTrackingInitSystem] Found Main Camera: '{Camera.main.name}'");
                    return Camera.main.transform;
                }
                else
                {
                    Debug.LogWarning("[WorldOriginTrackingInitSystem] No camera tagged as MainCamera found");
                }
                break;
        }
        
        return null;
    }
}

