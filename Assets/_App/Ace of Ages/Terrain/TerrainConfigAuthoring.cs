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

    [Header("Continental Settings")]
    [Tooltip("Frequency of the large-scale continental mask noise (lower = larger plains/mountain regions). Set to 0 to disable.")]
    public float continentalFrequency = 0.0008f;

    [Tooltip("Power curve applied to the continental mask. Values >1 bias more area toward flat plains; lower values allow more mountains.")]
    [Range(0.1f, 8f)]
    public float continentalExponent = 2.5f;
    
    [Header("Material")]
    [Tooltip("Material to use for terrain rendering (should use URP Lit shader)")]
    public Material terrainMaterial;
    
    [Header("Debug/Testing")]
    [Tooltip("Enable terrain tile rendering (disable to test tree rendering only)")]
    public bool renderTerrain = true;
    
    [Tooltip("Enable physics collider generation (disable for debugging/performance testing)")]
    public bool enablePhysicsColliders = true;
    
    [Tooltip("Enable TerrainRenderingDebugSystem logging (disable to reduce console spam)")]
    public bool enableRenderingDebug;
    
    [Header("Physics Optimization")]
    [Range(1, 20)]
    [Tooltip("Maximum number of terrain meshes generated per frame (Burst jobs)")]
    public int maxCollidersCreatedPerFrame = 6;

    [Range(1, 8)]
    [Tooltip("Maximum number of physics colliders created per frame (main-thread MeshCollider.Create). Keep low for VR (3-4).")]
    public int maxPhysicsCollidersCreatedPerFrame = 4;
    
    [Tooltip("Distance beyond which colliders are removed completely (no physics beyond this distance)")]
    public float maxColliderDistance = 450f;
    
    [Range(10, 200)]
    [Tooltip("Maximum memory in megabytes for collider cache - oldest entries evicted when exceeded")]
    public int maxColliderCacheMemoryMB = 50;

    [Tooltip("Tiles closer than this distance use full-resolution physics meshes. Beyond this, vertex stride is applied.")]
    public float physicsColliderFullResolutionDistance = 128f;

    [Range(1, 4)]
    [Tooltip("Sample every Nth vertex for physics beyond full-resolution distance. 2 = ~4x fewer triangles.")]
    public int physicsColliderVertexStride = 2;
    
    [NaughtyAttributes.Layer]
    [Tooltip("Physics layer index for all terrain colliders")]
    public int terrainPhysicsLayer = 0;

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
                continentalFrequency = authoring.continentalFrequency,
                continentalExponent = authoring.continentalExponent,
                // Physics optimization
                maxCollidersCreatedPerFrame = authoring.maxCollidersCreatedPerFrame,
                maxPhysicsCollidersCreatedPerFrame = authoring.maxPhysicsCollidersCreatedPerFrame,
                maxColliderDistance = authoring.maxColliderDistance,
                maxColliderCacheMemoryMB = authoring.maxColliderCacheMemoryMB,
                physicsColliderFullResolutionDistance = authoring.physicsColliderFullResolutionDistance,
                physicsColliderVertexStride = math.max(1, authoring.physicsColliderVertexStride),
                terrainPhysicsLayer = authoring.terrainPhysicsLayer,
                // Debug/Testing
                renderTerrain = authoring.renderTerrain,
                enablePhysicsColliders = authoring.enablePhysicsColliders,
                enableRenderingDebug = authoring.enableRenderingDebug
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

            AddComponent(entity, new PlayerTargetVelocity());
            
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
        continentalFrequency = Mathf.Max(0f, continentalFrequency);
        continentalExponent = Mathf.Max(0.1f, continentalExponent);
        
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
