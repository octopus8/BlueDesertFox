using Unity.Entities;
using UnityEngine;

public class SplineFollowerAuthoring : MonoBehaviour
{
    public float moveSpeed;

    public class Baker : Baker<SplineFollowerAuthoring>
    {
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



public struct SplineFollower : IComponentData
{
    public float moveSpeed;
    public float distanceRatio; // A value from 0 to 1 representing the object's position along the spline
}
