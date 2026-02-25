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
    private bool _initialized = false;

    void Start()
    {
        // Set the Transform reference at runtime (after baking)
        // This allows us to reference GameObjects outside the subscene
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null && world.EntityManager.Exists(_entity))
        {
            Transform targetTransform = FindTarget();
            
            if (targetTransform == null)
            {
                Debug.LogWarning($"TransformFollower on {gameObject.name}: Could not find target! " +
                    $"Mode: {targetMode}, Name: '{targetName}', Tag: '{targetTag}'", this);
            }
            
            // Add or set the managed component with the Transform reference
            if (!world.EntityManager.HasComponent<TransformReference>(_entity))
            {
                world.EntityManager.AddComponentObject(_entity, new TransformReference
                {
                    target = targetTransform
                });
            }
            else
            {
                world.EntityManager.SetComponentData(_entity, new TransformReference
                {
                    target = targetTransform
                });
            }
            _initialized = true;
        }
    }
    
    private Transform FindTarget()
    {
        switch (targetMode)
        {
            case TargetMode.FindByName:
                if (string.IsNullOrEmpty(targetName))
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Target name is empty!", this);
                    return null;
                }
                var foundByName = GameObject.Find(targetName);
                if (foundByName == null)
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Could not find GameObject named '{targetName}'", this);
                    return null;
                }
                return foundByName.transform;
                
            case TargetMode.FindByTag:
                if (string.IsNullOrEmpty(targetTag))
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Target tag is empty!", this);
                    return null;
                }
                var foundByTag = GameObject.FindGameObjectWithTag(targetTag);
                if (foundByTag == null)
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Could not find GameObject with tag '{targetTag}'", this);
                    return null;
                }
                return foundByTag.transform;
                
            case TargetMode.DirectReference:
                if (targetGameObject == null)
                {
                    Debug.LogError($"TransformFollower on {gameObject.name}: Target GameObject reference is null!", this);
                    return null;
                }
                return targetGameObject.transform;
                
            default:
                return null;
        }
    }

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

    public class Baker : Baker<TransformFollowerAuthoring>
    {
        public override void Bake(TransformFollowerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // Store the entity reference so we can access it at runtime
            authoring._entity = entity;
            
            // Add the unmanaged component with settings
            AddComponent(entity, new TransformFollowerSettings
            {
                offset = authoring.offset,
                followRotation = authoring.followRotation,
                smoothTime = authoring.smoothTime
            });
            
            // DO NOT add TransformReference here - it will be added at runtime in Start()
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
/// Managed component to hold the reference to the external Transform.
/// This is the workaround for the fundamental limitation - we need a managed component
/// to bridge between the GameObject world and the ECS world.
/// </summary>
public class TransformReference : IComponentData
{
    public Transform target;
}

