using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for static object spawning configuration.
/// Place this on the same GameObject as TerrainConfigAuthoring to enable static object spawning on terrain tiles.
/// </summary>
public class StaticObjectSpawnerConfigAuthoring : MonoBehaviour
{
    /// <summary>
    /// Container for a static object type with 3 LOD levels and spawn weight distribution.
    /// </summary>
    [System.Serializable]
    public class StaticObjectLODSet
    {
        [Tooltip("Name for this object type (for debugging)")]
        public string objectTypeName = "Object";
        
        [Tooltip("Highest detail mesh (0-50m from player)")]
        public GameObject lod0;
        
        [Tooltip("Medium detail mesh (50-150m from player)")]
        public GameObject lod1;
        
        [Tooltip("Lowest detail mesh (150m+ from player)")]
        public GameObject lod2;
        
        [Header("LOD Spawn Distribution")]
        [Tooltip("Spawn probability weight for LOD0 (highest detail). During spawning, this percentage of objects will use LOD0. Auto-normalized with other weights.")]
        [Range(0f, 1f)]
        public float lod0SpawnWeight = 0.6f;
        
        [Tooltip("Spawn probability weight for LOD1 (medium detail). During spawning, this percentage of objects will use LOD1. Auto-normalized with other weights.")]
        [Range(0f, 1f)]
        public float lod1SpawnWeight = 0.3f;
        
        [Tooltip("Spawn probability weight for LOD2 (lowest detail). During spawning, this percentage of objects will use LOD2. Auto-normalized with other weights.")]
        [Range(0f, 1f)]
        public float lod2SpawnWeight = 0.1f;
        
        [Header("Object Type Spawn Weight")]
        [Tooltip("Relative spawn probability for this object type vs other types. Auto-normalized at bake time.")]
        [Min(0f)]
        public float objectTypeSpawnWeight = 1f;
    }
    
    [Header("Static Object LOD Sets")]
    [Tooltip("Array of object types with LOD variants to randomly spawn on terrain tiles")]
    public StaticObjectLODSet[] objectLODSets;
    
    [Header("LOD Distance Thresholds")]
    [Tooltip("Distance threshold for LOD0->LOD1 transition (meters)")]
    public float lod0Distance = 50f;
    
    [Tooltip("Distance threshold for LOD1->LOD2 transition (meters)")]
    public float lod1Distance = 150f;
    
    [Tooltip("Distance beyond which objects use LOD2 (meters)")]
    public float lod2Distance = 300f;
    
    [Tooltip("Hysteresis buffer to prevent LOD flickering (meters). Adds/subtracts from thresholds based on transition direction.")]
    [Range(0f, 20f)]
    public float lodHysteresis = 5f;
    
    [Header("Spawn Density")]
    [Tooltip("Minimum number of objects per tile")]
    [Range(0, 300)]
    public int minObjectsPerTile = 5;
    
    [Tooltip("Maximum number of objects per tile")]
    [Range(0, 300)]
    public int maxObjectsPerTile = 15;
    
    [Header("Spawn Filtering")]
    [Tooltip("Minimum terrain height for object spawning (world Y coordinate)")]
    public float minSpawnHeight = -100f;
    
    [Tooltip("Maximum terrain height for object spawning (world Y coordinate)")]
    public float maxSpawnHeight = 100f;
    
    [Tooltip("Maximum slope angle in degrees (0 = flat, 90 = vertical cliff). Objects won't spawn on steeper slopes.")]
    [Range(0f, 90f)]
    public float maxSlopeDegrees = 45f;
    
    [Header("Performance")]
    [Tooltip("Maximum number of object entities to spawn per frame (prevents stuttering)")]
    [Range(1, 100)]
    public int maxObjectsSpawnedPerFrame = 20;
    
    [Header("Debug")]
    [Tooltip("Enable static object LOD and spawning debug logging (disable to reduce console spam)")]
    public bool enableObjectLODDebug;
    
    [Tooltip("Enable static object spawner system debug logging (disable to reduce console spam)")]
    public bool enableSpawnerDebug;
    
    [Header("Distance Culling (VR Performance)")]
    [Tooltip("Enable distance-based culling for object rendering. Objects beyond maxObjectRenderDistance won't render. Recommended ON for VR.")]
    public bool enableDistanceCulling = true;
    
