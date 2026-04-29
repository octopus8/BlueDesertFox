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
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        // Get EntityCommandBuffer from singleton system
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
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
    /// </summary>
    [BurstCompile]
    private partial struct AssignChunkJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ecb;
        
        private void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndex,
                            in LocalTransform transform,
                            in GlobalTreeInstance _)
        {
            int2 chunkCoord = GetChunkCoord(transform.Position);
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
            int2 currentChunk = GetChunkCoord(transform.Position);
            
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
    private static int2 GetChunkCoord(float3 worldPos)
    {
        return new int2(
            (int)math.floor(worldPos.x / ChunkSize),
            (int)math.floor(worldPos.z / ChunkSize)
        );
    }
}


