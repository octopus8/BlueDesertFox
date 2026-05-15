using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for the rotating dome part of a turret.
/// Attach this to the Dome child GameObject inside the ConcreteTurret_LOD0 prefab.
/// The TurretAimingSystem will rotate this entity's Y axis to lead the player's position
/// using a ballistic intercept calculation.
/// </summary>
public class TurretDomeAuthoring : MonoBehaviour
{
    [Tooltip("Speed of bullets fired from this turret (units/second). Used to compute the predictive lead angle.")]
    public float bulletSpeed = 30f;

    [Tooltip("Maximum rotation speed of the dome (degrees/second). Set to 0 for instant snap.")]
    public float rotationSpeed = 90f;

    private class Baker : Baker<TurretDomeAuthoring>
    {
        public override void Bake(TurretDomeAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new TurretDome
            {
                bulletSpeed = authoring.bulletSpeed,
                rotationSpeed = authoring.rotationSpeed,
                currentYAngle = 0f
            });
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
    /// 3D world-space intercept point written each frame by TurretAimingSystem.
    /// Read by TurretBarrelSystem to compute the barrel's pitch angle.
    /// </summary>
    public float3 interceptPoint;
}
