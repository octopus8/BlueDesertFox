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
    
    /// <summary>Caches the <see cref="EntityManager"/> reference.</summary>
    void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
    }
    
    /// <summary>Checks for the <see cref="updateTargetKey"/> press and calls <see cref="UpdateFollowerTarget"/> when triggered.</summary>
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
        if (_entityManager.Exists(_entityToFollow))
        {
            _entityManager.SetComponentData(_entityToFollow, new TransformReference
            {
                target = targetToFollow
            });
            
            _entityManager.SetComponentData(_entityToFollow, new TransformFollowerSettings
            {
                offset = offset,
                followRotation = followRotation,
                smoothTime = smoothTime
            });
        }
        
        var query = _entityManager.CreateEntityQuery(typeof(TransformFollowerSettings));
        var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
        
        foreach (var entity in entities)
        {
            var settings = _entityManager.GetComponentData<TransformFollowerSettings>(entity);
            settings.offset = offset;
            _entityManager.SetComponentData(entity, settings);
        }
        
        entities.Dispose();
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
        entities.Dispose();
    }
    
    /// <summary>
    /// Example: Create a new entity at runtime that follows a transform
    /// </summary>
    public Entity CreateFollowerEntity(Transform target, Vector3 offset)
    {
        Entity newEntity = _entityManager.CreateEntity();
        
        _entityManager.AddComponentData(newEntity, Unity.Transforms.LocalTransform.Identity);
        
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
        
        return newEntity;
    }
    
    /// <summary>Draws Scene-view gizmos showing the target position and offset when this GameObject is selected.</summary>
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
