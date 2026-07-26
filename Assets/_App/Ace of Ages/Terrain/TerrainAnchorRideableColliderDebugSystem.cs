using Unity.Entities;
using Unity.Physics;
using UnityEngine;

/// <summary>
/// One-shot play-mode warning when a TerrainAnchor's PhysicsCollider blob failed to bake — the
/// render mesh still scrolls but Rideable casts pass through.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct TerrainAnchorRideableColliderDebugSystem : ISystem
{
    private bool _logged;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainAnchorTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        if (_logged)
            return;

        _logged = true;

        int broken = 0;
        int ready = 0;

        foreach (var (collider, _) in SystemAPI
                     .Query<RefRO<PhysicsCollider>>()
                     .WithAll<TerrainAnchorTag>()
                     .WithEntityAccess())
        {
            if (collider.ValueRO.Value.IsCreated)
                ready++;
            else
                broken++;
        }

        if (broken > 0)
        {
            Debug.LogError(
                $"[TerrainAnchor] {broken} TerrainAnchor PhysicsCollider(s) have no baked blob. " +
                "Rideable casts will pass through. Enable Read/Write on the MeshCollider mesh, keep a " +
                "kinematic Rigidbody, then rebake the SubScene.");
        }
        else if (ready == 0)
        {
            Debug.LogWarning(
                "[TerrainAnchor] No TerrainAnchor entities have a PhysicsCollider. Scrolling Rideable " +
                "meshes (e.g. Quaterpipe) need MeshCollider + kinematic Rigidbody baked into the SubScene.");
        }
    }
}
