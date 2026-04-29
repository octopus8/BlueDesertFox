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

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TreeLODConfig>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

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
    /// Explicitly excludes entities that already have TreeChunkMembership to prevent race conditions
    /// where entities are destroyed between job scheduling and ECB playback.
    /// </summary>
    [BurstCompile]
    [WithNone(typeof(TreeChunkMembership))]
    private partial struct AssignChunkJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;
        
        private void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex,
                            in LocalTransform transform,
                            in GlobalTreeInstance _)
        {
            GetChunkCoord(in transform.Position, out int2 chunkCoord);
            ecb.AddComponent(chunkIndex, entity, new TreeChunkMembership
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
        private void Execute(in LocalTransform transform,
                            ref TreeChunkMembership chunkMembership,
                            in GlobalTreeInstance _)
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

