using Unity.Collections;
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
    /// <summary>Registers requirements for transform-follower initialization.</summary>
    protected override void OnCreate()
    {
        RequireForUpdate<TransformFollowerTargetSearch>();
        RequireForUpdate<TransformFollowerSettings>();
    }
    
    /// <summary>
    /// Finds all entities with a <see cref="TransformFollowerTargetSearch"/> component that have not
    /// yet been initialized, locates the target <see cref="Transform"/> via <see cref="FindTarget"/>,
    /// and adds or updates a <see cref="TransformReference"/> component with the resolved target.
    /// Marks each search component as <c>initialized</c> on success.
    /// </summary>
    protected override void OnUpdate()
    {
        bool needsInit = false;
        foreach (var search in SystemAPI.Query<RefRO<TransformFollowerTargetSearch>>())
        {
            if (!search.ValueRO.initialized)
            {
                needsInit = true;
                break;
            }
        }

        if (!needsInit)
            return;

        var pending = new NativeList<Entity>(8, Allocator.Temp);

        foreach (var (search, entity) in SystemAPI.Query<RefRO<TransformFollowerTargetSearch>>().WithEntityAccess())
        {
            if (!search.ValueRO.initialized)
                pending.Add(entity);
        }

        for (int i = 0; i < pending.Length; i++)
        {
            Entity entity = pending[i];
            var search = EntityManager.GetComponentData<TransformFollowerTargetSearch>(entity);
            if (search.initialized)
                continue;

            Transform targetTransform = FindTarget(search);

            if (targetTransform == null)
            {
                Debug.LogWarning($"[TransformFollowerInitSystem] Could not find target! " +
                    $"Mode: {search.mode}, Search: '{search.searchString}'");
                continue;
            }

            if (!EntityManager.HasComponent<TransformReference>(entity))
            {
                EntityManager.AddComponentObject(entity, new TransformReference
                {
                    target = targetTransform
                });
            }
            else
            {
                var transformRef = EntityManager.GetComponentObject<TransformReference>(entity);
                transformRef.target = targetTransform;
            }

            search.initialized = true;
            EntityManager.SetComponentData(entity, search);
        }

        pending.Dispose();
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
                    Debug.LogError($"[TransformFollowerInitSystem] Could not find GameObject named '{searchString}'");
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
                Debug.LogWarning("[TransformFollowerInitSystem] DirectReference mode doesn't work across subscenes");
                return null;
                
            default:
                return null;
        }
    }
}
