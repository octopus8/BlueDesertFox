using Unity.Entities;
using UnityEngine;

/// <summary>
/// Add this component to any GameObject that should be affected by floating origin shifts.
/// Typically added to the player entity.
/// </summary>
public class FloatingOriginEnabledAuthoring : MonoBehaviour
{
    public class Baker : Baker<FloatingOriginEnabledAuthoring>
    {
        public override void Bake(FloatingOriginEnabledAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<FloatingOriginEnabled>(entity);
        }
    }
}

