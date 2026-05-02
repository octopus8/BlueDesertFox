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
                var distanceData = new TerrainTileDistanceToPlayer
                {
                    distance = distance,
                    lodLevel = newLodLevel
                };
                
                if (hasDistanceData)
                {
                    ecb.SetComponent(entity, distanceData);
                }
                else
                {
                    ecb.AddComponent(entity, distanceData);
                }
                
                // If LOD level changed and tile has mesh, mark for preparation
                if (oldLodLevel != newLodLevel && tile.ValueRO.meshGenerated)
                {
                    // Only create colliders for tiles that need them
                    if (newLodLevel != TerrainPhysicsLODLevel.NoCollider)
                    {
                        var needsPrep = new PhysicsColliderNeedsPreparation
                        {
                            targetLOD = newLodLevel
                        };
                        
                        // Add or set the needs preparation component
                        if (SystemAPI.HasComponent<PhysicsColliderNeedsPreparation>(entity))
                        {
                            ecb.SetComponent(entity, needsPrep);
                        }
                        else
                        {
                            ecb.AddComponent(entity, needsPrep);
                        }
                        ecb.SetComponentEnabled<PhysicsColliderNeedsPreparation>(entity, true);
                        
                        // Remove valid tag since we're changing LOD (only if it exists)
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