    [Tooltip("Maximum distance to render objects in meters. Objects beyond this distance are culled (not rendered). Quest 3 recommended: 300-500m.")]
    [Range(100f, 1000f)]
    public float maxObjectRenderDistance = 400f;
    
    [Header("Quest 3 VR Optimizations")]
    [Tooltip("Maximum number of unique mesh/material batch combinations. Increase if seeing capacity warnings in logs. Default: 32")]
    [Range(16, 128)]
    public int maxUniqueBatches = 32;
    
    [Tooltip("Frame skip interval when player velocity exceeds threshold during terrain scrolling. Quest 3 @ 72Hz recommended: 3-4. Higher = more performance, less responsive LOD.")]
    [Range(1, 8)]
    public int vrFrameSkipScrolling = 4;
    
    [Tooltip("Player velocity threshold (m/s) above which vrFrameSkipScrolling is used instead of base VR frame skip. Default: 0.5 m/s (walking speed).")]
    [Range(0.1f, 10f)]
    public float playerVelocityThreshold = 0.5f;

    /// <summary>Bakes spawner settings and LOD prefab references into <see cref="StaticObjectSpawnerConfig"/> and <see cref="StaticObjectPrefabElement"/> buffer components.</summary>
    public class Baker : Baker<StaticObjectSpawnerConfigAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(StaticObjectSpawnerConfigAuthoring authoring)
        {
            // Validate that we have object LOD sets
            if (authoring.objectLODSets == null || authoring.objectLODSets.Length == 0)
            {
                Debug.LogWarning("[StaticObjectSpawner] No object LOD sets assigned to StaticObjectSpawnerConfigAuthoring!", authoring);
                return;
            }
            
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            // Pre-calculate slope threshold (cosine of max slope angle)
            // This avoids expensive acos() calls at runtime
            float slopeThreshold = math.cos(math.radians(authoring.maxSlopeDegrees));
            
            // Create static object spawner config singleton
            AddComponent(entity, new StaticObjectSpawnerConfig
            {
                minObjectsPerTile = authoring.minObjectsPerTile,
                maxObjectsPerTile = authoring.maxObjectsPerTile,
                minSpawnHeight = authoring.minSpawnHeight,
                maxSpawnHeight = authoring.maxSpawnHeight,
                slopeThreshold = slopeThreshold,
                maxObjectsSpawnedPerFrame = authoring.maxObjectsSpawnedPerFrame,
                enableSpawnerDebug = authoring.enableSpawnerDebug
            });
            
            // Create static object LOD config singleton
            AddComponent(entity, new StaticObjectLODConfig
            {
                lod0Distance = authoring.lod0Distance,
                lod1Distance = authoring.lod1Distance,
                lod2Distance = authoring.lod2Distance,
                hysteresisDelta = authoring.lodHysteresis,
                lodsPerObjectType = 3, // Hardcoded to 3 LOD levels
                maxChunksUpdatedPerFrame = 7,
                enableObjectLODDebug = authoring.enableObjectLODDebug,
                enableDistanceCulling = authoring.enableDistanceCulling,
                maxObjectRenderDistance = authoring.maxObjectRenderDistance,
                // Quest 3 VR Optimizations
                maxUniqueBatches = authoring.maxUniqueBatches,
                vrFrameSkipScrolling = authoring.vrFrameSkipScrolling,
                playerVelocityThreshold = authoring.playerVelocityThreshold
            });
            
            // Add buffer for object prefab entities
            var objectPrefabBuffer = AddBuffer<StaticObjectPrefabElement>(entity);
            
            // Add buffer for LOD spawn weights
            var lodWeightsBuffer = AddBuffer<StaticObjectLODWeights>(entity);
            
            // Add buffer for object type spawn weights
            var typeSpawnWeightsBuffer = AddBuffer<StaticObjectTypeSpawnWeight>(entity);
            
            int objectTypeCount = authoring.objectLODSets.Length;
            int validObjectTypes = 0;
            
