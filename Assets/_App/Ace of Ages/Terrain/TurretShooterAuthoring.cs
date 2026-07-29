using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for turret burst-fire behaviour.
/// Attach to the same barrel GameObject as TurretBarrelAuthoring.
///
/// At bake time the Baker captures the bullet spawn point's local position and rotation
/// relative to the barrel pivot (same pattern as PlayerShipAuthoring.bulletSpawnPoint).
/// At runtime, TurretShooterSystem applies that offset to the barrel's world transform
/// to produce the muzzle world position and forward direction for each shot.
/// </summary>
public class TurretShooterAuthoring : MonoBehaviour
{
    [Header("Spawn Point")]
    [Tooltip("Child GameObject placed at the barrel muzzle tip. Its local transform relative to this barrel " +
             "is baked into the component so the bullet spawns at the correct world position at runtime.")]
    public GameObject bulletSpawnPoint;

    [Header("Burst Settings")]
    [Tooltip("Number of bullets fired in a single burst.")]
    public int bulletsPerBurst = 3;

    [Tooltip("Seconds between individual shots within a burst.")]
    public float burstIntraDelay = 0.15f;

    [Tooltip("Seconds to wait after a full burst before the next one begins.")]
    public float cooldownDuration = 3f;

    [Header("Shoot Gate")]
    [Tooltip("Maximum angle (degrees) between muzzle forward and the intercept aim direction before firing is allowed.")]
    public float maxFireAngleDegrees = 8f;

    [Tooltip("Maximum distance between muzzle and the target before firing is allowed.")]
    public float maxFireDistance = 100;

    /// <summary>Bakes burst-fire settings and the muzzle spawn-point offset/rotation into a <see cref="TurretShooterState"/> component.</summary>
    private class Baker : Baker<TurretShooterAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(TurretShooterAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            float3 spawnLocalOffset = float3.zero;
            quaternion spawnLocalRotation = quaternion.identity;

            if (authoring.bulletSpawnPoint != null)
            {
                spawnLocalOffset = authoring.bulletSpawnPoint.transform.localPosition;
                spawnLocalRotation = authoring.bulletSpawnPoint.transform.localRotation;
            }
            else
            {
                Debug.LogWarning("[TurretShooterAuthoring] bulletSpawnPoint is null — bullets will spawn at the barrel pivot.", authoring);
            }

            // Muzzle position in dome-local space at neutral pitch (for ballistic intercept solve).
            var barrelTransform = authoring.transform;
            float3 launchInDomeLocal = (float3)barrelTransform.localPosition
                + math.rotate((quaternion)barrelTransform.localRotation, spawnLocalOffset);

            AddComponent(entity, new TurretLaunchOffset { domeLocalOffset = launchInDomeLocal });

            AddComponent(entity, new TurretShooterState
            {
                bulletsPerBurst           = authoring.bulletsPerBurst,
                burstIntraDelay           = authoring.burstIntraDelay,
                cooldownDuration          = authoring.cooldownDuration,
                maxFireAngleRadians       = math.radians(authoring.maxFireAngleDegrees),
                maxFireDistance          =  authoring.maxFireDistance,
                spawnLocalOffset          = spawnLocalOffset,
                spawnLocalRotation        = spawnLocalRotation,
                bulletsRemainingInBurst   = authoring.bulletsPerBurst,
                lastShotTime              = 0,
                inCooldown                = false,
                cooldownEndsAt            = 0,
                burstLineOfSightEvaluated = false,
                burstTerrainBlocked       = false
            });
        }
    }
}

/// <summary>
/// Burst-fire state component placed on a turret barrel entity (same entity as TurretBarrelTag).
/// TurretShooterSystem reads this each frame to decide when to fire and manages the burst/cooldown cycle.
/// </summary>
public struct TurretShooterState : IComponentData
{
    // --- Config (baked at build time) ---

    /// <summary>Number of bullets to fire in a single burst.</summary>
    public int bulletsPerBurst;

    /// <summary>Seconds between individual shots within a burst.</summary>
    public float burstIntraDelay;

    /// <summary>Seconds to wait between bursts.</summary>
    public float cooldownDuration;

    /// <summary>Maximum angle (radians) between muzzle forward and intercept aim direction required to fire.</summary>
    public float maxFireAngleRadians;

    /// <summary>Maximum distance between muzzle and target required to fire.</summary>
    public float maxFireDistance;

    /// <summary>Bullet spawn point local position relative to the barrel pivot (baked from a child GameObject).</summary>
    public float3 spawnLocalOffset;

    /// <summary>Bullet spawn point local rotation relative to the barrel pivot (baked from a child GameObject).</summary>
    public quaternion spawnLocalRotation;

    // --- Runtime state ---

    /// <summary>Bullets still to fire in the current burst. When it reaches 0 the cooldown begins.</summary>
    public int bulletsRemainingInBurst;

    /// <summary>ElapsedTime of the last shot fired (used to enforce burstIntraDelay).</summary>
    public double lastShotTime;

    /// <summary>True while the turret is waiting between bursts.</summary>
    public bool inCooldown;

    /// <summary>ElapsedTime at which the current cooldown expires and the next burst begins.</summary>
    public double cooldownEndsAt;

    /// <summary>True after terrain line-of-sight has been raycast for the current burst.</summary>
    public bool burstLineOfSightEvaluated;

    /// <summary>Cached terrain raycast result for the current burst (valid when burstLineOfSightEvaluated is true).</summary>
    public bool burstTerrainBlocked;
}

/// <summary>
/// Baked muzzle position in dome-local space at neutral barrel pitch (barrel localPosition + rotated spawn offset).
/// Added to the barrel entity by <see cref="TurretShooterAuthoring"/>.
/// <see cref="TurretAimingSystem"/> maps this to the parent dome via <see cref="TurretBarrelTag.domeEntity"/>.
/// </summary>
public struct TurretLaunchOffset : IComponentData
{
    public float3 domeLocalOffset;
}
