using Unity.Entities;
using UnityEngine;

/// <summary>
/// Detects static objects whose parent tile entity no longer exists (cleanup leak).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct TreeCleanupDebugSystem : ISystem
{
    private double _lastLogTime;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectTileOwnership>();
        _lastLogTime = 0;
    }

    public void OnUpdate(ref SystemState state)
    {
        if (state.WorldUnmanaged.Time.ElapsedTime - _lastLogTime < 2.0)
            return;

        _lastLogTime = state.WorldUnmanaged.Time.ElapsedTime;

        int orphanedTrees = 0;
        foreach (var ownership in SystemAPI.Query<RefRO<StaticObjectTileOwnership>>())
        {
            if (!state.EntityManager.Exists(ownership.ValueRO.tileEntity))
            {
                orphanedTrees++;
            }
        }

        if (orphanedTrees > 0)
        {
            Debug.LogWarning($"[StaticObjectCleanup] Found {orphanedTrees} orphaned static objects! " +
                           "Objects exist but their parent tiles have been destroyed.");
        }
    }
}
