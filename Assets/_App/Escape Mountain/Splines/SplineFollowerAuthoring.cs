using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component that adds a <see cref="SplineFollower"/> component to an ECS entity,
/// allowing <c>SplineFollowerSystem</c> to advance the entity along a pre-sampled spline each frame.
/// Assign <see cref="moveSpeed"/> in the Inspector to control travel speed.
/// </summary>
public class SplineFollowerAuthoring : MonoBehaviour
{
    /// <summary>Speed at which the entity travels along the spline in units per second.</summary>
    public float moveSpeed;

    /// <summary>
    /// Bakes <see cref="moveSpeed"/> and an initial <c>distanceRatio</c> of 0 into a
    /// <see cref="SplineFollower"/> component on the target entity.
    /// </summary>
    public class Baker : Baker<SplineFollowerAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(SplineFollowerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new SplineFollower
            {
                moveSpeed = authoring.moveSpeed,
                distanceRatio = 0f
            });
        }
    }
}



/// <summary>
/// ECS component that drives an entity's movement along a pre-sampled spline.
/// <c>SplineFollowerSystem</c> increments <see cref="distanceRatio"/> each frame based on
/// <see cref="moveSpeed"/> and updates the entity's <c>LocalTransform</c> accordingly.
/// </summary>
public struct SplineFollower : IComponentData
{
    /// <summary>Speed at which the entity travels along the spline in world units per second.</summary>
    public float moveSpeed;
    /// <summary>
    /// Normalized distance along the spline in the range [0, 1].
    /// 0 = spline start, 1 = spline end (or wraps back to 0 for closed splines).
    /// </summary>
    public float distanceRatio;
}
