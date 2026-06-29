using Unity.Entities;

using Unity.Mathematics;

using UnityEngine;



/// <summary>

/// Authoring component for static object spawning configuration.

/// Place this on the same GameObject as TerrainConfigAuthoring to enable static object spawning on terrain tiles.

/// </summary>

public class StaticObjectSpawnerConfigAuthoring : MonoBehaviour

{

    [Header("Static Object LOD Sets")]

    [Tooltip("Object types with LOD variants and relative spawn weights (normalized at bake time)")]

    public StaticObjectLODSetEntry[] objectLODSets;

    

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

    [Range(0, 800)]

    public int minObjectsPerTile = 5;

    

    [Tooltip("Maximum number of objects per tile")]

    [Range(0, 800)]

    public int maxObjectsPerTile = 15;

    

    [Header("Spawn Filtering")]

    [Tooltip("Maximum slope angle in degrees (0 = flat, 90 = vertical cliff). Objects won't spawn on steeper slopes.")]

    [Range(0f, 90f)]

    public float maxSlopeDegrees = 45f;

    

    [Header("Performance")]

    [Tooltip("Maximum number of object entities to spawn or destroy per frame (prevents ECB playback stuttering)")]

    [Range(1, 100)]

    public int maxObjectsSpawnedPerFrame = 20;



    [Tooltip("Maximum objects to spawn per frame for tiles within LOD0 distance (near-field). Default: 300.")]

    [Range(1, 800)]

    public int maxNearObjectsSpawnedPerFrame = 300;



    [Tooltip("Maximum spawn-position rejection attempts per frame, divided across tiles calculating positions")]

    [Range(500, 50000)]

    public int maxPositionCalcAttemptsPerFrame = 4000;

    

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

                slopeThreshold = slopeThreshold,

                maxObjectsSpawnedPerFrame = authoring.maxObjectsSpawnedPerFrame,

                maxNearObjectsSpawnedPerFrame = authoring.maxNearObjectsSpawnedPerFrame,

                maxPositionCalcAttemptsPerFrame = authoring.maxPositionCalcAttemptsPerFrame,

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

                maxChunksUpdatedPerFrame = 25,

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

            AddBuffer<StaticObjectLODMaterialMeshInfoElement>(entity);
            AddBuffer<StaticObjectLODRenderBoundsElement>(entity);
            AddBuffer<StaticObjectTypeMaxRenderBoundsElement>(entity);

            

            // Add buffer for object type spawn weights

            var typeSpawnWeightsBuffer = AddBuffer<StaticObjectTypeSpawnWeight>(entity);

            

            // Add buffer for per-object-type billboard flags

            var billboardBuffer = AddBuffer<StaticObjectBillboardTypeElement>(entity);

            

            // Add buffer for per-object-type scale config

            var typeScaleBuffer = AddBuffer<StaticObjectTypeScaleElement>(entity);

            

            int objectTypeCount = authoring.objectLODSets.Length;

            int validObjectTypes = 0;

            

            // Pre-compute total object type spawn weight for normalization

            float totalTypeSpawnWeight = 0f;

            int validTypeCount = 0;

            for (int i = 0; i < objectTypeCount; i++)

