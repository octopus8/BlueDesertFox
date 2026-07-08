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
    [Tooltip("Height above the player from which the ground raycast starts.")]
    [SerializeField] private float rayHeightAbove = 2f;
    [Tooltip("Length of the ground raycast below the player.")]
    [SerializeField] private float rayLengthBelow = 50f;
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
    [Tooltip("Max distance below ideal contact to apply spring forces while approaching ground.")]
    [SerializeField] private float approachingSurfaceMaxDistance = 1f;

    [Header("Takeoff / Airborne")]
    [Tooltip("Outward normal speed (m/s) above which ground adhesion is released. 0 = disabled.")]
    [SerializeField] private float takeoffSpeed = 2f;
    [Tooltip("Seconds after takeoff before ground contact can re-engage.")]
    [SerializeField] private float airborneGraceTime = 0.2f;
    [Tooltip("Min horizontal speed (m/s) for crest detection at convex ridge transitions.")]
    [SerializeField] private float minCrestSpeed = 3f;
    [Tooltip("Dot-product threshold between consecutive ground normals to detect a crest (lower = sharper ridge).")]
    [SerializeField] private float crestNormalDotThreshold = 0.92f;

    [Header("Facing")]
    [Tooltip("Seconds to smooth yaw toward movement direction. Higher = less terrain jitter. 0 = instant.")]
    [SerializeField] private float yawRotationSmoothTime = 0.2f;
    [Tooltip("Minimum horizontal speed (m/s) before yaw updates.")]
    [SerializeField] private float minYawSpeed = 0.5f;

    [Header("Head Steering")]
    [Tooltip("Degrees per second of Y-axis velocity rotation at full head roll (±90°). 0 = disabled.")]
    [SerializeField] private float steeringSensitivity = 30f;

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
                rideablePhysicsLayer = authoring.rideablePhysicsLayer,
                springStiffness = authoring.springStiffness,
                springDamping = authoring.springDamping,
                groundFriction = authoring.groundFriction,
                groundedDistance = authoring.groundedDistance,
                approachingSurfaceMaxDistance = authoring.approachingSurfaceMaxDistance,
                takeoffSpeed = authoring.takeoffSpeed,
                airborneGraceTime = authoring.airborneGraceTime,
                minCrestSpeed = authoring.minCrestSpeed,
                crestNormalDotThreshold = authoring.crestNormalDotThreshold,
                yawRotationSmoothTime = authoring.yawRotationSmoothTime,
                minYawSpeed = authoring.minYawSpeed,
                capsuleRadius = capsuleRadius,
                capsuleHalfCylinder = capsuleHalfCylinder,
                capsuleCenter = capsuleCenter
            });

            AddComponent(entity, new PlayerFollowObjectMotionState
            {
                terrainRelativeVelocity = float3.zero,
                smoothedYaw = 0f,
                wasGrounded = 0,
                wasInSurfaceContact = 0,
                airborneTimeRemaining = 0f,
                previousGroundNormal = math.up()
            });

            AddComponent(entity, new PlayerFollowObjectSteeringConfig
            {
                steeringSensitivity = authoring.steeringSensitivity
            });
        }
    }
}

/// <summary>Baked head-tilt steering settings for <see cref="PlayerFollowObjectHeadSteeringSystem"/>.</summary>
public struct PlayerFollowObjectSteeringConfig : IComponentData
{
    public float steeringSensitivity;
}

/// <summary>Tag component that identifies the Player Follow Object entity in the ECS world.</summary>
public struct PlayerFollowObjectTag : IComponentData
{
}
