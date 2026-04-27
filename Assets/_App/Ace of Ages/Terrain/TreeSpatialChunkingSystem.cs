using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that assigns spatial chunk membership to trees for efficient LOD update batching.
/// Runs before TreeLODUpdateSystem to keep chunk data current.
/// Uses frame budgeting to spread rechunking work when terrain scrolling is active.
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TreeLODUpdateSystem))]
public partial class TreeSpatialChunkingSystem : SystemBase
{
    private const float ChunkSize = 100f; // Must match TreeLODUpdateSystem.ChunkSize
    private const int MaxRechunksPerFrame = 100; // Limit rechunking to prevent spikes during heavy scrolling
    private bool _initialChunkingDone;
    private int _frameCounter;
    private int _totalRechunkedThisSession;

    protected override void OnCreate()
    {
        RequireForUpdate<TreeLODConfig>();
        _initialChunkingDone = false;
        _frameCounter = 0;
        _totalRechunkedThisSession = 0;
    }

    protected override void OnUpdate()
    {
        _frameCounter++;
        
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
        // Apply frame budgeting to prevent performance spikes
        int treesRechunked = 0;
        int treesProcessed = 0;
        
        foreach (var (transform, chunkMembership, entity) in 
                 SystemAPI.Query<RefRO<LocalTransform>, RefRW<TreeChunkMembership>>()
                     .WithAll<GlobalTreeInstance>()
                     .WithEntityAccess())
        {
            treesProcessed++;
            
            // Apply frame budget to spread work over multiple frames during heavy scrolling
            if (treesRechunked >= MaxRechunksPerFrame)
            {
                break;
            }
            
            int2 currentChunk = GetChunkCoord(transform.ValueRO.Position);
            
            // Update if chunk changed
            if (!math.all(currentChunk == chunkMembership.ValueRO.chunkCoord))
            {
                chunkMembership.ValueRW.chunkCoord = currentChunk;
                treesRechunked++;
            }
        }
        
        _totalRechunkedThisSession += treesRechunked;
        
        // Reduced logging: only log every 5 seconds (300 frames at 60fps) and only if significant rechunking
        if (_frameCounter % 300 == 0 && _totalRechunkedThisSession > 50)
        {
            Debug.Log($"[TreeSpatialChunking] Rechunked {_totalRechunkedThisSession} trees in last 5 seconds (terrain scrolling active)");
            _totalRechunkedThisSession = 0; // Reset counter
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


