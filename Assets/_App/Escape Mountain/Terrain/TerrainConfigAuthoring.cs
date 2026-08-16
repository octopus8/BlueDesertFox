using Sirenix.OdinInspector;
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
        FindMainCamera
    }
    
    [Header("Player Tracking")]
    [Tooltip("How to find the player GameObject at runtime")]
    public PlayerSearchMode playerSearchMode = PlayerSearchMode.AutoDetect;
    
    [ShowIf("playerSearchMode", PlayerSearchMode.FindByName)]
    [Tooltip("GameObject name to search for (only used if mode is FindByName)")]
    public string playerName = "XR Origin Hands (XR Rig)";
    
    [ShowIf("playerSearchMode", PlayerSearchMode.FindByTag)]
    [ValueDropdown("@UnityEditorInternal.InternalEditorUtility.tags")]
    [Tooltip("GameObject tag to search for (only used if mode is FindByTag)")]
    public string playerTag = "Player";
    
    [Header("Tile Settings")]
    [Tooltip("Size of each terrain tile in meters")]
    public float tileSize = 100f;
    
    [Tooltip("Distance from player that tiles remain active")]
    public float viewDistance = 500f;
    
    [Tooltip("Number of vertices per side of each tile (higher = more detailed)")]
    public int verticesPerSide = 32;

    [Tooltip("Extra Y shift applied after aligning terrain to the player feet at init. Negative lowers the terrain (e.g. -5 = 5m below feet).")]
    public float initYOffset = 0f;

    [Tooltip("Constant terrain grade along world +Z in degrees. 0 = flat. Positive = uphill as Z increases.")]
    [Range(-60f, 60f)]
    public float slopeAngleDegrees = 0f;

    [Tooltip("Per-tile grade variation in degrees subtracted from slopeAngleDegrees (e.g. -35° with 10 = tiles between -45° and -35°). 0 = uniform grade.")]
    [Range(0f, 30f)]
    public float slopeAngleVariation = 0f;

    [Tooltip("Meters of blend zone centered on each tile Z-boundary where adjacent tile grades crossfade. 0 = hard step at seams.")]
    [Min(0f)]
    public float slopeVariationBlendDistance = 20f;
    
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
    
    [Header("Physics Optimization")]
    [Range(1, 20)]
    [Tooltip("Maximum number of terrain meshes generated per frame (Burst jobs)")]
    public int maxCollidersCreatedPerFrame = 6;

    [Range(1, 8)]
    [Tooltip("Maximum BVH MeshCollider.Create calls per cross-frame batch. Keep at 1-2 for Quest VR.")]
    public int maxPhysicsCollidersCreatedPerFrame = 1;
    
    [Tooltip("Distance beyond which colliders are removed completely (no physics beyond this distance)")]
    public float maxColliderDistance = 450f;

    [Range(8, 256)]
    [Tooltip("Maximum memory in megabytes for the grid-coordinate collider blob LRU cache")]
    public int maxColliderCacheMemoryMB = 53;
    
    [NaughtyAttributes.Layer]
    [Tooltip("Physics layer index for all terrain colliders")]
    public int terrainPhysicsLayer = 0;

    [Tooltip("Physics material for terrain colliders (friction and bounciness). Leave empty for Unity Physics defaults.")]
    public PhysicsMaterial terrainPhysicsMaterial;

    [Header("Trail – Shared")]
    [Tooltip("Y height of all flat trail surfaces in world units (shared by all three trails)")]
    public float trailHeight = 0f;

    [Tooltip("Spacing in meters between trail centerline LUT samples (lower = sharper blends, higher = faster)")]
    [Min(0.25f)]
    public float trailLutStepMeters = 1f;

    [Tooltip("Shared world X where all trails meet at the start")]
    public float trailStartX = 0f;

    [Tooltip("World Z where the shared straight run begins")]
    public float trailStartZ = 0f;

    [Tooltip("Meters from Start Z (both +Z and -Z) where all trails stay locked to Start X before weaving")]
    [Min(0f)]
    public float trailStraightLength = 80f;

    [Tooltip("Meters over which weave amplitude fades in after the straight run (0 = immediate full weave)")]
    [Min(0f)]
    public float trailWeaveFadeLength = 30f;

    [Tooltip("If enabled, Start X/Z are overwritten once at play start from the player (or Player Follow Object) position")]
    public bool trailSnapStartToPlayer = true;

    [SerializeField, HideInInspector]
    bool trailPathSettingsInitialized;

    [Header("Trail 1")]
    [Tooltip("Enable a flat winding trail carved into the terrain")]
    public bool trail1Enabled = false;

    [ShowIf("trail1Enabled")]
    [Tooltip("Width of the fully-flat portion of Trail 1 in meters")]
    public float trail1Width = 15f;

    [ShowIf("trail1Enabled")]
    [Tooltip("Width of the smooth blend zone on each side of Trail 1 in meters")]
    public float trail1BlendWidth = 8f;

    [ShowIf("trail1Enabled")]
    [Tooltip("Optional path image: black line on white, red dot = start. When set, seed/frequency/amplitude are ignored.")]
    public Texture2D trail1PathImage;

    [ShowIf("@trail1Enabled && trail1PathImage != null")]
    [Tooltip("World meters represented by one image pixel. A 1024px image at 2 m/px covers 2048×2048 m.")]
    [Min(0.001f)]
    public float trail1MetersPerPixel = 1f;

    [ShowIf("@trail1Enabled && trail1PathImage == null")]
    [Tooltip("Random seed — change to get a different weave pattern for Trail 1")]
    public float trail1Seed = 0f;

    [ShowIf("@trail1Enabled && trail1PathImage == null")]
    [Tooltip("How rapidly Trail 1 weaves along Z (higher = tighter turns)")]
    public float trail1Frequency = 0.003f;

    [ShowIf("@trail1Enabled && trail1PathImage == null")]
    [Tooltip("Maximum left/right deviation of Trail 1 centerline in meters")]
    public float trail1Amplitude = 40f;

    [Header("Trail 2")]
    [Tooltip("Enable a second flat winding trail carved into the terrain")]
    public bool trail2Enabled = false;

    [ShowIf("trail2Enabled")]
    [Tooltip("Width of the fully-flat portion of Trail 2 in meters")]
    public float trail2Width = 15f;

    [ShowIf("trail2Enabled")]
    [Tooltip("Width of the smooth blend zone on each side of Trail 2 in meters")]
    public float trail2BlendWidth = 8f;

    [ShowIf("trail2Enabled")]
    [Tooltip("Optional path image: black line on white, red dot = start. When set, seed/frequency/amplitude are ignored.")]
    public Texture2D trail2PathImage;

    [ShowIf("@trail2Enabled && trail2PathImage != null")]
    [Tooltip("World meters represented by one image pixel. A 1024px image at 2 m/px covers 2048×2048 m.")]
    [Min(0.001f)]
    public float trail2MetersPerPixel = 1f;

    [ShowIf("@trail2Enabled && trail2PathImage == null")]
    [Tooltip("Random seed — change to get a different weave pattern for Trail 2")]
    public float trail2Seed = 100f;

    [ShowIf("@trail2Enabled && trail2PathImage == null")]
    [Tooltip("How rapidly Trail 2 weaves along Z (higher = tighter turns)")]
    public float trail2Frequency = 0.003f;

    [ShowIf("@trail2Enabled && trail2PathImage == null")]
    [Tooltip("Maximum left/right deviation of Trail 2 centerline in meters")]
    public float trail2Amplitude = 40f;

    [Header("Trail 3")]
    [Tooltip("Enable a third flat winding trail carved into the terrain")]
    public bool trail3Enabled = false;

    [ShowIf("trail3Enabled")]
    [Tooltip("Width of the fully-flat portion of Trail 3 in meters")]
    public float trail3Width = 15f;

    [ShowIf("trail3Enabled")]
    [Tooltip("Width of the smooth blend zone on each side of Trail 3 in meters")]
    public float trail3BlendWidth = 8f;

    [ShowIf("trail3Enabled")]
    [Tooltip("Optional path image: black line on white, red dot = start. When set, seed/frequency/amplitude are ignored.")]
    public Texture2D trail3PathImage;

    [ShowIf("@trail3Enabled && trail3PathImage != null")]
    [Tooltip("World meters represented by one image pixel. A 1024px image at 2 m/px covers 2048×2048 m.")]
    [Min(0.001f)]
    public float trail3MetersPerPixel = 1f;

    [ShowIf("@trail3Enabled && trail3PathImage == null")]
    [Tooltip("Random seed — change to get a different weave pattern for Trail 3")]
    public float trail3Seed = 200f;

    [ShowIf("@trail3Enabled && trail3PathImage == null")]
    [Tooltip("How rapidly Trail 3 weaves along Z (higher = tighter turns)")]
    public float trail3Frequency = 0.003f;

    [ShowIf("@trail3Enabled && trail3PathImage == null")]
    [Tooltip("Maximum left/right deviation of Trail 3 centerline in meters")]
    public float trail3Amplitude = 40f;

    bool Trail1UsesPathImage => trail1Enabled && trail1PathImage != null;
    bool Trail1UsesNoisePath => trail1Enabled && trail1PathImage == null;
    bool Trail2UsesPathImage => trail2Enabled && trail2PathImage != null;
    bool Trail2UsesNoisePath => trail2Enabled && trail2PathImage == null;
    bool Trail3UsesPathImage => trail3Enabled && trail3PathImage != null;
    bool Trail3UsesNoisePath => trail3Enabled && trail3PathImage == null;

    [System.NonSerialized] TrailImagePathUtility.ExtractResult _trail1GizmoPath;
    [System.NonSerialized] Texture2D _trail1GizmoPathTexture;
    [System.NonSerialized] float _trail1GizmoPathMeters;
    [System.NonSerialized] TrailImagePathUtility.ExtractResult _trail2GizmoPath;
    [System.NonSerialized] Texture2D _trail2GizmoPathTexture;
    [System.NonSerialized] float _trail2GizmoPathMeters;
    [System.NonSerialized] TrailImagePathUtility.ExtractResult _trail3GizmoPath;
    [System.NonSerialized] Texture2D _trail3GizmoPathTexture;
    [System.NonSerialized] float _trail3GizmoPathMeters;

    [Header("Debug/Testing")]
    [Tooltip("Enable terrain tile rendering (disable to test tree rendering only)")]
    public bool renderTerrain = true;
    
    [Tooltip("Enable physics collider generation (disable for debugging/performance testing)")]
    public bool enablePhysicsColliders = true;
    
    /// <summary>Bakes all terrain configuration fields into the <see cref="TerrainTileConfig"/> singleton ECS component.</summary>
    public class Baker : Baker<TerrainConfigAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(TerrainConfigAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            // Create terrain tile config singleton
            AddComponent(entity, new TerrainTileConfig
            {
                tileSize = authoring.tileSize,
                viewDistance = authoring.viewDistance,
                verticesPerSide = authoring.verticesPerSide,
                slopeAngleDegrees = authoring.slopeAngleDegrees,
                slopeAngleVariation = authoring.slopeAngleVariation,
                slopeVariationBlendDistance = authoring.slopeVariationBlendDistance,
                noiseFrequency = authoring.noiseFrequency,
                noiseAmplitude = authoring.noiseAmplitude,
                noiseOctaves = authoring.noiseOctaves,
                noiseLacunarity = authoring.noiseLacunarity,
                noisePersistence = authoring.noisePersistence,
                continentalFrequency = authoring.continentalFrequency,
                continentalExponent = authoring.continentalExponent,
                heightOffset = 0f,
                initYOffset = authoring.initYOffset,
                // Physics optimization
                maxCollidersCreatedPerFrame = authoring.maxCollidersCreatedPerFrame,
                maxPhysicsCollidersCreatedPerFrame = authoring.maxPhysicsCollidersCreatedPerFrame,
                maxColliderDistance = authoring.maxColliderDistance,
                maxColliderCacheMemoryMB = authoring.maxColliderCacheMemoryMB,
                terrainPhysicsLayer = authoring.terrainPhysicsLayer,
                terrainColliderMaterial = TerrainPhysicsMaterialUtility.FromPhysicsMaterial(authoring.terrainPhysicsMaterial),
                // Debug/Testing
                renderTerrain = authoring.renderTerrain,
                enablePhysicsColliders = authoring.enablePhysicsColliders
            });

            AddComponent(entity, new TerrainHeightAlignState
            {
                aligned = 0
            });
            
            // Create scroll velocity singleton (starts inactive)
            AddComponent(entity, new TerrainScrollVelocity
            {
                direction = float3.zero,
                speed = 0f,
                verticalSpeed = 0f
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
                case PlayerSearchMode.FindMainCamera:
                    searchMode = PlayerTrackingSearch.Mode.FindMainCamera;
                    break;
                case PlayerSearchMode.AutoDetect:
                default:
                    searchMode = PlayerTrackingSearch.Mode.FindMainCamera;
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

            // Bake trail configuration singleton (instance shape + shared height)
            AddComponent(entity, new TrailConfig
            {
                height = authoring.trailHeight,
                lutStepMeters = authoring.trailLutStepMeters > 0f ? authoring.trailLutStepMeters : 1f,
                trail1 = new TrailInstanceConfig
                {
                    enabled   = authoring.trail1Enabled,
                    width     = authoring.trail1Width,
                    blendWidth= authoring.trail1BlendWidth,
                    seed      = authoring.trail1Seed,
                    frequency = authoring.trail1Frequency,
                    amplitude = authoring.trail1Amplitude
                },
                trail2 = new TrailInstanceConfig
                {
                    enabled   = authoring.trail2Enabled,
                    width     = authoring.trail2Width,
                    blendWidth= authoring.trail2BlendWidth,
                    seed      = authoring.trail2Seed,
                    frequency = authoring.trail2Frequency,
                    amplitude = authoring.trail2Amplitude
                },
                trail3 = new TrailInstanceConfig
                {
                    enabled   = authoring.trail3Enabled,
                    width     = authoring.trail3Width,
                    blendWidth= authoring.trail3BlendWidth,
                    seed      = authoring.trail3Seed,
                    frequency = authoring.trail3Frequency,
                    amplitude = authoring.trail3Amplitude
                }
            });

            // Separate path component keeps TrailConfig layout stable for existing bakes.
            float straightLength = authoring.trailStraightLength > 0f ? authoring.trailStraightLength : 80f;
            AddComponent(entity, new TrailPathConfig
            {
                startX = authoring.trailStartX,
                startZ = authoring.trailStartZ,
                straightLength = straightLength,
                weaveFadeLength = math.max(0f, authoring.trailWeaveFadeLength),
                startAligned = 0,
                snapStartToPlayer = authoring.trailSnapStartToPlayer ? (byte)1 : (byte)0
            });

            AddComponent(entity, BakeTrailImagePaths(authoring));
        }

        TrailImagePaths BakeTrailImagePaths(TerrainConfigAuthoring authoring)
        {
            var paths = new TrailImagePaths
            {
                trail1 = BakeTrailImagePath(authoring.trail1Enabled, authoring.trail1PathImage, authoring.trail1MetersPerPixel, "Trail 1"),
                trail2 = BakeTrailImagePath(authoring.trail2Enabled, authoring.trail2PathImage, authoring.trail2MetersPerPixel, "Trail 2"),
                trail3 = BakeTrailImagePath(authoring.trail3Enabled, authoring.trail3PathImage, authoring.trail3MetersPerPixel, "Trail 3")
            };
            return paths;
        }

        BlobAssetReference<TrailImagePathBlob> BakeTrailImagePath(
            bool enabled,
            Texture2D texture,
            float metersPerPixel,
            string trailLabel)
        {
            if (!enabled || texture == null)
                return default;

            DependsOn(texture);

            if (!TrailImagePathUtility.TryExtract(texture, metersPerPixel, out var extracted, out string error))
            {
                Debug.LogError($"[TerrainConfig] {trailLabel} path image: {error}", texture);
                return default;
            }

            var blob = TrailImagePathUtility.CreateBlob(extracted);
            AddBlobAsset(ref blob, out _);
            return blob;
        }
    }

    /// <summary>Draws terrain tile-grid and view-distance radius gizmos around the player position in the Scene view when this component is selected.</summary>
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

        DrawTrailPathGizmos();
    }

    /// <summary>
    /// Draws the shared start (yellow), image polylines, and noise weave preview for enabled trails.
    /// </summary>
    private void DrawTrailPathGizmos()
    {
        float straight = trailStraightLength > 0f ? trailStraightLength : 80f;
        float fade = Mathf.Max(0f, trailWeaveFadeLength);
        float y = trailHeight + 1f;

        // Straight segment through start (both +Z and -Z).
        Gizmos.color = Color.yellow;
        Vector3 straightA = new Vector3(trailStartX, y, trailStartZ - straight);
        Vector3 straightB = new Vector3(trailStartX, y, trailStartZ + straight);
        Gizmos.DrawLine(straightA, straightB);
        Gizmos.DrawWireSphere(new Vector3(trailStartX, y, trailStartZ), 2f);

        void DrawWeave(bool enabled, float seed, float frequency, float amplitude, Color color)
        {
            if (!enabled || amplitude <= 0f)
                return;

            Gizmos.color = color;
            const float step = 5f;
            float zMin = trailStartZ - straight - fade - 200f;
            float zMax = trailStartZ + straight + fade + 200f;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;
            for (float z = zMin; z <= zMax; z += step)
            {
                float along = Mathf.Abs(z - trailStartZ);
                float x = trailStartX;
                if (along > straight)
                {
                    float weaveWeight = 1f;
                    if (fade > 0f)
                        weaveWeight = Mathf.SmoothStep(0f, 1f, (along - straight) / fade);
                    float edgeZ = trailStartZ + Mathf.Sign(z - trailStartZ) * straight;
                    float nZ = Mathf.PerlinNoise(z * frequency + seed, 0f) * 2f - 1f;
                    float nEdge = Mathf.PerlinNoise(edgeZ * frequency + seed, 0f) * 2f - 1f;
                    // Preview only — runtime uses snoise; shape is representative.
                    x = trailStartX + amplitude * (nZ - nEdge) * weaveWeight;
                }

                Vector3 p = new Vector3(x, y, z);
                if (hasPrev)
                    Gizmos.DrawLine(prev, p);
                prev = p;
                hasPrev = true;
            }
        }

        if (Trail1UsesPathImage)
            DrawImagePathGizmo(trail1PathImage, trail1MetersPerPixel, new Color(1f, 0.4f, 0.2f), y,
                ref _trail1GizmoPath, ref _trail1GizmoPathTexture, ref _trail1GizmoPathMeters);
        else
            DrawWeave(trail1Enabled, trail1Seed, trail1Frequency, trail1Amplitude, new Color(1f, 0.4f, 0.2f));

        if (Trail2UsesPathImage)
            DrawImagePathGizmo(trail2PathImage, trail2MetersPerPixel, new Color(0.2f, 0.8f, 1f), y,
                ref _trail2GizmoPath, ref _trail2GizmoPathTexture, ref _trail2GizmoPathMeters);
        else
            DrawWeave(trail2Enabled, trail2Seed, trail2Frequency, trail2Amplitude, new Color(0.2f, 0.8f, 1f));

        if (Trail3UsesPathImage)
            DrawImagePathGizmo(trail3PathImage, trail3MetersPerPixel, new Color(0.4f, 1f, 0.4f), y,
                ref _trail3GizmoPath, ref _trail3GizmoPathTexture, ref _trail3GizmoPathMeters);
        else
            DrawWeave(trail3Enabled, trail3Seed, trail3Frequency, trail3Amplitude, new Color(0.4f, 1f, 0.4f));
    }

    void DrawImagePathGizmo(
        Texture2D image,
        float metersPerPixel,
        Color color,
        float y,
        ref TrailImagePathUtility.ExtractResult cache,
        ref Texture2D cacheTexture,
        ref float cacheMeters)
    {
        if (cache == null || cacheTexture != image || !Mathf.Approximately(cacheMeters, metersPerPixel))
        {
            if (!TrailImagePathUtility.TryExtract(image, metersPerPixel, out cache, out _))
                return;

            cacheTexture = image;
            cacheMeters = metersPerPixel;
        }

        if (cache?.xOffset == null || cache.xOffset.Length == 0)
            return;

        Gizmos.color = color;
        Vector3 prev = Vector3.zero;
        bool hasPrev = false;
        for (int i = 0; i < cache.xOffset.Length; i++)
        {
            float ox = cache.xOffset[i];
            if (float.IsNaN(ox))
            {
                hasPrev = false;
                continue;
            }

            Vector3 p = new Vector3(trailStartX + ox, y, trailStartZ + cache.zMin + i * cache.zStep);
            if (hasPrev)
                Gizmos.DrawLine(prev, p);
            prev = p;
            hasPrev = true;
        }
    }
    
    /// <summary>Attempts to locate the player <see cref="Transform"/> at edit time for gizmo visualization using the configured <see cref="playerSearchMode"/>.</summary>
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
            case PlayerSearchMode.FindMainCamera:
                return Camera.main?.transform;
            case PlayerSearchMode.AutoDetect:
                return Camera.main?.transform;
        }
        return null;
    }

    /// <summary>Clamps all inspector-configured values to valid ranges (e.g. minimum tile size, positive octave count) when values change in the Inspector.</summary>
    private void OnValidate()
    {
        // Existing scenes deserialize newly added fields as 0; apply intended defaults once.
        if (!trailPathSettingsInitialized)
        {
            if (trailStraightLength <= 0f)
                trailStraightLength = 80f;
            if (trailWeaveFadeLength <= 0f)
                trailWeaveFadeLength = 30f;
            trailPathSettingsInitialized = true;
        }

        // Ensure valid values
        tileSize = Mathf.Max(1f, tileSize);
        viewDistance = Mathf.Max(tileSize, viewDistance);
        verticesPerSide = Mathf.Max(2, verticesPerSide);
        slopeAngleDegrees = Mathf.Clamp(slopeAngleDegrees, -60f, 60f);
        slopeAngleVariation = Mathf.Clamp(slopeAngleVariation, 0f, 30f);
        slopeAngleVariation = Mathf.Min(slopeAngleVariation, slopeAngleDegrees + 60f);
        slopeVariationBlendDistance = Mathf.Clamp(slopeVariationBlendDistance, 0f, tileSize);
        noiseFrequency = Mathf.Max(0.0001f, noiseFrequency);
        noiseAmplitude = Mathf.Max(0f, noiseAmplitude);
        noiseLacunarity = Mathf.Max(1f, noiseLacunarity);
        continentalFrequency = Mathf.Max(0f, continentalFrequency);
        continentalExponent = Mathf.Max(0.1f, continentalExponent);
        trailLutStepMeters = Mathf.Max(0.25f, trailLutStepMeters);
        trailStraightLength = Mathf.Max(0f, trailStraightLength);
        trailWeaveFadeLength = Mathf.Max(0f, trailWeaveFadeLength);
        trail1Width     = Mathf.Max(0f, trail1Width);
        trail1BlendWidth= Mathf.Max(0f, trail1BlendWidth);
        trail1Frequency = Mathf.Max(0f, trail1Frequency);
        trail1Amplitude = Mathf.Max(0f, trail1Amplitude);
        trail1MetersPerPixel = trail1MetersPerPixel > 0f ? trail1MetersPerPixel : 1f;
        trail2Width     = Mathf.Max(0f, trail2Width);
        trail2BlendWidth= Mathf.Max(0f, trail2BlendWidth);
        trail2Frequency = Mathf.Max(0f, trail2Frequency);
        trail2Amplitude = Mathf.Max(0f, trail2Amplitude);
        trail2MetersPerPixel = trail2MetersPerPixel > 0f ? trail2MetersPerPixel : 1f;
        trail3Width     = Mathf.Max(0f, trail3Width);
        trail3BlendWidth= Mathf.Max(0f, trail3BlendWidth);
        trail3Frequency = Mathf.Max(0f, trail3Frequency);
        trail3Amplitude = Mathf.Max(0f, trail3Amplitude);
        trail3MetersPerPixel = trail3MetersPerPixel > 0f ? trail3MetersPerPixel : 1f;

        WarnIfPathImageNotReadable(trail1Enabled, trail1PathImage, "Trail 1");
        WarnIfPathImageNotReadable(trail2Enabled, trail2PathImage, "Trail 2");
        WarnIfPathImageNotReadable(trail3Enabled, trail3PathImage, "Trail 3");
        
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

    static void WarnIfPathImageNotReadable(bool enabled, Texture2D texture, string trailLabel)
    {
        if (!enabled || texture == null || texture.isReadable)
            return;

        Debug.LogWarning(
            $"[TerrainConfig] {trailLabel} path image '{texture.name}' is not readable. Enable Read/Write in the texture import settings.",
            texture);
    }
}
