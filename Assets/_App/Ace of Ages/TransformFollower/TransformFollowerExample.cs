using UnityEngine;
using Unity.Entities;

/// <summary>
/// Example script showing how to set up an entity to follow a Transform at runtime.
/// </summary>
/// <remarks>
/// This demonstrates how to programmatically add the TransformFollower components
/// to an entity that's already been spawned, or how to modify an existing follower's target.
/// </remarks>
public class TransformFollowerExample : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The transform to follow")]
    public Transform targetToFollow;
    
    [Tooltip("The entity to make follow (must be in a loaded subscene)")]
    public GameObject entityInSubscene;
    
    [Header("Settings")]
    public Vector3 offset = Vector3.zero;
    public bool followRotation = true;
    public float smoothTime = 0.1f;
    
    [Header("Runtime Control")]
    [Tooltip("Press this key to update the target at runtime")]
    public KeyCode updateTargetKey = KeyCode.U;
    
    private EntityManager _entityManager;
    private Entity _entityToFollow;
    
    void Start()
    {
        // Get the entity manager from the default world
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        
        // Example: Find the entity associated with a GameObject in the subscene
        // Note: This only works if the GameObject is converted to an entity
        if (entityInSubscene != null)
        {
            // Try to get the entity from the GameObject
            // This works if the GameObject has been baked into an entity
            var entityGO = entityInSubscene;
            
            // In the current ECS version, you'd typically store entity references differently
            // This is just an example - in practice, you might:
            // 1. Use an authoring component to store the entity reference
            // 2. Query for entities with specific components
            // 3. Store the entity in a singleton component
            
            Debug.Log("To properly reference entities from MonoBehaviours, consider using:");
            Debug.Log("1. Entity references stored during baking");
            Debug.Log("2. Querying for entities with specific components");
            Debug.Log("3. Singleton patterns");
        }
    }
    
    void Update()
    {
        if (Input.GetKeyDown(updateTargetKey))
        {
            UpdateFollowerTarget();
        }
    }
    
    /// <summary>
    /// Example: Update or add transform follower components to an entity at runtime
    /// </summary>
    void UpdateFollowerTarget()
    {
        // Example of how to add/update components on an entity at runtime
        
        // Method 1: If you have the entity reference
        if (_entityManager.Exists(_entityToFollow))
        {
            // Update the managed component
            _entityManager.SetComponentData(_entityToFollow, new TransformReference
            {
                target = targetToFollow
            });
            
            // Update settings
            _entityManager.SetComponentData(_entityToFollow, new TransformFollowerSettings
            {
                offset = offset,
                followRotation = followRotation,
                smoothTime = smoothTime
            });
            
            Debug.Log($"Updated entity to follow: {targetToFollow.name}");
        }
        
        // Method 2: Query for all entities with a specific component and update them
        var query = _entityManager.CreateEntityQuery(typeof(TransformFollowerSettings));
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        foreach (var entity in entities)
        {
            var settings = _entityManager.GetComponentData<TransformFollowerSettings>(entity);
            settings.offset = offset;
            _entityManager.SetComponentData(entity, settings);
        }
        
        entities.Dispose();
        
        Debug.Log($"Updated {entities.Length} followers");
    }
    
    /// <summary>
    /// Example: How to find entities by querying for components
    /// </summary>
    void FindFollowerEntities()
    {
        var query = _entityManager.CreateEntityQuery(
            typeof(TransformFollowerSettings),
            typeof(TransformReference)
        );
        
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        Debug.Log($"Found {entities.Length} entities with TransformFollower components");
        
        foreach (var entity in entities)
        {
            var transformRef = _entityManager.GetComponentData<TransformReference>(entity);
            var settings = _entityManager.GetComponentData<TransformFollowerSettings>(entity);
            
            Debug.Log($"Entity following: {(transformRef.target != null ? transformRef.target.name : "null")}");
        }
        
        entities.Dispose();
    }
    
    /// <summary>
    /// Example: Create a new entity at runtime that follows a transform
    /// </summary>
    public Entity CreateFollowerEntity(Transform target, Vector3 offset)
    {
        // Create a new entity
        Entity newEntity = _entityManager.CreateEntity();
        
        // Add required components
        _entityManager.AddComponentData(newEntity, Unity.Transforms.LocalTransform.Identity);
        
        // Add follower components
        _entityManager.AddComponentData(newEntity, new TransformFollowerSettings
        {
            offset = offset,
            followRotation = true,
            smoothTime = 0.1f
        });
        
        _entityManager.AddComponentData(newEntity, new TransformReference
        {
            target = target
        });
        
        Debug.Log($"Created new follower entity for: {target.name}");
        
        return newEntity;
    }
    
    // Debug visualization
    void OnDrawGizmosSelected()
    {
        if (targetToFollow != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(targetToFollow.position, 0.5f);
            
            if (Application.isPlaying)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(targetToFollow.position, targetToFollow.position + offset);
                Gizmos.DrawWireSphere(targetToFollow.position + offset, 0.3f);
            }
        }
    }
}

