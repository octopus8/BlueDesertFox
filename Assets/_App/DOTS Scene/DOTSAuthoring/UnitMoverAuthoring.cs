using Unity.Entities;
using UnityEngine;

public class UnitMoverAuthoring : MonoBehaviour
{
    public float moveSpeed;
    public SplineBlobAssetComponent spline;

    public class Baker : Baker<UnitMoverAuthoring>
    {
        public override void Bake(UnitMoverAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitMover
            {
                moveSpeed = authoring.moveSpeed,
                spline = authoring.spline
            });
        }
    }
}



public struct UnitMover : IComponentData
{
    public float moveSpeed;
    public SplineBlobAssetComponent spline;
}
