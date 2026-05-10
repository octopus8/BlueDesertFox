using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

#if UNITY_EDITOR
using Unity.Profiling;
#endif

/// <summary>
/// System that calculates distance from each terrain tile to the player and determines appropriate LOD level.
/// Runs before TerrainPhysicsSystem to ensure distance data is up-to-date.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TerrainPhysicsSystem))]
public partial class TerrainDistanceTrackingSystem : SystemBase
{
#if UNITY_EDITOR
    private static readonly ProfilerMarker s_ProfilerMarker = new ProfilerMarker("TerrainPhysics.DistanceTracking");
#endif

    protected override void OnCreate()
    {
        RequireForUpdate<TerrainTileConfig>();
    }

    protected override void OnUpdate()
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            
            // Early exit if physics colliders are disabled
            if (!config.enablePhysicsColliders)
            {
                return;
            }
            
            // Get player transform reference (managed component)
            if (!SystemAPI.ManagedAPI.TryGetSingleton<PlayerTransformReference>(out var playerRef) ||
                playerRef == null || 
                playerRef.playerTransform == null)
            {
                return;
            }
            
            float3 playerPosition = playerRef.playerTransform.position;
            
            // Use EntityCommandBuffer for structural changes
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            
            // Query all terrain tiles using SystemAPI.Query
            foreach (var (transform, tile, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<TerrainTile>>().WithEntityAccess())
            {
                // Calculate tile center in world space using its transform
                float3 tileCenter = transform.ValueRO.Position;
                
                // Calculate 2D distance (XZ plane) from player to tile center
                float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
                float2 tileCenter2D = new float2(tileCenter.x, tileCenter.z);
                float distance = math.distance(tileCenter2D, playerPos2D);
                
                // Determine if we need a collider based on distance
                bool needsCollider = distance < config.maxColliderDistance;
                
                // Check if we have existing distance data
                bool hasDistanceData = SystemAPI.HasComponent<TerrainTileDistanceToPlayer>(entity);
                bool hadCollider = hasDistanceData; // Assume if we tracked distance, we had collider logic
                
                // Update or add distance component
                var distanceData = new TerrainTileDistanceToPlayer
                {
                    distance = distance
                };
                
                if (hasDistanceData)
                {
                    ecb.SetComponent(entity, distanceData);
                }
                else
                {
                    ecb.AddComponent(entity, distanceData);
                }
                
                // If collider state changed and tile has mesh, mark for preparation or removal
                if (tile.ValueRO.meshGenerated)
                {
                    if (needsCollider && !SystemAPI.HasComponent<PhysicsColliderValid>(entity))
                    {
                        // Need to create collider
                        // Add or enable the needs preparation component
                        if (SystemAPI.HasComponent<PhysicsColliderNeedsPreparation>(entity))
                        {
                            ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
                        }
                        else
                        {
                            ecb.AddComponent<PhysicsColliderNeedsPreparation>(entity);
                            ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
                        }
                    }
                    else if (!needsCollider)
                    {
                        // Too far away - remove collider components if present
                        if (SystemAPI.HasComponent<Unity.Physics.PhysicsCollider>(entity))
                        {
                            ecb.RemoveComponent<Unity.Physics.PhysicsCollider>(entity);
                        }
                        if (SystemAPI.HasComponent<PhysicsColliderValid>(entity))
                        {
                            ecb.RemoveComponent<PhysicsColliderValid>(entity);
                        }
                        if (EntityManager.HasComponent<Unity.Physics.PhysicsWorldIndex>(entity))
                        {
                            ecb.RemoveComponent<Unity.Physics.PhysicsWorldIndex>(entity);
                        }
                    }
                }
            }
            
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}


