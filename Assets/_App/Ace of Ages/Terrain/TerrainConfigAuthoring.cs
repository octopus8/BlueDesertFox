using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for terrain system configuration.
/// Place this on a GameObject in your scene to configure the infinite terrain system.
/// </summary>
public class TerrainConfigAuthoring : MonoBehaviour
{
    public enum PlayerSearchMode
    {
        AutoDetect,
        FindByName,
        FindByTag,
        FindAutoHandPlayer,
        FindMainCamera
    }
    
    [Header("Player Tracking")]
    [Tooltip("How to find the player GameObject at runtime")]
    public PlayerSearchMode playerSearchMode = PlayerSearchMode.AutoDetect;
    
    [Tooltip("GameObject name to search for (only used if mode is FindByName)")]
    public string playerName = "XR Origin Hands (XR Rig)";
    
    [Tooltip("GameObject tag to search for (only used if mode is FindByTag)")]
    public string playerTag = "Player";
    
    [Header("Tile Settings")]
    [Tooltip("Size of each terrain tile in meters")]
    public float tileSize = 100f;
    
    [Tooltip("Distance from player that tiles remain active")]
    public float viewDistance = 500f;
    
    [Tooltip("Number of vertices per side of each tile (higher = more detailed)")]
    public int verticesPerSide = 32;
    
    [Header("Auto-Scrolling")]
    [Tooltip("Enable automatic terrain scrolling along Z axis (endless runner mode)")]
    public bool scrollEnabled = false;
    
    [Tooltip("Speed of terrain scrolling in units per second (5.0 = 5 m/s forward)")]
    public float scrollSpeed = 5.0f;
    
    [Header("Procedural Noise Settings")]
    [Tooltip("Base frequency of the noise (higher = more variation)")]
    public float noiseFrequency = 0.01f;
    
    [Tooltip("Maximum height of terrain features")]
    public float noiseAmplitude = 20f;
    
    [Tooltip("Number of noise layers to combine")]
    [Range(1, 8)]
    public int noiseOctaves = 4;
    
    [Tooltip("Frequency multiplier for each octave")]
    public float noiseLacunarity = 2.0f;
    
    [Tooltip("Amplitude multiplier for each octave")]
    [Range(0f, 1f)]
    public float noisePersistence = 0.5f;
    
    [Header("Material")]
    [Tooltip("Material to use for terrain rendering (should use URP Lit shader)")]
    public Material terrainMaterial;
    
    [Header("Physics Optimization")]
    [Range(1, 10)]
    [Tooltip("Maximum number of physics colliders created per frame to prevent stalls")]
    public int maxCollidersCreatedPerFrame = 3;
    
    [Tooltip("Distance threshold for full-resolution colliders (uses all vertices)")]
    public float lodFullResolutionDistance = 150f;
    
    [Tooltip("Distance threshold for half-resolution colliders (uses every 2nd vertex)")]
    public float lodHalfResolutionDistance = 300f;
    
    [Tooltip("Distance threshold for quarter-resolution colliders (uses every 4th vertex)")]
    public float lodQuarterResolutionDistance = 450f;
    
    [Range(10, 200)]
    [Tooltip("Maximum memory in megabytes for collider cache - oldest entries evicted when exceeded")]
    public int maxColliderCacheMemoryMB = 50;
    
    [Tooltip("Assign distant tiles (half/quarter resolution) to separate physics layer")]
    public bool usePhysicsLODLayers = true;
    
    [Range(0, 31)]
    [Tooltip("Physics layer index for low-detail terrain (half/quarter resolution tiles)")]
    public int lowDetailPhysicsLayer = 0;

