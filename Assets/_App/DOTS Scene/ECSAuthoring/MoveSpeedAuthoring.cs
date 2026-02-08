using Unity.Entities;
using UnityEngine;

public class MoveSpeedAuthoring : MonoBehaviour
{
    public float moveSpeed;

    public class Baker : Baker<MoveSpeedAuthoring>
    {
        public override void Bake(MoveSpeedAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new UnitMover { moveSpeed = authoring.moveSpeed });
        }
    }
}



public struct UnitMover : IComponentData
{
    public float moveSpeed;
}
