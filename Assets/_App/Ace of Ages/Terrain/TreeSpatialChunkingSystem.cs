using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that assigns spatial chunk membership to trees for efficient LOD update batching.
/// Runs before TreeLODUpdateSystem to keep chunk data current.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TreeLODUpdateSystem))]
public partial class TreeSpatialChunkingSystem : SystemBase
{
    private const float ChunkSize = 100f; // Must match TreeLODUpdateSystem.ChunkSize
    private bool _initialChunkingDone;

    protected override void OnCreate()
    {
        RequireForUpdate<TreeLODConfig>();
        _initialChunkingDone = false;
    }

    protected override void OnUpdate()
    {
        // Check for trees that don't have chunk membership yet
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        
        int treesChunked = 0;
        
        // Assign chunk membership to trees that don't have it yet
        foreach (var (transform, entity) in 
                 SystemAPI.Query<RefRO<LocalTransform>>()
                     .WithAll<GlobalTreeInstance>()
                     .WithNone<TreeChunkMembership>()
                     .WithEntityAccess())
        {
            int2 chunkCoord = GetChunkCoord(transform.ValueRO.Position);
            
            ecb.AddComponent(entity, new TreeChunkMembership
            {
                chunkCoord = chunkCoord
            });
            
            treesChunked++;
        }
        
        ecb.Playback(EntityManager);
        ecb.Dispose();
        
        // Log initial chunking completion
        if (!_initialChunkingDone && treesChunked > 0)
        {
            Debug.Log($"[TreeSpatialChunking] Assigned chunk membership to {treesChunked} trees");
            _initialChunkingDone = true;
        }
        
        // Update chunk membership for trees that moved (e.g., due to terrain scrolling)
        // This is important for scrolling terrain scenarios
        int treesRechunked = 0;
        
        foreach (var (transform, chunkMembership, entity) in 
                 SystemAPI.Query<RefRO<LocalTransform>, RefRW<TreeChunkMembership>>()
                     .WithAll<GlobalTreeInstance>()
                     .WithEntityAccess())
        {
            int2 currentChunk = GetChunkCoord(transform.ValueRO.Position);
            
            // Update if chunk changed
            if (!math.all(currentChunk == chunkMembership.ValueRO.chunkCoord))
            {
                chunkMembership.ValueRW.chunkCoord = currentChunk;
                treesRechunked++;
            }
        }
        
        // Log rechunking if significant (indicates terrain scrolling or tree movement)
        if (treesRechunked > 10)
        {
            Debug.Log($"[TreeSpatialChunking] Rechunked {treesRechunked} trees (likely due to terrain scrolling)");
        }
    }
    
    /// <summary>
    /// Calculate chunk coordinate from world position.
    /// </summary>
    private static int2 GetChunkCoord(float3 worldPos)
    {
        return new int2(
            (int)math.floor(worldPos.x / ChunkSize),
            (int)math.floor(worldPos.z / ChunkSize)
        );
    }
}


