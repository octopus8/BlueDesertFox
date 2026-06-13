using Unity.Entities;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Debug system to monitor tree entity counts and cleanup issues.
/// Enable/disable via StaticObjectSpawnerConfigAuthoring.enableObjectLODDebug flag.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TileSpawningSystem))]
public partial struct TreeCleanupDebugSystem : ISystem
{
    private double _lastLogTime;
    
    /// <summary>Registers <see cref="StaticObjectTileOwnership"/> and <see cref="StaticObjectLODConfig"/> requirements and resets the log timer.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectTileOwnership>();
        state.RequireForUpdate<StaticObjectLODConfig>();
        _lastLogTime = 0;
    }
    
    /// <summary>
    /// Every two seconds (when <see cref="StaticObjectLODConfig.enableObjectLODDebug"/> is true),
    /// logs a count of all static-object entities with <see cref="StaticObjectTileOwnership"/> to
    /// the Console to help diagnose tile-cleanup leaks.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        // Early exit if debug logging is disabled
        var lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();
        if (!lodConfig.enableObjectLODDebug)
            return;
        
        // Log every 2 seconds
        if (state.WorldUnmanaged.Time.ElapsedTime - _lastLogTime < 2.0)
            return;
            
        _lastLogTime = state.WorldUnmanaged.Time.ElapsedTime;
        
        // Count trees
        var treeQuery = SystemAPI.QueryBuilder().WithAll<StaticObjectTileOwnership>().Build();
        int objectCount = treeQuery.CalculateEntityCount();
        
        // Count tiles
        var tileQuery = SystemAPI.QueryBuilder().WithAll<TerrainTile>().Build();
        int tileCount = tileQuery.CalculateEntityCount();
        
        // Count tiles with trees spawned
        var tilesWithTreesQuery = SystemAPI.QueryBuilder()
            .WithAll<TerrainTile, StaticObjectsSpawned>().Build();
        int tilesWithTrees = tilesWithTreesQuery.CalculateEntityCount();
        
        // Count orphaned trees (trees whose parent tile doesn't exist)
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
            Debug.LogWarning($"[TreeDebug] Found {orphanedTrees} orphaned trees! " +
                           "Trees exist but their parent tiles have been destroyed.");
        }
    }
}