            // Pre-compute total object type spawn weight for normalization
            float totalTypeSpawnWeight = 0f;
            int validTypeCount = 0;
            for (int i = 0; i < objectTypeCount; i++)
            {
                var lodSet = authoring.objectLODSets[i];
                if (lodSet == null || lodSet.lod0 == null)
                    continue;
                
                totalTypeSpawnWeight += lodSet.objectTypeSpawnWeight;
                validTypeCount++;
            }
            
            float defaultEqualTypeWeight = validTypeCount > 0 ? 1f / validTypeCount : 1f;
            if (totalTypeSpawnWeight < 0.001f)
            {
                Debug.LogWarning("[StaticObjectSpawner] All object type spawn weights are zero! Using equal distribution.", authoring);
            }
            
            // Convert GameObject prefabs to entity prefabs for each LOD set.
            // Entities.Graphics will bake MaterialMeshInfo onto each prefab entity automatically;
            // StaticObjectLODMeshInfoInitSystem reads those IDs at runtime to build the lookup buffer.
            for (int objectTypeIndex = 0; objectTypeIndex < objectTypeCount; objectTypeIndex++)
            {
                var lodSet = authoring.objectLODSets[objectTypeIndex];
                
                if (lodSet == null)
                {
                    Debug.LogWarning($"[StaticObjectSpawner] Null LOD set at index {objectTypeIndex}!", authoring);
                    continue;
                }
                
                GameObject[] lodPrefabs = new GameObject[] { lodSet.lod0, lodSet.lod1, lodSet.lod2 };
                
                if (lodPrefabs[0] == null)
                {
                    Debug.LogError($"[StaticObjectSpawner] Object type '{lodSet.objectTypeName}' missing LOD0 (required)! Skipping this object type.", authoring);
                    continue;
                }
                
                // Normalize LOD spawn weights to sum to 1.0
                float totalWeight = lodSet.lod0SpawnWeight + lodSet.lod1SpawnWeight + lodSet.lod2SpawnWeight;
                float normalizedLOD0Weight = 0.6f;
                float normalizedLOD1Weight = 0.3f;
                float normalizedLOD2Weight = 0.1f;
                
                if (totalWeight > 0.001f)
                {
                    normalizedLOD0Weight = lodSet.lod0SpawnWeight / totalWeight;
                    normalizedLOD1Weight = lodSet.lod1SpawnWeight / totalWeight;
                    normalizedLOD2Weight = lodSet.lod2SpawnWeight / totalWeight;
                }
                else
                {
                    Debug.LogWarning($"[StaticObjectSpawner] Object type '{lodSet.objectTypeName}' has zero total LOD weight! Using default distribution (60/30/10).", authoring);
                }
                
                float normalizedTypeSpawnWeight = totalTypeSpawnWeight > 0.001f
                    ? lodSet.objectTypeSpawnWeight / totalTypeSpawnWeight
                    : defaultEqualTypeWeight;
                
                lodWeightsBuffer.Add(new StaticObjectLODWeights
                {
                    objectTypeIndex = validObjectTypes,
                    lod0Weight = normalizedLOD0Weight,
                    lod1Weight = normalizedLOD1Weight,
                    lod2Weight = normalizedLOD2Weight
                });
                
                typeSpawnWeightsBuffer.Add(new StaticObjectTypeSpawnWeight
                {
                    objectTypeIndex = validObjectTypes,
                    weight = normalizedTypeSpawnWeight
                });
                
                for (int lodLevel = 0; lodLevel < 3; lodLevel++)
                {
                    GameObject lodPrefab = lodPrefabs[lodLevel];
                    
                    if (lodPrefab != null)
                    {
                        // Ensure GPU instancing is enabled on every material — BRG requires this to
                        // batch multiple instances into a single draw call.
                        foreach (var renderer in lodPrefab.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            foreach (var mat in renderer.sharedMaterials)
                            {
                                if (mat != null && !mat.enableInstancing)
                                {
                                    mat.enableInstancing = true;
#if UNITY_EDITOR
                                    UnityEditor.EditorUtility.SetDirty(mat);
#endif
                                    Debug.Log($"[StaticObjectSpawner] Enabled GPU instancing on material '{mat.name}' for '{lodSet.objectTypeName}' LOD{lodLevel}.", mat);
                                }
                            }
                        }

                        Entity prefabEntity = GetEntity(lodPrefab, TransformUsageFlags.Dynamic);
                        objectPrefabBuffer.Add(new StaticObjectPrefabElement { prefabEntity = prefabEntity });
                    }
                    else
                    {
                        // Fallback: reuse best available lower LOD prefab.
                        GameObject fallbackPrefab = lodLevel == 1 ? lodPrefabs[0]
                            : (lodPrefabs[1] != null ? lodPrefabs[1] : lodPrefabs[0]);
                        
                        if (lodLevel == 1)
                            Debug.LogWarning($"[StaticObjectSpawner] Object '{lodSet.objectTypeName}' LOD1 missing, using LOD0 as fallback", authoring);
                        else
                            Debug.LogWarning($"[StaticObjectSpawner] Object '{lodSet.objectTypeName}' LOD2 missing, using LOD{(lodPrefabs[1] != null ? "1" : "0")} as fallback", authoring);
                        
                        if (fallbackPrefab != null)
                        {
                            Entity prefabEntity = GetEntity(fallbackPrefab, TransformUsageFlags.Dynamic);
                            objectPrefabBuffer.Add(new StaticObjectPrefabElement { prefabEntity = prefabEntity });
                        }
                    }
                }
                
                Debug.Log($"[StaticObjectSpawner] Baked object type '{lodSet.objectTypeName}' with {(lodPrefabs[1] != null ? "3" : lodPrefabs[2] != null ? "2" : "1")} LOD levels (LOD weights: {normalizedLOD0Weight:F2}/{normalizedLOD1Weight:F2}/{normalizedLOD2Weight:F2}, type spawn weight: {normalizedTypeSpawnWeight:F2})");
                validObjectTypes++;
            }
            
