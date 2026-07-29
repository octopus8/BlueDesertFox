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
    /// <summary>Registers <see cref="WorldOriginTrackingSearch"/> and <see cref="WorldOriginTransformReference"/> requirements.</summary>
    protected override void OnCreate()
    {
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
            return;
        
        foreach (var (search, entity) in SystemAPI.Query<RefRW<WorldOriginTrackingSearch>>().WithEntityAccess())
        {
            if (search.ValueRO.initialized)
                continue;
            
            Transform worldOriginTransform = FindWorldOrigin(search.ValueRO);
            
            if (worldOriginTransform == null)
            {
                Debug.LogError($"[WorldOriginTrackingInitSystem] Could not find world origin GameObject! " +
                    $"Mode: {search.ValueRO.mode}, Search: '{search.ValueRO.searchString}'\n" +
                    $"World origin tracking rotation will not work until a world origin is found.");
                continue;
            }
            
            var worldOriginRef = EntityManager.GetComponentObject<WorldOriginTransformReference>(entity);
            if (worldOriginRef != null)
            {
                worldOriginRef.worldOriginTransform = worldOriginTransform;
            }
            else
            {
                Debug.LogError("[WorldOriginTrackingInitSystem] WorldOriginTransformReference component is null!");
            }
            
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
                    return foundByName.transform;

                Debug.LogWarning($"[WorldOriginTrackingInitSystem] Could not find GameObject named '{name}'\n" +
                    $"Make sure the GameObject exists and is active in the hierarchy.");
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
                        return foundByTag.transform;

                    Debug.LogWarning($"[WorldOriginTrackingInitSystem] Could not find GameObject with tag '{tag}'");
                }
                catch (UnityException e)
                {
                    Debug.LogError($"[WorldOriginTrackingInitSystem] Tag '{tag}' does not exist! {e.Message}");
                }
                break;
                
            case WorldOriginTrackingSearch.Mode.FindMainCamera:
                if (Camera.main != null)
                    return Camera.main.transform;

                Debug.LogWarning("[WorldOriginTrackingInitSystem] No camera tagged as MainCamera found");
                break;
        }
        
        return null;
    }
}
