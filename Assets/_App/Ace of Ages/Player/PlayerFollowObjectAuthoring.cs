using Unity.Entities;
using Unity.Mathematics;
using Sirenix.OdinInspector;
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
    [Tooltip("Physics layer for ramps/halfpipes. Used for ground contact and steep-wall capsule sweeps during movement.")]
    [ValueDropdown(nameof(GetUnityLayerDropdown))]
    [SerializeField] private int rideablePhysicsLayer = 15;

#if UNITY_EDITOR
    private static ValueDropdownItem<int>[] GetUnityLayerDropdown()
    {
        string[] names = UnityEditorInternal.InternalEditorUtility.layers;
        var items = new ValueDropdownItem<int>[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            items[i] = new ValueDropdownItem<int>(names[i], LayerMask.NameToLayer(names[i]));
        }
        return items;
    }
#else
    private static ValueDropdownItem<int>[] GetUnityLayerDropdown() => null;
#endif

    [Header("Suspension")]
    [Tooltip("Natural frequency (Hz) of the ride. Bumps arriving faster than this are absorbed by leg " +
             "travel; slower terrain features pass through to the rider. Bump frequency is speed / " +
             "collider quad size, so at 28 m/s over 5.3 m quads the terrain excites ~5 Hz. Raising this " +
             "toward that band brings back harshness and resonance at mid speed.")]
    [Min(0f)]
    [SerializeField] private float rideFrequency = 1.2f;

    [Tooltip("Suspension damping ratio. 1 = critically damped. Higher settles faster after landings but " +
             "transmits more high-frequency bump energy, so isolation gets worse as this approaches 1.")]
    [Range(0f, 1.5f)]
    [SerializeField] private float rideDampingRatio = 0.5f;

    [Tooltip("Neutral leg length (m) — how far the rider rides above the board's contact point at rest. " +
             "The startup terrain align drops the surface by this much, so the authored position stays " +
             "the rider's rest pose rather than the ground line.")]
    [Min(0f)]
    [SerializeField] private float rideHeight = 0.35f;

    [Tooltip("Extra reach (m) below neutral before the leg runs out and contact is lost. This is the " +
             "bump-versus-ledge threshold: drops shallower than this are swallowed by the leg extending, " +
             "deeper drops launch the rider ballistically.")]
    [Min(0f)]
    [SerializeField] private float maxLegExtension = 0.5f;

    [Tooltip("Squash (m) available above neutral before the suspension bottoms out against its hard stop. " +
             "Clamped to Ride Height. Bottoming out kills the approach rate, which is what makes hard " +
             "landings feel hard and ramp lips launch correctly.")]
    [Min(0f)]
    [SerializeField] private float maxLegCompression = 0.3f;

    [Tooltip("Maximum upward speed (m/s) the contact step may add in one frame. Caps pops from the hard " +
             "stop and damper so a probe discontinuity cannot launch the rider. Ledge launches keep their " +
             "existing upward speed because only the increase is clamped. ~5 m/s is about a 1.3 m pop.")]
    [Min(0f)]
    [SerializeField] private float maxGroundLiftSpeed = 5f;

    [Tooltip("Metres per second the body may be pushed back out of the ground while bottomed out. Fast " +
             "enough that a hard landing recovers within a few frames, slow enough that sustained contact " +
             "against a rising face cannot walk the rider up it.")]
    [Min(0f)]
    [SerializeField] private float maxPenetrationRecoverySpeed = 6f;

    [Tooltip("Half-extent (m) of the contact footprint. Probes fore/aft and left/right at this radius to " +
             "fit a steady ground plane and let the board bridge narrow crests instead of dropping into " +
             "every gap. 0 = single centre ray.")]
    [Min(0f)]
    [SerializeField] private float contactProbeRadius = 0.7f;

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

            float rideHeight = math.max(0f, authoring.rideHeight);

            AddComponent(entity, new PlayerFollowObjectGroundConfig
            {
                bottomOffset = bottomOffset,
                rayHeightAbove = authoring.rayHeightAbove,
                rayLengthBelow = authoring.rayLengthBelow,
                rideablePhysicsLayer = authoring.rideablePhysicsLayer,
                rideFrequency = math.max(0f, authoring.rideFrequency),
                rideDampingRatio = math.max(0f, authoring.rideDampingRatio),
                rideHeight = rideHeight,
                maxLegExtension = math.max(0f, authoring.maxLegExtension),
                maxLegCompression = math.clamp(authoring.maxLegCompression, 0f, rideHeight),
                contactProbeRadius = math.max(0f, authoring.contactProbeRadius),
                groundFriction = authoring.groundFriction,
                yawRotationSmoothTime = authoring.yawRotationSmoothTime,
                minYawSpeed = authoring.minYawSpeed,
                capsuleRadius = capsuleRadius,
                capsuleHalfCylinder = capsuleHalfCylinder,
                capsuleCenter = capsuleCenter,
                gravity = (float3)Physics.gravity,
                maxGroundLiftSpeed = math.max(0f, authoring.maxGroundLiftSpeed),
                maxPenetrationRecoverySpeed = math.max(0f, authoring.maxPenetrationRecoverySpeed)
            });

            AddComponent(entity, new PlayerFollowObjectMotionState
            {
                terrainRelativeVelocity = float3.zero,
                smoothedYaw = 0f,
                inContact = 0,
                legLength = rideHeight,
                previousContactHeight = 0f,
                hasPreviousContact = 0,
                previousGroundNormal = math.up(),
                contactPoint = float3.zero,
                lastSurfaceVerticalRate = 0f,
                lastContactLiftSpeed = 0f,
                lastLiftWasClamped = 0,
                wallSlideActive = 0,
                previousOnRideable = 0
            });

            AddComponent(entity, new PlayerFollowObjectSteeringConfig
            {
                steeringSensitivity = authoring.steeringSensitivity,
                turnDrag = authoring.turnDrag
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

/// <summary>Tag component that identifies the Player Follow Object entity in the ECS world.</summary>
public struct PlayerFollowObjectTag : IComponentData
{
}
