using Unity.Entities;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Debug system to monitor tree entity counts and cleanup issues.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct TreeCleanupDebugSystem : ISystem
{
    private double _lastLogTime;
    
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TreeTileOwnership>();
        _lastLogTime = 0;
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Log every 2 seconds
        if (state.WorldUnmanaged.Time.ElapsedTime - _lastLogTime < 2.0)
            return;
            
        _lastLogTime = state.WorldUnmanaged.Time.ElapsedTime;
        
        // Count trees
        var treeQuery = SystemAPI.QueryBuilder().WithAll<TreeTileOwnership>().Build();
        int treeCount = treeQuery.CalculateEntityCount();
        
        // Count tiles
        var tileQuery = SystemAPI.QueryBuilder().WithAll<TerrainTile>().Build();
        int tileCount = tileQuery.CalculateEntityCount();
        
        // Count tiles with trees spawned
        var tilesWithTreesQuery = SystemAPI.QueryBuilder()
            .WithAll<TerrainTile, TreesSpawned>().Build();
        int tilesWithTrees = tilesWithTreesQuery.CalculateEntityCount();
        
        // Count orphaned trees (trees whose parent tile doesn't exist)
        int orphanedTrees = 0;
        foreach (var ownership in SystemAPI.Query<RefRO<TreeTileOwnership>>())
        {
            if (!state.EntityManager.Exists(ownership.ValueRO.tileEntity))
            {
                orphanedTrees++;
            }
        }
        
        if (orphanedTrees > 0)
        {
            Debug.LogWarning($"[TreeDebug] Found {orphanedTrees} orphaned trees! " +
                           "Trees exist but their parent tiles have been destroyed.");
        }
    }
}

