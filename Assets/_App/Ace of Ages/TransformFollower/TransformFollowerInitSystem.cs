using Unity.Entities;
using UnityEngine;

/// <summary>
/// System that initializes TransformReference components at runtime.
/// This runs at startup to find target transforms for entities that need them.
/// </summary>
/// <remarks>
/// This is necessary because:
/// 1. MonoBehaviour.Start() doesn't run on GameObjects in baked subscenes
/// 2. We can't reference GameObjects outside the subscene during baking
/// 3. We need to find and assign the target Transform at runtime
/// 
/// This system looks for entities with TransformFollowerTargetSearch but no TransformReference,
/// and creates the TransformReference by searching for the target GameObject.
/// </remarks>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class TransformFollowerInitSystem : SystemBase
{
    private EntityQuery _uninitializedQuery;
    
    /// <summary>Builds the entity query for uninitialized transform-follower entities.</summary>
    protected override void OnCreate()
    {
        _uninitializedQuery = GetEntityQuery(
            ComponentType.ReadWrite<TransformFollowerTargetSearch>(),
            ComponentType.ReadOnly<TransformFollowerSettings>());
    }
    
    /// <summary>
    /// Finds all entities with a <see cref="TransformFollowerTargetSearch"/> component that have not
    /// yet been initialized, locates the target <see cref="Transform"/> via <see cref="FindTarget"/>,
    /// and adds or updates a <see cref="TransformReference"/> component with the resolved target.
    /// Marks each search component as <c>initialized</c> on success.
    /// </summary>
    protected override void OnUpdate()
    {
        // Get all entities that need initialization
        var entities = _uninitializedQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
        var searchParams = _uninitializedQuery.ToComponentDataArray<TransformFollowerTargetSearch>(Unity.Collections.Allocator.Temp);
        
        int initializedCount = 0;
        int failedCount = 0;
        
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            var search = searchParams[i];
            
            // Skip if already initialized
            if (search.initialized)
            {
                continue;
            }
            
            Debug.Log($"[TransformFollowerInitSystem] Initializing entity {i}. Mode: {search.mode}, Search: '{search.searchString}'");
            
            // Find the target transform
            Transform targetTransform = FindTarget(search);
            
            if (targetTransform == null)
            {
                Debug.LogWarning($"[TransformFollowerInitSystem] Could not find target! " +
                    $"Mode: {search.mode}, Search: '{search.searchString}'");
                failedCount++;
                continue;
            }
            
            Debug.Log($"[TransformFollowerInitSystem] Found target: {targetTransform.name} at position {targetTransform.position}");
            
            // Add the TransformReference component
            if (!EntityManager.HasComponent<TransformReference>(entity))
            {
                EntityManager.AddComponentObject(entity, new TransformReference
                {
                    target = targetTransform
                });
                Debug.Log($"[TransformFollowerInitSystem] Added TransformReference to entity");
            }
            else
            {
                var transformRef = EntityManager.GetComponentObject<TransformReference>(entity);
                transformRef.target = targetTransform;
                Debug.Log($"[TransformFollowerInitSystem] Updated TransformReference on entity");
            }
            
            // Mark as initialized
            search.initialized = true;
            EntityManager.SetComponentData(entity, search);
            initializedCount++;
        }
        
        entities.Dispose();
        searchParams.Dispose();
        
        if (initializedCount > 0 || failedCount > 0)
        {
            Debug.Log($"[TransformFollowerInitSystem] Initialization complete. Initialized: {initializedCount}, Failed: {failedCount}");
        }
    }
    
    /// <summary>
    /// Resolves a target <see cref="Transform"/> using the given <paramref name="searchParams"/> mode.
    /// Supports <c>FindByName</c> (uses <c>GameObject.Find</c>) and <c>FindByTag</c>
    /// (uses <c>GameObject.FindGameObjectWithTag</c>). <c>DirectReference</c> is not supported across
    /// SubScene boundaries and always returns <c>null</c>.
    /// </summary>
    private Transform FindTarget(TransformFollowerTargetSearch searchParams)
    {
        string searchString = searchParams.searchString.ToString();
        
        Debug.Log($"[TransformFollowerInitSystem] FindTarget - Mode: {searchParams.mode}, Search: '{searchString}'");
        
        switch (searchParams.mode)
        {
            case TransformFollowerTargetSearch.Mode.FindByName:
                if (string.IsNullOrEmpty(searchString))
                {
                    Debug.LogError("[TransformFollowerInitSystem] Search string is empty!");
                    return null;
                }
                var foundByName = GameObject.Find(searchString);
                if (foundByName == null)
                {
                    Debug.LogError($"[TransformFollowerInitSystem] Could not find GameObject named '{searchString}'");
                    
                    // Debug: List all GameObjects in the scene
                    var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                    Debug.Log($"[TransformFollowerInitSystem] Found {allObjects.Length} GameObjects in scene:");
                    foreach (var obj in allObjects)
                    {
                        if (obj.name.Contains("Controller") || obj.name.Contains("Attach"))
                        {
                            Debug.Log($"  - {obj.name}");
                        }
                    }
                }
                return foundByName != null ? foundByName.transform : null;
                
            case TransformFollowerTargetSearch.Mode.FindByTag:
                if (string.IsNullOrEmpty(searchString))
                {
                    Debug.LogError("[TransformFollowerInitSystem] Search string is empty!");
                    return null;
                }
                try
                {
                    var foundByTag = GameObject.FindGameObjectWithTag(searchString);
                    return foundByTag != null ? foundByTag.transform : null;
                }
                catch (UnityException)
                {
                    Debug.LogError($"[TransformFollowerInitSystem] Tag '{searchString}' is not defined");
                    return null;
                }
                
            case TransformFollowerTargetSearch.Mode.DirectReference:
                // Direct references don't work across subscene boundaries
                Debug.LogWarning("[TransformFollowerInitSystem] DirectReference mode doesn't work across subscenes");
                return null;
                
            default:
                return null;
        }
    }
}



