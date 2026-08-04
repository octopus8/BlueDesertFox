using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Bakes a finish-line / stop volume from a BoxCollider in a SubScene.
/// Overlap is tested in ECS against the Player Follow Object (Unity trigger callbacks are not used).
/// </summary>
/// <remarks>
/// The BoxCollider must stay <b>disabled</b>. Unity Physics bakes enabled colliders (including
/// triggers) into the collision world, and player obstacle capsule sweeps would hard-stop against
/// this volume before the soft brake can run. Size/center are still read at bake time.
/// </remarks>
[RequireComponent(typeof(BoxCollider))]
public class PlayerStopVolumeAuthoring : MonoBehaviour
{
    [Tooltip("How quickly terrain-relative speed is reduced toward zero once engaged (m/s²). 0 = coast (no brake force).")]
    [Min(0f)]
    [SerializeField] private float deceleration = 50f;

    [Tooltip("When true, braking stays engaged after a full stop so slope gravity cannot re-accelerate.")]
    [SerializeField] private bool holdAfterStop = true;

#if UNITY_EDITOR
    void OnValidate()
    {
        var box = GetComponent<BoxCollider>();
        if (box != null && box.enabled)
        {
            box.enabled = false;
            Debug.LogWarning(
                "[PlayerStopVolume] BoxCollider was enabled on '" + name +
                "'. It must stay disabled so Unity Physics does not bake a blocking obstacle. " +
                "Volume size is still read from the collider at bake time.",
                this);
        }
    }

    void OnDrawGizmosSelected()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null)
            return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        Matrix4x4 local = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
        Gizmos.matrix = local;
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
#endif

    private class Baker : Baker<PlayerStopVolumeAuthoring>
    {
        public override void Bake(PlayerStopVolumeAuthoring authoring)
        {
            var box = authoring.GetComponent<BoxCollider>();
            DependsOn(box);

            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new PlayerStopVolume
            {
                localCenter = box != null ? (float3)box.center : float3.zero,
                halfExtents = box != null
                    ? math.abs((float3)box.size) * 0.5f
                    : new float3(0.5f),
                deceleration = math.max(0f, authoring.deceleration),
                holdAfterStop = authoring.holdAfterStop ? (byte)1 : (byte)0
            });
        }
    }
}

/// <summary>
/// Axis-aligned box in the volume entity's local space (pre-scale). Runtime tests use
/// <see cref="Unity.Transforms.LocalToWorld"/> so non-uniform scale is applied correctly.
/// </summary>
public struct PlayerStopVolume : IComponentData
{
    public float3 localCenter;
    public float3 halfExtents;
    public float deceleration;
    public byte holdAfterStop;
}