            if (objectPrefabBuffer.Length == 0)
            {
                Debug.LogError("[StaticObjectSpawner] No valid object prefabs were converted to entities!", authoring);
            }
            else
            {
                Debug.Log($"[StaticObjectSpawner] Baked {validObjectTypes} object types with {objectPrefabBuffer.Length} total LOD prefabs");
            }
        }
    }

    /// <summary>Clamps all inspector values (densities, distances, spawn counts) to valid ranges when values change.</summary>
    private void OnValidate()
    {
        // Ensure valid values
        minObjectsPerTile = Mathf.Max(0, minObjectsPerTile);
        maxObjectsPerTile = Mathf.Max(minObjectsPerTile, maxObjectsPerTile);
        maxSlopeDegrees = Mathf.Clamp(maxSlopeDegrees, 0f, 90f);
        maxObjectsSpawnedPerFrame = Mathf.Max(1, maxObjectsSpawnedPerFrame);
        
        // Validate LOD distances are in increasing order
        lod0Distance = Mathf.Max(1f, lod0Distance);
        lod1Distance = Mathf.Max(lod0Distance + 1f, lod1Distance);
        lod2Distance = Mathf.Max(lod1Distance + 1f, lod2Distance);
        lodHysteresis = Mathf.Max(0f, lodHysteresis);
        
        // Validate distance culling settings
        maxObjectRenderDistance = Mathf.Clamp(maxObjectRenderDistance, 100f, 1000f);
        
        // Normalize LOD spawn weights for each object type
        if (objectLODSets != null)
        {
            float totalTypeSpawnWeight = 0f;
            foreach (var lodSet in objectLODSets)
            {
                if (lodSet != null)
                {
                    float totalWeight = lodSet.lod0SpawnWeight + lodSet.lod1SpawnWeight + lodSet.lod2SpawnWeight;
                    if (totalWeight < 0.001f)
                    {
                        // Reset to defaults if all weights are zero
                        lodSet.lod0SpawnWeight = 0.6f;
                        lodSet.lod1SpawnWeight = 0.3f;
                        lodSet.lod2SpawnWeight = 0.1f;
                    }
                    
                    totalTypeSpawnWeight += lodSet.objectTypeSpawnWeight;
                }
            }
            
            if (totalTypeSpawnWeight < 0.001f)
            {
                foreach (var lodSet in objectLODSets)
                {
                    if (lodSet != null)
                        lodSet.objectTypeSpawnWeight = 1f;
                }
            }
        }
    }
}
