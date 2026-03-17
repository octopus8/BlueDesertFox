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

    protected override void OnUpdate()
    {
#if UNITY_EDITOR
        using (s_ProfilerMarker.Auto())
#endif
        {
            var config = SystemAPI.GetSingleton<TerrainTileConfig>();
            
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
            foreach (var (tile, entity) in SystemAPI.Query<RefRO<TerrainTile>>().WithEntityAccess())
            {
                // Calculate tile center in world space
                float2 tileCenter = new float2(
                    tile.ValueRO.gridCoordinate.x * config.tileSize + config.tileSize * 0.5f,
                    tile.ValueRO.gridCoordinate.y * config.tileSize + config.tileSize * 0.5f
                );
                
                // Calculate 2D distance (XZ plane) from player to tile center
                float2 playerPos2D = new float2(playerPosition.x, playerPosition.z);
                float distance = math.distance(tileCenter, playerPos2D);
                
                // Determine LOD level based on distance thresholds
                TerrainPhysicsLODLevel newLodLevel;
                if (distance < config.lodFullResolutionDistance)
                {
                    newLodLevel = TerrainPhysicsLODLevel.FullResolution;
                }
                else if (distance < config.lodHalfResolutionDistance)
                {
                    newLodLevel = TerrainPhysicsLODLevel.HalfResolution;
                }
                else if (distance < config.lodQuarterResolutionDistance)
                {
                    newLodLevel = TerrainPhysicsLODLevel.QuarterResolution;
                }
                else
                {
                    newLodLevel = TerrainPhysicsLODLevel.NoCollider;
                }
                
                // Check if we have existing distance data
                bool hasDistanceData = SystemAPI.HasComponent<TerrainTileDistanceToPlayer>(entity);
                TerrainPhysicsLODLevel oldLodLevel = TerrainPhysicsLODLevel.NoCollider;
                
                if (hasDistanceData)
                {
                    var oldDistanceData = SystemAPI.GetComponent<TerrainTileDistanceToPlayer>(entity);
                    oldLodLevel = oldDistanceData.lodLevel;
                }
                
                // Update or add distance component
                ecb.SetComponent(entity, new TerrainTileDistanceToPlayer
                {
                    distance = distance,
                    lodLevel = newLodLevel
                });
                
                // If LOD level changed and tile has mesh, mark for preparation
                if (oldLodLevel != newLodLevel && tile.ValueRO.meshGenerated)
                {
                    // Only create colliders for tiles that need them
                    if (newLodLevel != TerrainPhysicsLODLevel.NoCollider)
                    {
                        ecb.SetComponent(entity, new PhysicsColliderNeedsPreparation
                        {
                            targetLOD = newLodLevel
                        });
                        ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
                        
                        // Remove valid tag since we're changing LOD
                        if (SystemAPI.HasComponent<PhysicsColliderValid>(entity))
                        {
                            ecb.RemoveComponent<PhysicsColliderValid>(entity);
                        }
                    }
                    else
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


