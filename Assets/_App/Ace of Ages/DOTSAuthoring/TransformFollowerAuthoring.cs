using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component to make an entity follow a Transform defined outside of the DOTS subscene.
/// </summary>
/// <remarks>
/// This requires a managed component (TransformReference) to bridge the gap between GameObjects and Entities.
/// The fundamental limitation is that DOTS entities can't directly reference GameObjects in a Burst-compatible way.
/// </remarks>
public class TransformFollowerAuthoring : MonoBehaviour
{
    [Tooltip("The Transform to follow (can be outside the subscene)")]
    public Transform targetTransform;
    
    [Tooltip("Offset from the target transform")]
    public Vector3 offset = Vector3.zero;
    
    [Tooltip("Should the entity rotate to match the target?")]
    public bool followRotation = true;
    
    [Tooltip("Smoothing factor (0 = instant, higher = smoother)")]
    public float smoothTime = 0.1f;

    public class Baker : Baker<TransformFollowerAuthoring>
    {
        public override void Bake(TransformFollowerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // Add the unmanaged component with settings
            AddComponent(entity, new TransformFollowerSettings
            {
                offset = authoring.offset,
                followRotation = authoring.followRotation,
                smoothTime = authoring.smoothTime
            });
            
            // Add the managed component with the Transform reference
            // This is necessary because we can't store a managed reference in a Burst-compatible component
            AddComponentObject(entity, new TransformReference
            {
                target = authoring.targetTransform
            });
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

