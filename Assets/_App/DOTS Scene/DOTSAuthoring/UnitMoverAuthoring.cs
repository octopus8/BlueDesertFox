using Unity.Entities;
using UnityEngine;

public class UnitMoverAuthoring : MonoBehaviour
{
    public float moveSpeed;

    public class Baker : Baker<UnitMoverAuthoring>
    {
        public override void Bake(UnitMoverAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitMover
            {
                moveSpeed = authoring.moveSpeed,
                distanceRatio = 0f
            });
        }
    }
}



public struct UnitMover : IComponentData
{
    public float moveSpeed;
    public float distanceRatio; // A value from 0 to 1 representing the object's position along the spline
}