            {

                var entry = authoring.objectLODSets[i];

                var lodSet = entry.lodSet;

                if (lodSet == null || lodSet.lod0 == null)

                    continue;

                

                totalTypeSpawnWeight += entry.spawnWeight;

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

                var entry = authoring.objectLODSets[objectTypeIndex];

                var lodSet = entry.lodSet;

                

                if (lodSet == null)

                {

                    Debug.LogWarning($"[StaticObjectSpawner] Null LOD set at index {objectTypeIndex}!", authoring);

                    continue;

                }

                

                GameObject[] lodPrefabs = new GameObject[] { lodSet.lod0, lodSet.lod1, lodSet.lod2 };

                

                if (lodPrefabs[0] == null)

                {

                    Debug.LogError($"[StaticObjectSpawner] Object type '{lodSet.name}' missing LOD0 (required)! Skipping this object type.", authoring);

                    continue;

                }

                

                float normalizedTypeSpawnWeight = totalTypeSpawnWeight > 0.001f

                    ? entry.spawnWeight / totalTypeSpawnWeight

                    : defaultEqualTypeWeight;

                

                typeSpawnWeightsBuffer.Add(new StaticObjectTypeSpawnWeight

                {

                    objectTypeIndex = validObjectTypes,

                    weight = normalizedTypeSpawnWeight

                });

                

                billboardBuffer.Add(new StaticObjectBillboardTypeElement

                {

                    isBillboard = lodSet.lod2IsBillboard

                });

                

                float3 lossyScale = lodSet.lod0.transform.lossyScale;

                float baseScale = math.cmax(lossyScale);

                if (baseScale <= 0f)

                    baseScale = 1f;

                

                float lod1Scale = lodPrefabs[1] != null

                    ? math.cmax(lodPrefabs[1].transform.lossyScale)

                    : baseScale;

                float lod2Scale = lodPrefabs[2] != null

                    ? math.cmax(lodPrefabs[2].transform.lossyScale)

                    : (lodPrefabs[1] != null ? lod1Scale : baseScale);

                if (lod1Scale <= 0f)

                    lod1Scale = baseScale;

                if (lod2Scale <= 0f)

                    lod2Scale = lod1Scale;

                

                typeScaleBuffer.Add(new StaticObjectTypeScaleElement

                {

                    baseScale = baseScale,

                    maxScaleDelta = math.max(0f, entry.maxScaleDelta),

                    lod1ScaleMultiplier = lod1Scale / baseScale,

                    lod2ScaleMultiplier = lod2Scale / baseScale

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

                                    Debug.Log($"[StaticObjectSpawner] Enabled GPU instancing on material '{mat.name}' for '{lodSet.name}' LOD{lodLevel}.", mat);

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

                            Debug.LogWarning($"[StaticObjectSpawner] Object '{lodSet.name}' LOD1 missing, using LOD0 as fallback", authoring);

                        else

                            Debug.LogWarning($"[StaticObjectSpawner] Object '{lodSet.name}' LOD2 missing, using LOD{(lodPrefabs[1] != null ? "1" : "0")} as fallback", authoring);

                        

                        if (fallbackPrefab != null)

                        {

                            Entity prefabEntity = GetEntity(fallbackPrefab, TransformUsageFlags.Dynamic);

                            objectPrefabBuffer.Add(new StaticObjectPrefabElement { prefabEntity = prefabEntity });

                        }

                    }

                }

                

                Debug.Log($"[StaticObjectSpawner] Baked object type '{lodSet.name}' with {(lodPrefabs[1] != null ? "3" : lodPrefabs[2] != null ? "2" : "1")} LOD levels (type spawn weight: {normalizedTypeSpawnWeight:F2})");

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

        maxNearObjectsSpawnedPerFrame = Mathf.Max(1, maxNearObjectsSpawnedPerFrame);

        maxPositionCalcAttemptsPerFrame = Mathf.Max(500, maxPositionCalcAttemptsPerFrame);

        

        // Validate LOD distances are in increasing order

        lod0Distance = Mathf.Max(1f, lod0Distance);

        lod1Distance = Mathf.Max(lod0Distance + 1f, lod1Distance);

        lod2Distance = Mathf.Max(lod1Distance + 1f, lod2Distance);

        lodHysteresis = Mathf.Max(0f, lodHysteresis);

        

        // Validate distance culling settings

        maxObjectRenderDistance = Mathf.Clamp(maxObjectRenderDistance, 100f, 1000f);

        

        if (objectLODSets == null)

            return;



        float totalTypeSpawnWeight = 0f;

        for (int i = 0; i < objectLODSets.Length; i++)

        {

            var entry = objectLODSets[i];

            if (entry.lodSet == null)

                continue;



            entry.spawnWeight = Mathf.Max(0f, entry.spawnWeight);

            entry.maxScaleDelta = Mathf.Max(0f, entry.maxScaleDelta);

            objectLODSets[i] = entry;

            totalTypeSpawnWeight += entry.spawnWeight;

        }



        if (totalTypeSpawnWeight < 0.001f)

        {

            for (int i = 0; i < objectLODSets.Length; i++)

            {

                var entry = objectLODSets[i];

                if (entry.lodSet == null)

                    continue;



                entry.spawnWeight = 1f;

                objectLODSets[i] = entry;

            }

        }

    }

}

