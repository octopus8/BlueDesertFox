using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using MeshCollider = Unity.Physics.MeshCollider;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// Optimized system that creates physics colliders for terrain tiles with LOD support, caching, and frame budgeting.
/// Three-phase architecture:
/// 1. Cache lookup and sorting by priority
/// 2. Main-thread MeshCollider.Create() with frame budget limit
/// 3. LRU cache eviction when memory threshold exceeded
/// Target performance: <5ms during origin shifts (measured via profiler markers)
/// </summary>
[RequireMatchingQueriesForUpdate]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainColliderPreparationSystem))]
public partial class TerrainPhysicsSystem : SystemBase
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_CacheLookupMarker = new ProfilerMarker("TerrainPhysics.CacheLookup");
    private static readonly ProfilerMarker s_ColliderCreationMarker = new ProfilerMarker("TerrainPhysics.ColliderCreation");
    private static readonly ProfilerMarker s_LRUEvictionMarker = new ProfilerMarker("TerrainPhysics.LRUEviction");
    private static readonly ProfilerMarker s_QueueClearMarker = new ProfilerMarker("TerrainPhysics.QueueClear");
#endif

    private NativeHashMap<ColliderCacheKey, ColliderCacheEntry> _colliderCache;
    private long _totalCacheMemoryBytes;
    private long _currentFrameNumber;

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileConfig>();
        
        // Initialize LRU cache
        _colliderCache = new NativeHashMap<ColliderCacheKey, ColliderCacheEntry>(256, Allocator.Persistent);
        _totalCacheMemoryBytes = 0;
        _currentFrameNumber = 0;
        
        // Subscribe to origin shift events for queue clearing
        FloatingOriginEvents.OnNonPlayerOriginShifted += OnOriginShifted;
    }

    protected override void OnUpdate()
    {
        _currentFrameNumber++;
        
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        
        // Phase 1: Query tiles with prepared collider data and sort by priority
        var preparedQuery = GetEntityQuery(
            ComponentType.ReadOnly<PhysicsColliderPrepared>(),
            ComponentType.ReadOnly<TerrainTile>(),
            ComponentType.ReadWrite<ColliderPreparedVertexElement>(),
            ComponentType.ReadWrite<ColliderPreparedTriangleElement>()
        );
        
        var preparedEntities = preparedQuery.ToEntityArray(Allocator.Temp);
        
        if (preparedEntities.Length == 0)
        {
            preparedEntities.Dispose();
            return;
        }
        
        // Sort entities by priority (distance-based, lower = closer = higher priority)
        var sortedEntities = new NativeArray<EntityWithPriority>(preparedEntities.Length, Allocator.Temp);
        for (int i = 0; i < preparedEntities.Length; i++)
        {
            var prepared = EntityManager.GetComponentData<PhysicsColliderPrepared>(preparedEntities[i]);
            sortedEntities[i] = new EntityWithPriority
            {
                entity = preparedEntities[i],
                priority = prepared.priority
            };
        }
        sortedEntities.Sort(new PriorityComparer());
        
        preparedEntities.Dispose();
        
        // Phase 2: Create colliders with frame budget limit
        int collidersCreatedThisFrame = 0;
        int maxPerFrame = math.max(1, config.maxCollidersCreatedPerFrame);
        
#if UNITY_EDITOR
        using (s_ColliderCreationMarker.Auto())
#endif
        {
            for (int i = 0; i < sortedEntities.Length && collidersCreatedThisFrame < maxPerFrame; i++)
            {
                Entity entity = sortedEntities[i].entity;
                
                if (!EntityManager.Exists(entity))
                    continue;
                
                var prepared = EntityManager.GetComponentData<PhysicsColliderPrepared>(entity);
                var tile = EntityManager.GetComponentData<TerrainTile>(entity);
                
                // Calculate cache key
                var cacheKey = ColliderCacheKey.FromConfig(config, prepared.lodLevel);
                
#if UNITY_EDITOR
                using (s_CacheLookupMarker.Auto())
#endif
                {
                    // Check cache for existing BlobAsset
                    if (_colliderCache.TryGetValue(cacheKey, out var cacheEntry))
                    {
                        // Cache hit - reuse existing collider
                        cacheEntry.lastAccessFrame = _currentFrameNumber;
                        _colliderCache[cacheKey] = cacheEntry;
                        
                        // Create PhysicsCollider from cached blob data
                        CreatePhysicsColliderFromCache(entity, cacheEntry.blobAsset, prepared.lodLevel, config);
                        
                        // Clean up prepared buffers
                        EntityManager.RemoveComponent<PhysicsColliderPrepared>(entity);
                        
                        collidersCreatedThisFrame++;
                        continue;
                    }
                }
                
                // Cache miss - create new collider from prepared data
                var vertexBuffer = EntityManager.GetBuffer<ColliderPreparedVertexElement>(entity);
                var triangleBuffer = EntityManager.GetBuffer<ColliderPreparedTriangleElement>(entity);
                
                if (vertexBuffer.Length == 0 || triangleBuffer.Length == 0)
                {
                    Debug.LogWarning($"[TerrainPhysics] Entity {entity.Index} has empty prepared buffers, skipping");
                    EntityManager.RemoveComponent<PhysicsColliderPrepared>(entity);
                    continue;
                }
                
                // Convert buffers to NativeArrays for MeshCollider.Create()
                var vertices = new NativeArray<float3>(vertexBuffer.Length, Allocator.Temp);
                var triangles = new NativeArray<int3>(triangleBuffer.Length, Allocator.Temp);
                
                for (int v = 0; v < vertexBuffer.Length; v++)
                {
                    vertices[v] = vertexBuffer[v].value;
                }
                
                for (int t = 0; t < triangleBuffer.Length; t++)
                {
                    triangles[t] = triangleBuffer[t].value;
                }
                
                // Create MeshCollider on main thread (cannot be Burst-compiled)
                try
                {
                    var collider = MeshCollider.Create(
                        vertices,
                        triangles,
                        CreateCollisionFilter(prepared.lodLevel, config),
                        Unity.Physics.Material.Default
                    );
                    
                    // Add PhysicsCollider component
                    EntityManager.AddComponentData(entity, new PhysicsCollider { Value = collider });
                    
                    // Add PhysicsWorldIndex if not present
                    if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
                    {
                        EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex());
                    }
                    
                    // Mark as valid (survives origin shifts)
                    EntityManager.AddComponent<PhysicsColliderValid>(entity);
                    
                    // Create BlobAsset for caching
                    var blobAsset = TerrainColliderBlob.Create(vertices, triangles, prepared.lodLevel, Allocator.Persistent);
                    
                    // Estimate memory usage: vertexCount * 12 bytes + triangleCount * 12 bytes
                    int estimatedMemory = vertices.Length * 12 + triangles.Length * 12;
                    
                    // Add to cache
                    _colliderCache[cacheKey] = new ColliderCacheEntry
                    {
                        blobAsset = blobAsset,
                        lastAccessFrame = _currentFrameNumber,
                        estimatedMemoryBytes = estimatedMemory
                    };
                    
                    _totalCacheMemoryBytes += estimatedMemory;
                    
                    collidersCreatedThisFrame++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TerrainPhysics] Failed to create collider for entity {entity.Index}: {e.Message}");
                }
                finally
                {
                    vertices.Dispose();
                    triangles.Dispose();
                }
                
                // Remove prepared component
                EntityManager.RemoveComponent<PhysicsColliderPrepared>(entity);
            }
        }
        
        sortedEntities.Dispose();
        
        // Phase 3: LRU cache eviction if memory threshold exceeded
        long maxMemoryBytes = (long)config.maxColliderCacheMemoryMB * 1024 * 1024;
        if (_totalCacheMemoryBytes > maxMemoryBytes)
        {
            EvictLRUEntries(maxMemoryBytes);
        }
    }

    /// <summary>
    /// Creates a PhysicsCollider from cached BlobAsset data.
    /// </summary>
    private void CreatePhysicsColliderFromCache(Entity entity, BlobAssetReference<TerrainColliderBlob> cachedBlob, TerrainPhysicsLODLevel lodLevel, TerrainTileConfig config)
    {
        ref var blobData = ref cachedBlob.Value;
        
        // Convert blob arrays to NativeArrays
        var vertices = new NativeArray<float3>(blobData.vertexCount, Allocator.Temp);
        var triangles = new NativeArray<int3>(blobData.triangleCount, Allocator.Temp);
        
        for (int i = 0; i < blobData.vertexCount; i++)
        {
            vertices[i] = blobData.vertices[i];
        }
        
        for (int i = 0; i < blobData.triangleCount; i++)
        {
            triangles[i] = blobData.triangles[i];
        }
        
        // Create collider
        var collider = MeshCollider.Create(
            vertices,
            triangles,
            CreateCollisionFilter(lodLevel, config),
            Unity.Physics.Material.Default
        );
        
        EntityManager.AddComponentData(entity, new PhysicsCollider { Value = collider });
        
        if (!EntityManager.HasComponent<PhysicsWorldIndex>(entity))
        {
            EntityManager.AddSharedComponent(entity, new PhysicsWorldIndex());
        }
        
        EntityManager.AddComponent<PhysicsColliderValid>(entity);
        
        vertices.Dispose();
        triangles.Dispose();
    }

    /// <summary>
    /// Creates collision filter based on LOD level and configuration.
    /// Low-detail tiles (half/quarter resolution) use separate physics layer if enabled.
    /// </summary>
    private CollisionFilter CreateCollisionFilter(TerrainPhysicsLODLevel lodLevel, TerrainTileConfig config)
    {
        uint layerMask;
        
        if (config.usePhysicsLODLayers && lodLevel >= TerrainPhysicsLODLevel.HalfResolution)
        {
            // Use low-detail layer for distant tiles
            layerMask = 1u << config.lowDetailPhysicsLayer;
        }
        else
        {
            // Use default layer for close tiles
            layerMask = 1u << 0;
        }
        
        return new CollisionFilter
        {
            BelongsTo = layerMask,
            CollidesWith = ~0u, // Collide with everything
            GroupIndex = 0
        };
    }

    /// <summary>
    /// Evicts oldest cache entries until memory usage is below 75% of max.
    /// </summary>
    private void EvictLRUEntries(long maxMemoryBytes)
    {
#if UNITY_EDITOR
        using (s_LRUEvictionMarker.Auto())
#endif
        {
            long targetMemory = (long)(maxMemoryBytes * 0.75f);
            
            // Collect all cache entries with their keys
            var entries = new NativeList<CacheEntryWithKey>(Allocator.Temp);
            
            foreach (var kvp in _colliderCache)
            {
                entries.Add(new CacheEntryWithKey
                {
                    key = kvp.Key,
                    entry = kvp.Value
                });
            }
            
            // Sort by lastAccessFrame ascending (oldest first)
            entries.Sort(new LRUComparer());
            
            // Evict oldest entries until below target
            long memoryFreed = 0;
            int entriesEvicted = 0;
            
            for (int i = 0; i < entries.Length && _totalCacheMemoryBytes > targetMemory; i++)
            {
                var entryWithKey = entries[i];
                
                // Dispose BlobAsset
                if (entryWithKey.entry.blobAsset.IsCreated)
                {
                    entryWithKey.entry.blobAsset.Dispose();
                }
                
                // Remove from cache
                _colliderCache.Remove(entryWithKey.key);
                
                _totalCacheMemoryBytes -= entryWithKey.entry.estimatedMemoryBytes;
                memoryFreed += entryWithKey.entry.estimatedMemoryBytes;
                entriesEvicted++;
            }
            
            entries.Dispose();
            
#if UNITY_EDITOR
            if (entriesEvicted > 0)
            {
                Debug.Log($"[TerrainPhysics] LRU Eviction: Freed {memoryFreed / 1024}KB by evicting {entriesEvicted} cache entries");
            }
#endif
        }
    }

    /// <summary>
    /// Called when origin shift occurs - clears pending collider creation queue and re-evaluates LOD levels.
    /// </summary>
    private void OnOriginShifted(float3 offset)
    {
#if UNITY_EDITOR
        using (s_QueueClearMarker.Auto())
#endif
        {
            // Count tiles with prepared colliders before clearing
            var preparedQuery = GetEntityQuery(ComponentType.ReadOnly<PhysicsColliderPrepared>());
            int clearedCount = preparedQuery.CalculateEntityCount();
            
            // Remove PhysicsColliderPrepared from all entities (clear queue)
            EntityManager.RemoveComponent<PhysicsColliderPrepared>(preparedQuery);
            
            // Re-evaluate LOD levels for tiles with valid colliders
            // TerrainDistanceTrackingSystem will handle adding PhysicsColliderNeedsPreparation if LOD changed
            
            Debug.Log($"[TerrainPhysics] Clearing physics creation queue due to origin shift: {clearedCount} tiles cleared, will re-prioritize based on new distances");
        }
    }

    protected override void OnDestroy()
    {
        // Unsubscribe from events
        FloatingOriginEvents.OnNonPlayerOriginShifted -= OnOriginShifted;
        
        // Dispose all cached BlobAssets
        foreach (var kvp in _colliderCache)
        {
            if (kvp.Value.blobAsset.IsCreated)
            {
                kvp.Value.blobAsset.Dispose();
            }
        }
        
        _colliderCache.Dispose();
    }
}

/// <summary>
/// Helper struct for sorting entities by priority.
/// </summary>
struct EntityWithPriority
{
    public Entity entity;
    public int priority;
}

/// <summary>
/// Comparer for sorting entities by priority (ascending - lower = higher priority).
/// </summary>
struct PriorityComparer : IComparer<EntityWithPriority>
{
    public int Compare(EntityWithPriority a, EntityWithPriority b)
    {
        return a.priority.CompareTo(b.priority);
    }
}

/// <summary>
/// Helper struct for LRU eviction sorting.
/// </summary>
struct CacheEntryWithKey
{
    public ColliderCacheKey key;
    public ColliderCacheEntry entry;
}

/// <summary>
/// Comparer for sorting cache entries by last access frame (ascending - oldest first).
/// </summary>
struct LRUComparer : IComparer<CacheEntryWithKey>
{
    public int Compare(CacheEntryWithKey a, CacheEntryWithKey b)
    {
        return a.entry.lastAccessFrame.CompareTo(b.entry.lastAccessFrame);
    }
}






