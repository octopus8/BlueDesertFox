using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace _App.Ace_of_Ages.Terrain
{
    /// <summary>
    /// LRU cache of terrain collider blobs keyed by absolute grid coordinate.
    /// Terrain height is deterministic from grid coord + baked noise config, so blobs
    /// can be reused when tiles leave and re-enter maxColliderDistance during scroll.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct TerrainColliderBlobCacheSystem : ISystem
    {
        struct CacheEntry
        {
            public BlobAssetReference<Collider> collider;
            public int estimatedMemoryBytes;
            public uint lastAccessFrame;
        }

        NativeHashMap<int2, CacheEntry> _cache;
        int _totalMemoryBytes;
        int _maxCacheBytes;
        uint _frameCounter;

        public void OnCreate(ref SystemState state)
        {
            _cache = new NativeHashMap<int2, CacheEntry>(64, Allocator.Persistent);
            _totalMemoryBytes = 0;
            _maxCacheBytes = 53 * 1024 * 1024;
            _frameCounter = 0;

            if (!SystemAPI.HasSingleton<TerrainColliderCacheStats>())
                state.EntityManager.CreateEntity(typeof(TerrainColliderCacheStats));
        }

        public void OnDestroy(ref SystemState state)
        {
            if (!_cache.IsCreated)
                return;

            foreach (var kv in _cache)
            {
                if (kv.Value.collider.IsCreated)
                    kv.Value.collider.Dispose();
            }

            _cache.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            _frameCounter++;

            if (SystemAPI.HasSingleton<TerrainTileConfig>())
            {
                int mb = SystemAPI.GetSingleton<TerrainTileConfig>().maxColliderCacheMemoryMB;
                _maxCacheBytes = math.max(1, mb) * 1024 * 1024;
            }

            if (SystemAPI.HasSingleton<TerrainColliderCacheStats>())
            {
                SystemAPI.SetSingleton(new TerrainColliderCacheStats
                {
                    entryCount = _cache.Count,
                    totalMemoryBytes = _totalMemoryBytes
                });
            }
        }

        /// <summary>
        /// Removes and returns a cached collider blob for <paramref name="gridCoord"/> if present.
        /// Transfers ownership to the caller (cache no longer holds the blob).
        /// </summary>
        public static bool TryTakeCachedCollider(
            WorldUnmanaged world,
            int2 gridCoord,
            out BlobAssetReference<Collider> collider)
        {
            collider = default;

            var handle = world.GetExistingUnmanagedSystem<TerrainColliderBlobCacheSystem>();
            if (handle == SystemHandle.Null)
                return false;

            ref var cache = ref world.GetUnsafeSystemRef<TerrainColliderBlobCacheSystem>(handle);
            if (!cache._cache.TryGetValue(gridCoord, out var entry) || !entry.collider.IsCreated)
                return false;

            collider = entry.collider;
            cache._totalMemoryBytes -= entry.estimatedMemoryBytes;
            cache._cache.Remove(gridCoord);
            return true;
        }

        /// <summary>
        /// Attempts to retrieve a cached collider blob for <paramref name="gridCoord"/> without removing it.
        /// Updates LRU access frame on hit. Prefer <see cref="TryTakeCachedCollider"/> when assigning to entities.
        /// </summary>
        public static bool TryGetCachedCollider(
            WorldUnmanaged world,
            int2 gridCoord,
            out BlobAssetReference<Collider> collider)
        {
            collider = default;

            var handle = world.GetExistingUnmanagedSystem<TerrainColliderBlobCacheSystem>();
            if (handle == SystemHandle.Null)
                return false;

            ref var cache = ref world.GetUnsafeSystemRef<TerrainColliderBlobCacheSystem>(handle);
            if (!cache._cache.TryGetValue(gridCoord, out var entry) || !entry.collider.IsCreated)
                return false;

            entry.lastAccessFrame = cache._frameCounter;
            cache._cache[gridCoord] = entry;
            collider = entry.collider;
            return true;
        }

        /// <summary>
        /// Stores a collider blob in the cache, evicting LRU entries if over the memory budget.
        /// </summary>
        public static void RetireToCache(
            WorldUnmanaged world,
            int2 gridCoord,
            BlobAssetReference<Collider> collider,
            int estimatedMemoryBytes)
        {
            if (!collider.IsCreated)
                return;

            var handle = world.GetExistingUnmanagedSystem<TerrainColliderBlobCacheSystem>();
            if (handle == SystemHandle.Null)
            {
                collider.Dispose();
                return;
            }

            ref var cache = ref world.GetUnsafeSystemRef<TerrainColliderBlobCacheSystem>(handle);

            if (cache._cache.TryGetValue(gridCoord, out var existing))
            {
                if (existing.collider.IsCreated)
                    existing.collider.Dispose();
                cache._totalMemoryBytes -= existing.estimatedMemoryBytes;
                cache._cache.Remove(gridCoord);
            }

            cache._totalMemoryBytes += estimatedMemoryBytes;

            cache._cache.TryAdd(gridCoord, new CacheEntry
            {
                collider = collider,
                estimatedMemoryBytes = estimatedMemoryBytes,
                lastAccessFrame = cache._frameCounter
            });

            cache.EvictIfOverBudget(cache._maxCacheBytes);
        }

        /// <summary>
        /// Rough memory estimate for a full-resolution terrain collider blob.
        /// </summary>
        public static int EstimateColliderMemoryBytes(int vertexCount, int triangleCount)
        {
            return math.max(4096, vertexCount * 16 + triangleCount * 48 + 8192);
        }

        void EvictIfOverBudget(int maxBytes)
        {
            while (_totalMemoryBytes > maxBytes && _cache.Count > 0)
            {
                int2 oldestKey = default;
                uint oldestFrame = uint.MaxValue;
                CacheEntry oldestEntry = default;
                bool found = false;

                foreach (var kv in _cache)
                {
                    if (kv.Value.lastAccessFrame < oldestFrame)
                    {
                        oldestFrame = kv.Value.lastAccessFrame;
                        oldestKey = kv.Key;
                        oldestEntry = kv.Value;
                        found = true;
                    }
                }

                if (!found)
                    break;

                if (oldestEntry.collider.IsCreated)
                    oldestEntry.collider.Dispose();

                _cache.Remove(oldestKey);
                _totalMemoryBytes -= oldestEntry.estimatedMemoryBytes;
            }
        }
    }
}
