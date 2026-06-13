using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that assigns spatial chunk membership to trees for efficient LOD update batching.
/// Runs before TreeLODUpdateSystem to keep chunk data current.
/// Uses parallel jobs for maximum performance with zero frame budgeting overhead.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TreeLODUpdateSystem))]
[BurstCompile]
public partial struct TreeSpatialChunkingSystem : ISystem
{
    private const float ChunkSize = 100f; // Must match TreeLODUpdateSystem.ChunkSize

    /// <summary>Registers <see cref="StaticObjectLODConfig"/> and <see cref="EndSimulationEntityCommandBufferSystem.Singleton"/> requirements.</summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectLODConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    /// <summary>
    /// Schedules two parallel jobs: one to assign <see cref="StaticObjectChunkMembership"/> to newly
    /// spawned trees, and one to recompute chunk coordinates for already-assigned trees whose positions
    /// may have changed due to terrain scrolling.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        // Use EndSimulationECB to ensure playback happens in same frame as job execution
        // This prevents race conditions where entities are destroyed before ECB playback
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        
        // Job 1: Assign chunk membership to new trees (parallel)
        var assignJob = new AssignChunkJob
        {
            ecb = ecb.AsParallelWriter()
        };
        state.Dependency = assignJob.ScheduleParallel(state.Dependency);
        
        // Job 2: Update existing chunk memberships (parallel)
        var updateJob = new UpdateChunkJob();
        state.Dependency = updateJob.ScheduleParallel(state.Dependency);
    }
    
    /// <summary>
    /// Burst-compiled job that assigns chunk membership to trees that don't have it yet.
    /// Explicitly excludes entities that already have StaticObjectChunkMembership to prevent race conditions
    /// where entities are destroyed between job scheduling and ECB playback.
    /// </summary>
    [BurstCompile]
    [WithNone(typeof(StaticObjectChunkMembership))]
    private partial struct AssignChunkJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;
        
        /// <summary>Computes the chunk coordinate for a new tree and adds a <see cref="StaticObjectChunkMembership"/> component via ECB.</summary>
        private void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex,
                            in LocalTransform transform,
                            in GlobalStaticObjectInstance _)
        {
            GetChunkCoord(in transform.Position, out int2 chunkCoord);
            ecb.AddComponent(chunkIndex, entity, new StaticObjectChunkMembership
            {
                chunkCoord = chunkCoord
            });
        }
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
            GetChunkCoord(in transform.Position, out int2 currentChunk);
            
            // Cache-friendly: only write if changed
            if (!math.all(currentChunk == chunkMembership.chunkCoord))
            {
                chunkMembership.chunkCoord = currentChunk;
            }
        }
    }
    
    /// <summary>
    /// Calculate chunk coordinate from world position.
    /// </summary>
    [BurstCompile]
    private static void GetChunkCoord(in float3 worldPos, out int2 result)
    {
        result = new int2(
            (int)math.floor(worldPos.x / ChunkSize),
            (int)math.floor(worldPos.z / ChunkSize)
        );
    }
}

