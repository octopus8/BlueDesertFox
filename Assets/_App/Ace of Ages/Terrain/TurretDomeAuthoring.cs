using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

/// <summary>
/// Authoring component for the rotating dome part of a turret.
/// Attach this to the Dome child GameObject inside the ConcreteTurret_LOD0 prefab.
/// The TurretAimingSystem will rotate this entity's Y axis to lead the player's position
/// using a ballistic intercept calculation.
/// </summary>
public class TurretDomeAuthoring : MonoBehaviour
{
    /// <summary>Speed of bullets fired from this turret in units per second, used to compute the ballistic intercept lead angle.</summary>
    [Tooltip("Speed of bullets fired from this turret (units/second). Used to compute the predictive lead angle.")]
    public float bulletSpeed = 30f;

    /// <summary>Maximum rotation speed of the dome in degrees per second. Set to 0 for instant snap-to-aim.</summary>
    [Tooltip("Maximum rotation speed of the dome (degrees/second). Set to 0 for instant snap.")]
    public float rotationSpeed = 90f;

    /// <summary>Bakes bullet speed and rotation speed into a <see cref="TurretDome"/> component on the dome entity.</summary>
    private class Baker : Baker<TurretDomeAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(TurretDomeAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TurretDome
            {
                bulletSpeed = authoring.bulletSpeed,
                rotationSpeed = authoring.rotationSpeed,
                currentYAngle = 0f
            });

            if (authoring.GetComponent<MeshRenderer>() != null)
                AddComponent<DisableRendering>(entity);
        }
    }
}

/// <summary>
/// Component marking a turret dome entity that should rotate to predictively aim at the player.
/// Placed on the Dome child entity of a turret.
/// TurretAimingSystem reads player position and terrain scroll velocity to solve the
/// ballistic intercept equation and updates LocalTransform.Rotation (Y axis only) each frame.
/// </summary>
public struct TurretDome : IComponentData
{
    /// <summary>Speed of the turret's bullets in units per second. Used in the intercept calculation.</summary>
    public float bulletSpeed;

    /// <summary>Maximum rotation speed in degrees per second. Set to 0 for instant aim.</summary>
    public float rotationSpeed;

    /// <summary>Current Y-axis angle in radians, tracked per entity for smooth interpolation.</summary>
    public float currentYAngle;

    /// <summary>
    /// 3D world-space intercept point written each frame by TurretAimingSystem (solved from muzzle when
    /// <see cref="TurretLaunchOffset"/> is baked on the barrel). Read by TurretBarrelSystem and TurretShooterSystem.
    /// </summary>
    public float3 interceptPoint;
}
