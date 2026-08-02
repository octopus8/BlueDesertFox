using _App.Ace_of_Ages.Terrain;
using Unity.Entities;

/// <summary>
/// Cancels Default-World terrain in-flight work and caches before runtime tiles are destroyed.
/// AutoLoad SubScene reload can replace <see cref="TerrainTileConfig"/> without an empty
/// <c>RequireForUpdate</c> window, so system <c>OnStopRunning</c> may never run — call
/// <see cref="ScrubBeforeDestroyingRuntimeTiles"/> from <see cref="TileSpawningSystem"/> instead.
/// </summary>
public static class TerrainRuntimeReloadUtility
{
    /// <summary>
    /// Completes in-flight mesh/physics jobs, then clears collider blob cache and rendering queues.
    /// Must run before destroying Default-World tile entities on scene reload.
    /// </summary>
    public static void ScrubBeforeDestroyingRuntimeTiles(ref SystemState state)
    {
        var world = state.WorldUnmanaged;
        CompleteInFlightWork(world);
        ClearCaches(world, state.World);
    }

    /// <summary>
    /// Completes and discards cross-frame terrain mesh and BVH jobs that still hold entity/buffer refs.
    /// </summary>
    public static void CompleteInFlightWork(WorldUnmanaged world)
    {
        var meshHandle = world.GetExistingUnmanagedSystem<TerrainMeshScheduleSystem>();
        if (meshHandle != SystemHandle.Null)
        {
            ref var mesh = ref world.GetUnsafeSystemRef<TerrainMeshScheduleSystem>(meshHandle);
            mesh.CancelInFlightAndClearQueues();
        }

        var physicsHandle = world.GetExistingUnmanagedSystem<TerrainPhysicsScheduleSystem>();
        if (physicsHandle != SystemHandle.Null)
        {
            ref var physics = ref world.GetUnsafeSystemRef<TerrainPhysicsScheduleSystem>(physicsHandle);
            physics.CancelInFlightAndClearBatch();
        }
    }

    /// <summary>
    /// Disposes cached collider blobs and clears pending rendering queues / material cache.
    /// </summary>
    public static void ClearCaches(WorldUnmanaged world, World managedWorld)
    {
        var cacheHandle = world.GetExistingUnmanagedSystem<TerrainColliderBlobCacheSystem>();
        if (cacheHandle != SystemHandle.Null)
        {
            ref var cache = ref world.GetUnsafeSystemRef<TerrainColliderBlobCacheSystem>(cacheHandle);
            cache.ClearCache();
        }

        if (managedWorld != null && managedWorld.IsCreated)
        {
            var rendering = managedWorld.GetExistingSystemManaged<TerrainRenderingSystem>();
            rendering?.ClearPendingForReload();
        }
    }
}
