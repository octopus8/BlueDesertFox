using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component to make an entity follow a Transform defined outside of the DOTS subscene.
/// </summary>
/// <remarks>
/// This requires a managed component (TransformReference) to bridge the gap between GameObjects and Entities.
/// The fundamental limitation is that DOTS entities can't directly reference GameObjects in a Burst-compatible way.
/// 
/// IMPORTANT: Because we need to reference objects outside the subscene, the Transform reference is set at
/// RUNTIME in Start(), not during baking. The baker only sets up the settings component.
/// </remarks>
public class TransformFollowerAuthoring : MonoBehaviour
{
    public enum TargetMode
    {
        FindByName,
        FindByTag,
        DirectReference
    }
    
    [Tooltip("How to find the target GameObject")]
    public TargetMode targetMode = TargetMode.FindByName;
    
    [Tooltip("Name of the GameObject to follow (e.g., 'Right Controller')")]
    public string targetName = "";
    
    [Tooltip("Tag of the GameObject to follow (e.g., 'Player')")]
    public string targetTag = "";
    
    [Tooltip("Direct reference (only works for objects in the same subscene)")]
    public GameObject targetGameObject;
    
    [Tooltip("Offset from the target transform")]
    public Vector3 offset = Vector3.zero;
    
    [Tooltip("Should the entity rotate to match the target?")]
    public bool followRotation = true;
    
    [Tooltip("Smoothing factor (0 = instant, higher = smoother)")]
    public float smoothTime = 0.1f;

    private Entity _entity;

    /// <summary>
    /// Starts the <see cref="InitializeAfterBaking"/> coroutine for non-SubScene usage.
    /// Note: this method does NOT run for GameObjects baked into SubScenes —
    /// <see cref="TransformFollowerInitSystem"/> handles runtime initialization in that case.
    /// </summary>
    void Start()
    {
        Debug.Log($"[TransformFollower] Start() called on {gameObject.name}", this);
        
        // NOTE: This Start() method will NOT run for GameObjects in baked subscenes!
        // Subscenes are fully converted to entities at edit time, and MonoBehaviours are destroyed.
        // The TransformFollowerInitSystem handles initialization at runtime instead.
        
        // This code is kept for backward compatibility with non-subscene usage
        StartCoroutine(InitializeAfterBaking());
    }
    
    /// <summary>
    /// Waits one frame for baking to complete, then locates the baked entity and adds or updates a
    /// <see cref="TransformReference"/> component with the target <see cref="Transform"/> resolved via
    /// <see cref="FindTarget"/>. Used as a fallback for non-SubScene authoring instances.
    /// </summary>
    private System.Collections.IEnumerator InitializeAfterBaking()
    {
        // Wait a frame to ensure baking is complete
        yield return null;
        
        // Set the Transform reference at runtime (after baking)
        // This allows us to reference GameObjects outside the subscene
        var world = World.DefaultGameObjectInjectionWorld;
        Debug.Log($"[TransformFollower] Initializing on {gameObject.name}. World exists: {world != null}", this);
        
        if (world == null)
        {
            Debug.LogError($"[TransformFollower] Failed to initialize on {gameObject.name}. World is null!", this);
            yield break;
        }
        
        // Try to get the entity from the baker-stored reference first
        Entity entity = _entity;
        
        // If the entity is invalid, try to find it via the EntityManager
        if (!world.EntityManager.Exists(entity))
        {
            Debug.LogWarning($"[TransformFollower] Baked entity reference is invalid. Trying to find entity another way...", this);
            
            // Query for entities with our settings component
            // This won't work well if there are multiple followers, but it's a fallback
            var query = world.EntityManager.CreateEntityQuery(typeof(TransformFollowerSettings));
            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            if (entities.Length > 0)
            {
                Debug.LogWarning($"[TransformFollower] Found {entities.Length} entities with TransformFollowerSettings. Using first one.", this);
                entity = entities[0];
            }
            entities.Dispose();
        }
        
        if (!world.EntityManager.Exists(entity))
        {
            Debug.LogError($"[TransformFollower] Failed to find valid entity for {gameObject.name}", this);
            yield break;
        }
        
        Debug.Log($"[TransformFollower] Found valid entity for {gameObject.name}", this);
        
        Transform targetTransform = FindTarget();
        
        if (targetTransform == null)
        {
            Debug.LogWarning($"TransformFollower on {gameObject.name}: Could not find target! " +
                $"Mode: {targetMode}, Name: '{targetName}', Tag: '{targetTag}'", this);
        }
        else
        {
            Debug.Log($"[TransformFollower] Found target transform: {targetTransform.name} at position {targetTransform.position}", this);
        }
        
        // Add or set the managed component with the Transform reference
        if (!world.EntityManager.HasComponent<TransformReference>(entity))
        {
            world.EntityManager.AddComponentObject(entity, new TransformReference
            {
                target = targetTransform
            });
            Debug.Log($"[TransformFollower] Added TransformReference component to entity", this);
        }
        else
        {
            world.EntityManager.SetComponentData(entity, new TransformReference
            {
                target = targetTransform
            });
            Debug.Log($"[TransformFollower] Updated TransformReference component on entity", this);
        }
    }
    