    public class Baker : Baker<TerrainConfigAuthoring>
    {
        public override void Bake(TerrainConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            // Create terrain tile config singleton
            AddComponent(entity, new TerrainTileConfig
            {
                tileSize = authoring.tileSize,
                viewDistance = authoring.viewDistance,
                verticesPerSide = authoring.verticesPerSide,
                noiseFrequency = authoring.noiseFrequency,
                noiseAmplitude = authoring.noiseAmplitude,
                noiseOctaves = authoring.noiseOctaves,
                noiseLacunarity = authoring.noiseLacunarity,
                noisePersistence = authoring.noisePersistence,
                // Physics optimization
                maxCollidersCreatedPerFrame = authoring.maxCollidersCreatedPerFrame,
                lodFullResolutionDistance = authoring.lodFullResolutionDistance,
                lodHalfResolutionDistance = authoring.lodHalfResolutionDistance,
                lodQuarterResolutionDistance = authoring.lodQuarterResolutionDistance,
                maxColliderCacheMemoryMB = authoring.maxColliderCacheMemoryMB,
                usePhysicsLODLayers = authoring.usePhysicsLODLayers,
                lowDetailPhysicsLayer = authoring.lowDetailPhysicsLayer
            });
            
            // Create scroll velocity singleton (starts inactive)
            AddComponent(entity, new TerrainScrollVelocity
            {
                direction = float3.zero,
                speed = 0f
            });
            
            // Create scroll offset singleton (starts at zero)
            AddComponent(entity, new ScrollOffset
            {
                accumulatedOffset = float3.zero
            });
            
            // Determine search mode and parameters
            PlayerTrackingSearch.Mode searchMode;
            string searchString = "";
            
            switch (authoring.playerSearchMode)
            {
                case PlayerSearchMode.FindByName:
                    searchMode = PlayerTrackingSearch.Mode.FindByName;
                    searchString = authoring.playerName;
                    break;
                case PlayerSearchMode.FindByTag:
                    searchMode = PlayerTrackingSearch.Mode.FindByTag;
                    searchString = authoring.playerTag;
                    break;
                case PlayerSearchMode.FindAutoHandPlayer:
                    searchMode = PlayerTrackingSearch.Mode.FindAutoHandPlayer;
                    break;
                case PlayerSearchMode.FindMainCamera:
                    searchMode = PlayerTrackingSearch.Mode.FindMainCamera;
                    break;
                case PlayerSearchMode.AutoDetect:
                default:
                    // Auto-detect: try AutoHandPlayer first, then Main Camera
                    searchMode = PlayerTrackingSearch.Mode.FindAutoHandPlayer;
                    break;
            }
            
            // Add search component - will be used by PlayerTrackingInitSystem at runtime
            AddComponent(entity, new PlayerTrackingSearch
            {
                mode = searchMode,
                searchString = searchString,
                initialized = false
            });
            
            // Add empty PlayerTransformReference - will be populated at runtime
            AddComponentObject(entity, new PlayerTransformReference
            {
                playerTransform = null
            });
            
            // Add terrain material reference if assigned
            AddComponentObject(entity, new TerrainMaterialReference
            {
                material = authoring.terrainMaterial
            });
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Try to find player at edit time for visualization
        Transform playerTransform = null;
        
        if (Application.isPlaying)
        {
            // In play mode, get from ECS
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                var em = world.EntityManager;
                var query = em.CreateEntityQuery(typeof(PlayerTransformReference));
                if (query.CalculateEntityCount() > 0)
                {
                    var entity = query.GetSingletonEntity();
                    var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
                    playerTransform = playerRef?.playerTransform;
                }
                query.Dispose();
            }
        }
        else
        {
            // In edit mode, try to find based on search mode
            playerTransform = FindPlayerForVisualization();
        }
        
        // Draw player position if found
        if (playerTransform != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(playerTransform.position, 5f);
            Gizmos.DrawLine(playerTransform.position, playerTransform.position + Vector3.up * 10f);
        }
        
        // Visualize view distance
        Vector3 center = playerTransform != null ? playerTransform.position : transform.position;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(center, viewDistance);
        
        // Draw a sample tile
        Gizmos.color = Color.cyan;
        Vector3 tileCorner = center;
        tileCorner.x = Mathf.Floor(tileCorner.x / tileSize) * tileSize;
        tileCorner.z = Mathf.Floor(tileCorner.z / tileSize) * tileSize;
        
        Vector3 size = new Vector3(tileSize, 0, tileSize);
        Gizmos.DrawWireCube(tileCorner + size * 0.5f, size);
    }
    
    private Transform FindPlayerForVisualization()
    {
        switch (playerSearchMode)
        {
            case PlayerSearchMode.FindByName:
                if (!string.IsNullOrEmpty(playerName))
                {
                    var go = GameObject.Find(playerName);
                    return go?.transform;
                }
                break;
            case PlayerSearchMode.FindByTag:
                if (!string.IsNullOrEmpty(playerTag))
                {
                    var go = GameObject.FindGameObjectWithTag(playerTag);
                    return go?.transform;
                }
                break;
            case PlayerSearchMode.FindAutoHandPlayer:
                var autoHandPlayer = FindFirstObjectByType<Autohand.AutoHandPlayer>();
                return autoHandPlayer?.transform;
            case PlayerSearchMode.FindMainCamera:
                return Camera.main?.transform;
            case PlayerSearchMode.AutoDetect:
                // Try AutoHandPlayer first
                var player = FindFirstObjectByType<Autohand.AutoHandPlayer>();
                if (player != null) return player.transform;
                // Fall back to main camera
                return Camera.main?.transform;
        }
        return null;
    }

    private void OnValidate()
    {
        // Ensure valid values
        tileSize = Mathf.Max(1f, tileSize);
        viewDistance = Mathf.Max(tileSize, viewDistance);
        verticesPerSide = Mathf.Max(2, verticesPerSide);
        noiseFrequency = Mathf.Max(0.0001f, noiseFrequency);
        noiseAmplitude = Mathf.Max(0f, noiseAmplitude);
        noiseLacunarity = Mathf.Max(1f, noiseLacunarity);
        
        // Set default search string if empty
        if (playerSearchMode == PlayerSearchMode.FindByName && string.IsNullOrEmpty(playerName))
        {
            playerName = "XR Origin Hands (XR Rig)";
        }
        if (playerSearchMode == PlayerSearchMode.FindByTag && string.IsNullOrEmpty(playerTag))
        {
            playerTag = "Player";
        }
    }
}
