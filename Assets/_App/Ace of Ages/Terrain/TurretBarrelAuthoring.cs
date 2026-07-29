using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

/// <summary>
/// Authoring component for the howitzer_barrel child of a turret dome.
/// Attach this to the howitzer_barrel GameObject inside ConcreteTurret_LOD0.prefab.
///
/// Because StaticObjectHierarchyFlattenUtility removes the ECS parent-child transform
/// hierarchy, the barrel entity ends up with a fully independent world-space LocalTransform.
/// This authoring component captures the barrel's dome-relative position and rotation at
/// bake time so TurretBarrelSystem can reapply them every frame after the dome rotates,
/// and also adds X-axis pitch so the barrel points at the predicted intercept point.
/// </summary>
public class TurretBarrelAuthoring : MonoBehaviour
{
    [Header("Pitch Settings")]
    [Tooltip("Maximum pitch rotation speed in degrees per second. Set to 0 for instant snap.")]
    public float pitchSpeed = 90f;

    [Tooltip("Minimum pitch delta from the model's neutral orientation (degrees). Negative = below neutral.")]
    public float minPitchDegrees = -20f;

    [Tooltip("Maximum pitch delta from the model's neutral orientation (degrees). Positive = above neutral.")]
    public float maxPitchDegrees = 60f;

    [Tooltip("The model-space axis that points toward the barrel tip. Usually (0,0,1). " +
             "Change to (0,-1,0) or (0,1,0) if the barrel doesn't aim correctly.")]
    public Vector3 modelForwardAxis = Vector3.forward;

    /// <summary>Bakes pitch settings, dome entity reference, neutral elevation angle, local offset, and local rotation into a <see cref="TurretBarrelTag"/> component.</summary>
    private class Baker : Baker<TurretBarrelAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(TurretBarrelAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            // The barrel's direct parent in the prefab hierarchy is the Dome GameObject.
            var domeEntity = GetEntity(authoring.transform.parent.gameObject, TransformUsageFlags.Dynamic);

            // Compute the natural elevation angle of the barrel at pitch=0.
            // This is the angle of the model's forward axis (after local rotation is applied)
            // measured in the dome's local YZ plane: atan2(F0.y, F0.z).
            // Stored so TurretBarrelSystem can compute the pitch DELTA needed each frame.
            var localRot = (quaternion)authoring.transform.localRotation;
            var modelFwd = math.normalize((float3)authoring.modelForwardAxis);
            var F0 = math.rotate(localRot, modelFwd);
            float neutralElevationAngle = math.atan2(F0.y, F0.z);

            AddComponent(entity, new TurretBarrelTag
            {
                domeEntity = domeEntity,
                localOffset = authoring.transform.localPosition,
                localRotation = authoring.transform.localRotation,
                neutralElevationAngle = neutralElevationAngle,
                currentPitchAngle = 0f,
                pitchSpeed = authoring.pitchSpeed,
                minPitchAngle = math.radians(authoring.minPitchDegrees),
                maxPitchAngle = math.radians(authoring.maxPitchDegrees)
            });

            if (authoring.GetComponent<MeshRenderer>() != null)
                AddComponent<DisableRendering>(entity);
        }
    }
}

/// <summary>
/// Component placed on a turret barrel entity.
/// Stores the barrel's dome-relative transform and pitch configuration so TurretBarrelSystem
/// can keep the barrel locked to the dome and pitched toward the predicted intercept point,
/// even after the ECS parent-child hierarchy has been flattened.
/// </summary>
public struct TurretBarrelTag : IComponentData
{
    /// <summary>The dome entity this barrel belongs to.</summary>
    public Entity domeEntity;

    /// <summary>Barrel position in dome-local space (from the prefab's localPosition).</summary>
    public float3 localOffset;

    /// <summary>Barrel rotation in dome-local space at neutral pitch (from the prefab's localRotation).</summary>
    public quaternion localRotation;

    /// <summary>
    /// Angle (radians) of the model's forward axis in the dome's local YZ plane at pitch=0.
    /// Precomputed at bake time as atan2(F0.y, F0.z) where F0 = rotate(localRotation, modelForwardAxis).
    /// Used to compute the pitch delta needed to point at the intercept each frame.
    /// </summary>
    public float neutralElevationAngle;

    /// <summary>Current pitch delta in radians, tracked per-entity for smooth interpolation.</summary>
    public float currentPitchAngle;

    /// <summary>Maximum pitch rotation speed in degrees per second.</summary>
    public float pitchSpeed;

    /// <summary>Minimum allowed pitch delta from neutral (radians). Typically negative (dip below neutral).</summary>
    public float minPitchAngle;

    /// <summary>Maximum allowed pitch delta from neutral (radians). Typically positive (rise above neutral).</summary>
    public float maxPitchAngle;
}
