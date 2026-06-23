using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that assigns spatial chunk membership to trees for efficient LOD update batching.
/// Runs after static object positions are updated so chunk coords match scrolled world positions.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(TransformSystemGroup))]
[UpdateAfter(typeof(objectPositionUpdateSystem))]
[UpdateBefore(typeof(StaticObjectLODUpdateSystem))]
[BurstCompile]
public partial struct TreeSpatialChunkingSystem : ISystem
{
    /// <summary>Registers <see cref="StaticObjectLODConfig"/> requirement.</summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectLODConfig>();
    }

    /// <summary>
    /// Assigns missing chunk membership on the main thread, then schedules a parallel job to recompute
    /// chunk coordinates for trees whose positions changed due to terrain scrolling.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (transform, entity) in SystemAPI
                     .Query<RefRO<LocalTransform>>()
                     .WithAll<GlobalStaticObjectInstance>()
                     .WithNone<StaticObjectChunkMembership>()
                     .WithEntityAccess())
        {
            state.EntityManager.AddComponentData(entity, new StaticObjectChunkMembership
            {
                chunkCoord = StaticObjectSpatialChunkUtility.GetChunkCoord(transform.ValueRO.Position)
            });
        }

        var updateJob = new UpdateChunkJob();
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
    }
    
    /// <summary>
    /// Burst-compiled job that updates chunk membership for trees that have moved.
    /// </summary>
    [BurstCompile]
    private partial struct UpdateChunkJob : IJobEntity
    {
        /// <summary>Recomputes the chunk coordinate for a tree that already has <see cref="StaticObjectChunkMembership"/> and updates it in-place.</summary>
        private void Execute(in LocalTransform transform,
                            ref StaticObjectChunkMembership chunkMembership,
                            in GlobalStaticObjectInstance _)
        {
            int2 currentChunk = StaticObjectSpatialChunkUtility.GetChunkCoord(transform.Position);
            
            if (!math.all(currentChunk == chunkMembership.chunkCoord))
                chunkMembership.chunkCoord = currentChunk;
        }
    }
}

