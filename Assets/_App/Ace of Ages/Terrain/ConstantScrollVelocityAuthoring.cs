using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for constant scroll velocity (testing).
/// Enables ConstantScrollVelocitySystem and configures direction/speed.
/// </summary>
public class ConstantScrollVelocityAuthoring : MonoBehaviour
{
    [Tooltip("Scroll direction (will be normalized on bake)")]
    public Vector3 direction = new Vector3(0, 0, 1);
    
    [Tooltip("Scroll speed in units per second")]
    public float speed = 50f;

    public class Baker : Baker<ConstantScrollVelocityAuthoring>
    {
        public override void Bake(ConstantScrollVelocityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            float3 dir = new float3(authoring.direction.x, authoring.direction.y, authoring.direction.z);
            
            // Normalize direction
            if (math.lengthsq(dir) > 0.0001f)
                dir = math.normalize(dir);
            else
                dir = new float3(0, 0, 1); // Default forward
            
            AddComponent(entity, new ConstantScrollVelocityConfig
            {
                direction = dir,
                speed = authoring.speed
            });
        }
    }
}

