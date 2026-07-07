using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using CapsuleCollider = UnityEngine.CapsuleCollider;

/// <summary>
/// Authoring component for the Player Follow Object entity. Bakes ground-contact config
/// used by <see cref="PlayerFollowObjectGroundContactSystem"/> and a tag for
/// <see cref="PlayerFollowObjectSyncSystem"/>.
/// </summary>
public class PlayerFollowObjectAuthoring : MonoBehaviour
{
    [Header("Ground Raycast")]
    [SerializeField] private float rayHeightAbove = 2f;
    [SerializeField] private float rayLengthBelow = 50f;
    [SerializeField] private int fallbackTerrainLayer = 11;
    [Tooltip("Physics layer for ramps and other rideable launch surfaces (not blocking obstacles).")]
    [SerializeField] private int rideablePhysicsLayer = 15;

    [Header("Spring-Damper")]
    [SerializeField] private float springStiffness = 400f;
    [SerializeField] private float springDamping = 35f;

    [Header("Ground Motion")]
    [Tooltip("Tangent velocity damping while grounded. Higher = lower terminal slide speed (~g*sin(slope)/friction). 0 = no cap.")]
    [SerializeField] private float groundFriction = 0.25f;
    [Tooltip("Max distance to terrain surface before considered grounded.")]
    [SerializeField] private float groundedDistance = 0.25f;

    public class Baker : Baker<PlayerFollowObjectAuthoring>
    {
        public override void Bake(PlayerFollowObjectAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerFollowObjectTag>(entity);

            var capsule = authoring.GetComponent<CapsuleCollider>();
            float capsuleRadius = 0.5f;
            float capsuleHalfCylinder = 0f;
            float3 capsuleCenter = float3.zero;
            float bottomOffset = 0.5f;

            if (capsule != null)
            {
                capsuleRadius = capsule.radius;
                capsuleHalfCylinder = math.max(0f, capsule.height * 0.5f - capsule.radius);
                capsuleCenter = capsule.center;
                bottomOffset = capsule.height * 0.5f - capsule.center.y;
            }

            AddComponent(entity, new PlayerFollowObjectGroundConfig
            {
                bottomOffset = bottomOffset,
                rayHeightAbove = authoring.rayHeightAbove,
                rayLengthBelow = authoring.rayLengthBelow,
                fallbackTerrainLayer = authoring.fallbackTerrainLayer,
                rideablePhysicsLayer = authoring.rideablePhysicsLayer,
                springStiffness = authoring.springStiffness,
                springDamping = authoring.springDamping,
                groundFriction = authoring.groundFriction,
                groundedDistance = authoring.groundedDistance,
                capsuleRadius = capsuleRadius,
                capsuleHalfCylinder = capsuleHalfCylinder,
                capsuleCenter = capsuleCenter
            });

            AddComponent(entity, new PlayerFollowObjectMotionState
            {
                velocity = float3.zero,
                wasGrounded = 0
            });
        }
    }
}

/// <summary>Tag component that identifies the Player Follow Object entity in the ECS world.</summary>
public struct PlayerFollowObjectTag : IComponentData
{
}
