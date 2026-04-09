using Unity.Entities;
using UnityEngine;

/// <summary>
/// System that initializes HeadsetTransformReference at runtime.
/// This runs at startup to find the headset GameObject and assign it to the terrain system for head-tracking rotation.
/// </summary>
/// <remarks>
/// This system looks for entities with HeadsetTrackingSearch but uninitialized HeadsetTransformReference,
/// and populates the HeadsetTransformReference by searching for the target GameObject.
/// </remarks>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class HeadsetTrackingInitSystem : SystemBase
{
    private bool _hasLoggedAttempt = false;
    
    protected override void OnCreate()
    {
        // Require both components to exist
        RequireForUpdate<HeadsetTrackingSearch>();
        RequireForUpdate<HeadsetTransformReference>();
    }
    
    protected override void OnUpdate()
    {
        // Check if any entities need initialization
        bool needsInit = false;
        
        foreach (var search in SystemAPI.Query<RefRO<HeadsetTrackingSearch>>())
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
            Debug.Log("[HeadsetTrackingInitSystem] Attempting to find headset GameObject...");
            _hasLoggedAttempt = true;
        }
        
        // Process uninitialized entities
        foreach (var (search, entity) in SystemAPI.Query<RefRW<HeadsetTrackingSearch>>().WithEntityAccess())
        {
            // Skip if already initialized
            if (search.ValueRO.initialized)
            {
                continue;
            }
            
            Debug.Log($"[HeadsetTrackingInitSystem] Searching for headset. Mode: {search.ValueRO.mode}, Search: '{search.ValueRO.searchString}'");
            
            // Find the target transform
            Transform headsetTransform = FindHeadset(search.ValueRO);
            
            if (headsetTransform == null)
            {
                Debug.LogError($"[HeadsetTrackingInitSystem] Could not find headset GameObject! " +
                    $"Mode: {search.ValueRO.mode}, Search: '{search.ValueRO.searchString}'\n" +
                    $"Head-tracking rotation will not work until a headset is found.");
                continue;
            }
            
            Debug.Log($"[HeadsetTrackingInitSystem] ✅ Found headset: {headsetTransform.name} at position {headsetTransform.position}");
            
            // Update the HeadsetTransformReference
            var headsetRef = EntityManager.GetComponentObject<HeadsetTransformReference>(entity);
            if (headsetRef != null)
            {
                headsetRef.headsetTransform = headsetTransform;
                Debug.Log($"[HeadsetTrackingInitSystem] ✅ HeadsetTransformReference updated successfully");
            }
            else
            {
                Debug.LogError("[HeadsetTrackingInitSystem] HeadsetTransformReference component is null!");
            }
            
            // Mark as initialized
            search.ValueRW.initialized = true;
        }
    }
    
    private Transform FindHeadset(HeadsetTrackingSearch searchParams)
    {
        switch (searchParams.mode)
        {
            case HeadsetTrackingSearch.Mode.FindByName:
                string name = searchParams.searchString.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    Debug.LogError("[HeadsetTrackingInitSystem] FindByName mode requires a search string!");
                    return null;
                }
                
                var foundByName = GameObject.Find(name);
                if (foundByName != null)
                {
                    Debug.Log($"[HeadsetTrackingInitSystem] Found GameObject by name: '{name}'");
                    return foundByName.transform;
                }
                else
                {
                    Debug.LogWarning($"[HeadsetTrackingInitSystem] Could not find GameObject named '{name}'\n" +
                        $"Make sure the GameObject exists and is active in the hierarchy.");
                }
                break;
                
            case HeadsetTrackingSearch.Mode.FindByTag:
                string tag = searchParams.searchString.ToString();
                if (string.IsNullOrEmpty(tag))
                {
                    Debug.LogError("[HeadsetTrackingInitSystem] FindByTag mode requires a search string!");
                    return null;
                }
                
                try
                {
                    var foundByTag = GameObject.FindGameObjectWithTag(tag);
                    if (foundByTag != null)
                    {
                        Debug.Log($"[HeadsetTrackingInitSystem] Found GameObject by tag: '{tag}'");
                        return foundByTag.transform;
                    }
                    else
                    {
                        Debug.LogWarning($"[HeadsetTrackingInitSystem] Could not find GameObject with tag '{tag}'");
                    }
                }
                catch (UnityException e)
                {
                    Debug.LogError($"[HeadsetTrackingInitSystem] Tag '{tag}' does not exist! {e.Message}");
                }
                break;
                
            case HeadsetTrackingSearch.Mode.FindMainCamera:
                if (Camera.main != null)
                {
                    Debug.Log($"[HeadsetTrackingInitSystem] Found Main Camera: '{Camera.main.name}'");
                    return Camera.main.transform;
                }
                else
                {
                    Debug.LogWarning("[HeadsetTrackingInitSystem] No camera tagged as MainCamera found");
                }
                break;
        }
        
        return null;
    }
}