    /// <summary>
    /// Resolves a target <see cref="Transform"/> using the configured <see cref="targetMode"/>.
    /// Supports FindByName (<c>GameObject.Find</c>), FindByTag (<c>FindGameObjectWithTag</c>),
    /// and DirectReference (returns <see cref="targetGameObject"/>).
    /// </summary>
    private Transform FindTarget()
    {
        Debug.Log($"[TransformFollower] FindTarget called. Mode: {targetMode}, Name: '{targetName}', Tag: '{targetTag}'", this);
        
        switch (targetMode)
        {
            case TargetMode.FindByName:
                if (string.IsNullOrEmpty(targetName))
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Target name is empty!", this);
                    return null;
                }
                Debug.Log($"[TransformFollower] Searching for GameObject with name: '{targetName}'", this);
                var foundByName = GameObject.Find(targetName);
                if (foundByName == null)
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Could not find GameObject named '{targetName}'", this);
                    return null;
                }
                Debug.Log($"[TransformFollower] Successfully found GameObject: {foundByName.name}", this);
                return foundByName.transform;
                
            case TargetMode.FindByTag:
                if (string.IsNullOrEmpty(targetTag))
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Target tag is empty!", this);
                    return null;
                }
                Debug.Log($"[TransformFollower] Searching for GameObject with tag: '{targetTag}'", this);
                var foundByTag = GameObject.FindGameObjectWithTag(targetTag);
                if (foundByTag == null)
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Could not find GameObject with tag '{targetTag}'", this);
                    return null;
                }
                Debug.Log($"[TransformFollower] Successfully found GameObject: {foundByTag.name}", this);
                return foundByTag.transform;
                
            case TargetMode.DirectReference:
                if (targetGameObject == null)
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Target GameObject reference is null!", this);
                    return null;
                }
                Debug.Log($"[TransformFollower] Using direct reference: {targetGameObject.name}", this);
                return targetGameObject.transform;
                
            default:
                return null;
        }
    }

    /// <summary>
    /// Removes the managed <see cref="TransformReference"/> component from the baked entity when
    /// this authoring MonoBehaviour is destroyed, preventing dangling references in the ECS world.
    /// </summary>
    void OnDestroy()
    {
        // Clean up when destroyed
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.EntityManager.Exists(_entity))
        {
            if (world.EntityManager.HasComponent<TransformReference>(_entity))
            {
                world.EntityManager.RemoveComponent<TransformReference>(_entity);
            }
        }
    }

    /// <summary>Bakes follower settings and a runtime-search configuration component onto the entity.</summary>
    public class Baker : Baker<TransformFollowerAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(TransformFollowerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // Store the entity reference so we can access it at runtime (if needed)
            authoring._entity = entity;
            
            // Add the unmanaged component with settings
            AddComponent(entity, new TransformFollowerSettings
            {
                offset = authoring.offset,
                followRotation = authoring.followRotation,
                smoothTime = authoring.smoothTime
            });
            
            // Add the target search parameters
            // This will be used at runtime to find and set the TransformReference
            TransformFollowerTargetSearch.Mode mode;
            string searchString;
            
            switch (authoring.targetMode)
            {
                case TargetMode.FindByName:
                    mode = TransformFollowerTargetSearch.Mode.FindByName;
                    searchString = authoring.targetName;
                    break;
                case TargetMode.FindByTag:
                    mode = TransformFollowerTargetSearch.Mode.FindByTag;
                    searchString = authoring.targetTag;
                    break;
                case TargetMode.DirectReference:
                    mode = TransformFollowerTargetSearch.Mode.DirectReference;
                    searchString = authoring.targetGameObject != null ? authoring.targetGameObject.name : "";
                    break;
                default:
                    mode = TransformFollowerTargetSearch.Mode.FindByName;
                    searchString = "";
                    break;
            }
            
            AddComponent(entity, new TransformFollowerTargetSearch
            {
                mode = mode,
                searchString = searchString,
                initialized = false
            });
            
            // DO NOT add TransformReference here - it will be added at runtime by TransformFollowerInitSystem
            // This is because we need to reference objects outside the subscene
        }
    }
}

/// <summary>
/// Unmanaged component storing the follower settings.
/// </summary>
public struct TransformFollowerSettings : IComponentData
{
    public Unity.Mathematics.float3 offset;
    public bool followRotation;
    public float smoothTime;
}

/// <summary>
/// Unmanaged component storing the target search parameters.
/// This is baked into the entity so it can be used at runtime to find the target.
/// </summary>
public struct TransformFollowerTargetSearch : IComponentData
{
    public enum Mode : byte
    {
        FindByName = 0,
        FindByTag = 1,
        DirectReference = 2
    }
    
    public Mode mode;
    public Unity.Collections.FixedString128Bytes searchString; // Stores either name or tag
    public bool initialized; // Flag to track if TransformReference has been set up
}

/// <summary>
/// Managed component to hold the reference to the external Transform.
/// This is the workaround for the fundamental limitation - we need a managed component
/// to bridge between the GameObject world and the ECS world.
/// </summary>
public class TransformReference : IComponentData
{
    public Transform target;
}

