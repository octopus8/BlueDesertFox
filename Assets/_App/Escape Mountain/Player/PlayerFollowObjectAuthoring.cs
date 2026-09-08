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
    [Tooltip("Height above the sphere from which the ground SphereCast starts.")]
    [SerializeField] private float rayHeightAbove = 2f;
    [Tooltip("Length of the ground SphereCast below the sphere.")]
    [SerializeField] private float rayLengthBelow = 50f;

    [Tooltip("Metres per second the body may be pushed back out of the ground when penetrating. Fast " +
             "enough that a hard landing recovers within a few frames, slow enough that sustained contact " +
             "against a rising face cannot walk the rider up it.")]
    [Min(0f)]
    [SerializeField] private float maxPenetrationRecoverySpeed = 6f;

    [Header("Ground Motion")]
    [Tooltip("Tangent velocity damping while in contact. Higher = lower terminal slide speed (~g*sin(slope)/friction). 0 = no cap.")]
    [SerializeField] private float groundFriction = 0.25f;

    [Header("Facing")]
    [Tooltip("Seconds to smooth yaw toward movement direction. Higher = less terrain jitter. 0 = instant.")]
    [SerializeField] private float yawRotationSmoothTime = 0.2f;
    [Tooltip("Minimum horizontal speed (m/s) before yaw updates.")]
    [SerializeField] private float minYawSpeed = 0.5f;

    [Header("Head Steering")]
    [Tooltip("Degrees per second of Y-axis velocity rotation at full head roll (±90°). 0 = disabled.")]
    [SerializeField] private float steeringSensitivity = 30f;
    [Tooltip("Fraction of horizontal speed lost at full ±90° head roll (either direction). 0 = off, 1 = full stop.")]
    [Range(0f, 1f)]
    [SerializeField] private float turnDrag = 0f;

    [Header("Pipe")]
    [Tooltip("Unity layer used as a BelongsTo fallback when identifying PipeFace colliders.")]
    [SerializeField] private int pipePhysicsLayer = 15;

    public class Baker : Baker<PlayerFollowObjectAuthoring>
    {
        public override void Bake(PlayerFollowObjectAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerFollowObjectTag>(entity);

            float sphereRadius = 0.5f;
            float3 sphereCenter = float3.zero;

            var sphere = authoring.GetComponent<SphereCollider>();
            if (sphere != null)
            {
                sphereRadius = sphere.radius;
                sphereCenter = sphere.center;
            }
            else
            {
                var capsule = authoring.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    sphereRadius = capsule.radius;
                    sphereCenter = capsule.center;
                }
            }

            AddComponent(entity, new PlayerFollowObjectGroundConfig
            {
                rayHeightAbove = authoring.rayHeightAbove,
                rayLengthBelow = authoring.rayLengthBelow,
                groundFriction = authoring.groundFriction,
                yawRotationSmoothTime = authoring.yawRotationSmoothTime,
                minYawSpeed = authoring.minYawSpeed,
                sphereRadius = math.max(0f, sphereRadius),
                sphereCenter = sphereCenter,
                gravity = (float3)Physics.gravity,
                maxPenetrationRecoverySpeed = math.max(0f, authoring.maxPenetrationRecoverySpeed),
                pipeLayerMask = authoring.pipePhysicsLayer >= 0 && authoring.pipePhysicsLayer < 32
                    ? 1u << authoring.pipePhysicsLayer
                    : 0u
            });

            AddComponent(entity, new PlayerFollowObjectMotionState
            {
                terrainRelativeVelocity = float3.zero,
                smoothedYaw = 0f,
                inContact = 0,
                hasPreviousContact = 0,
                previousGroundNormal = math.up(),
                contactPoint = float3.zero,
                onPipe = 0,
                pipeAirborne = 0
            });

            AddComponent(entity, new PlayerFollowObjectSteeringConfig
            {
                steeringSensitivity = authoring.steeringSensitivity,
                turnDrag = authoring.turnDrag
            });

            AddComponent(entity, new PlayerFollowObjectBrakeState
            {
                active = 0,
                deceleration = 0f,
                holdAfterStop = 0
            });
        }
    }
}

/// <summary>Baked head-tilt steering settings for <see cref="PlayerFollowObjectHeadSteeringSystem"/>.</summary>
public struct PlayerFollowObjectSteeringConfig : IComponentData
{
    public float steeringSensitivity;
    public float turnDrag;
}

/// <summary>
/// Finish-line / stop-volume brake. When <see cref="active"/> is set, locomotion decelerates
/// terrain-relative velocity toward zero and (when <see cref="holdAfterStop"/> is set) keeps it zero.
/// </summary>
public struct PlayerFollowObjectBrakeState : IComponentData
{
    public byte active;
    public float deceleration;
    public byte holdAfterStop;
}

/// <summary>Tag component that identifies the Player Follow Object entity in the ECS world.</summary>
public struct PlayerFollowObjectTag : IComponentData
{
}
