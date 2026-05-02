using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for tree spawning configuration.
/// Place this on the same GameObject as TerrainConfigAuthoring to enable tree spawning on terrain tiles.
/// </summary>
public class TreeSpawnerConfigAuthoring : MonoBehaviour
{
    /// <summary>
    /// Container for a tree type with 3 LOD levels (LOD0 = high detail, LOD2 = low detail).
    /// </summary>
    [System.Serializable]
    public class TreeLODSet
    {
        [Tooltip("Name for this tree type (for debugging)")]
        public string treeTypeName = "Tree";
        
        [Tooltip("Highest detail mesh (0-50m from player)")]
        public GameObject lod0;
        
        [Tooltip("Medium detail mesh (50-150m from player)")]
        public GameObject lod1;
        
        [Tooltip("Lowest detail mesh (150m+ from player)")]
        public GameObject lod2;
    }
    
    [Header("Tree LOD Sets")]
    [Tooltip("Array of tree types with LOD variants to randomly spawn on terrain tiles")]
    public TreeLODSet[] treeLODSets;
    
    [Header("LOD Distance Thresholds")]
    [Tooltip("Distance threshold for LOD0->LOD1 transition (meters)")]
    public float lod0Distance = 50f;
    
    [Tooltip("Distance threshold for LOD1->LOD2 transition (meters)")]
    public float lod1Distance = 150f;
    
    [Tooltip("Distance beyond which trees use LOD2 (meters)")]
    public float lod2Distance = 300f;
    
    [Tooltip("Hysteresis buffer to prevent LOD flickering (meters). Adds/subtracts from thresholds based on transition direction.")]
    [Range(0f, 20f)]
    public float lodHysteresis = 5f;
    
    [Header("Spawn Density")]
    [Tooltip("Minimum number of trees per tile")]
    [Range(0, 200)]
    public int minTreesPerTile = 5;
    
    [Tooltip("Maximum number of trees per tile")]
    [Range(0, 200)]
    public int maxTreesPerTile = 15;
    
    [Header("Spawn Filtering")]
    [Tooltip("Minimum terrain height for tree spawning (world Y coordinate)")]
    public float minSpawnHeight = -100f;
    
    [Tooltip("Maximum terrain height for tree spawning (world Y coordinate)")]
    public float maxSpawnHeight = 100f;
    
    [Tooltip("Maximum slope angle in degrees (0 = flat, 90 = vertical cliff). Trees won't spawn on steeper slopes.")]
    [Range(0f, 90f)]
    public float maxSlopeDegrees = 45f;
    
    [Header("Performance")]
    [Tooltip("Maximum number of tree entities to spawn per frame (prevents stuttering)")]
    [Range(1, 100)]
    public int maxTreesSpawnedPerFrame = 20;
    
    [Header("Debug")]
    [Tooltip("Enable tree LOD and spawning debug logging (disable to reduce console spam)")]
    public bool enableTreeLODDebug;
    
    [Header("Distance Culling (VR Performance)")]
    [Tooltip("Enable distance-based culling for tree rendering. Trees beyond maxTreeRenderDistance won't render. Recommended ON for VR.")]
    public bool enableDistanceCulling = true;
    
    [Tooltip("Maximum distance to render trees in meters. Trees beyond this distance are culled (not rendered). Quest 3 recommended: 300-500m.")]
    [Range(100f, 1000f)]
    public float maxTreeRenderDistance = 400f;

    public class Baker : Baker<TreeSpawnerConfigAuthoring>
    {
        public override void Bake(TreeSpawnerConfigAuthoring authoring)
        {
            // Validate that we have tree LOD sets
            if (authoring.treeLODSets == null || authoring.treeLODSets.Length == 0)
            {
                Debug.LogWarning("[TreeSpawner] No tree LOD sets assigned to TreeSpawnerConfigAuthoring!", authoring);
                return;
            }
            
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            // Pre-calculate slope threshold (cosine of max slope angle)
            // This avoids expensive acos() calls at runtime
            float slopeThreshold = math.cos(math.radians(authoring.maxSlopeDegrees));
            
            // Create tree spawner config singleton
            AddComponent(entity, new TreeSpawnerConfig
            {
                minTreesPerTile = authoring.minTreesPerTile,
                maxTreesPerTile = authoring.maxTreesPerTile,
                minSpawnHeight = authoring.minSpawnHeight,
                maxSpawnHeight = authoring.maxSpawnHeight,
                slopeThreshold = slopeThreshold,
                maxTreesSpawnedPerFrame = authoring.maxTreesSpawnedPerFrame
            });
            
            // Create tree LOD config singleton
            AddComponent(entity, new TreeLODConfig
            {
                lod0Distance = authoring.lod0Distance,
                lod1Distance = authoring.lod1Distance,
                lod2Distance = authoring.lod2Distance,
                hysteresisDelta = authoring.lodHysteresis,
                lodsPerTreeType = 3, // Hardcoded to 3 LOD levels
                maxChunksUpdatedPerFrame = 7,
                enableTreeLODDebug = authoring.enableTreeLODDebug,
                enableDistanceCulling = authoring.enableDistanceCulling,
                maxTreeRenderDistance = authoring.maxTreeRenderDistance
            });
            
            // Add buffer for tree prefab entities
            var treePrefabBuffer = AddBuffer<TreePrefabElement>(entity);
            
            // Calculate total mesh count (3 LODs per tree type)
            int treeTypeCount = authoring.treeLODSets.Length;
            int totalMeshCount = treeTypeCount * 3;
            
            // Create arrays for mesh/material references (flattened: [Tree0_LOD0, Tree0_LOD1, Tree0_LOD2, Tree1_LOD0, ...])
            var treeMeshes = new Mesh[totalMeshCount];
            var treeMaterials = new Material[totalMeshCount];
            int validTreeTypes = 0;
            
            // Convert GameObject prefabs to entity prefabs and extract mesh/material for each LOD
            for (int treeTypeIndex = 0; treeTypeIndex < treeTypeCount; treeTypeIndex++)
            {
                var lodSet = authoring.treeLODSets[treeTypeIndex];
                
                if (lodSet == null)
                {
                    Debug.LogWarning($"[TreeSpawner] Null LOD set at index {treeTypeIndex}!", authoring);
                    continue;
                }
                
                // Array to hold LOD prefabs [LOD0, LOD1, LOD2]
                GameObject[] lodPrefabs = new GameObject[] { lodSet.lod0, lodSet.lod1, lodSet.lod2 };
                
                // Validate that at least LOD0 exists
                if (lodPrefabs[0] == null)
                {
                    Debug.LogError($"[TreeSpawner] Tree type '{lodSet.treeTypeName}' missing LOD0 (required)! Skipping this tree type.", authoring);
                    continue;
                }
                
                // Process each LOD level
                Mesh[] lodMeshes = new Mesh[3];
                Material[] lodMaterials = new Material[3];
                
                for (int lodLevel = 0; lodLevel < 3; lodLevel++)
                {
                    GameObject lodPrefab = lodPrefabs[lodLevel];
                    
                    if (lodPrefab != null)
                    {
                        // Extract mesh
                        var meshFilter = lodPrefab.GetComponentInChildren<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            lodMeshes[lodLevel] = meshFilter.sharedMesh;
                        }
                        else
                        {
                            Debug.LogWarning($"[TreeSpawner] Tree '{lodSet.treeTypeName}' LOD{lodLevel} missing MeshFilter/sharedMesh", authoring);
                        }
                        
                        // Extract material
                        var meshRenderer = lodPrefab.GetComponentInChildren<MeshRenderer>();
                        if (meshRenderer != null && meshRenderer.sharedMaterial != null)
                        {
                            lodMaterials[lodLevel] = meshRenderer.sharedMaterial;
                        }
                        else
                        {
                            Debug.LogWarning($"[TreeSpawner] Tree '{lodSet.treeTypeName}' LOD{lodLevel} missing MeshRenderer/sharedMaterial", authoring);
                        }
                        
                        // Convert to entity prefab and store in buffer (one entry per LOD)
                        Entity prefabEntity = GetEntity(lodPrefab, TransformUsageFlags.Dynamic);
                        treePrefabBuffer.Add(new TreePrefabElement
                        {
                            prefabEntity = prefabEntity
                        });
                    }
                    else
                    {
                        // Apply fallback logic for missing LODs
                        if (lodLevel == 1)
                        {
                            // LOD1 missing -> use LOD0
                            lodMeshes[1] = lodMeshes[0];
                            lodMaterials[1] = lodMaterials[0];
                            Debug.LogWarning($"[TreeSpawner] Tree '{lodSet.treeTypeName}' LOD1 missing, using LOD0 as fallback", authoring);
                            
                            // Create a prefab entity reference (use LOD0's prefab)
                            if (lodPrefabs[0] != null)
                            {
                                Entity prefabEntity = GetEntity(lodPrefabs[0], TransformUsageFlags.Dynamic);
                                treePrefabBuffer.Add(new TreePrefabElement { prefabEntity = prefabEntity });
                            }
                        }
                        else if (lodLevel == 2)
                        {
                            // LOD2 missing -> use LOD1 (or LOD0 if LOD1 also missing)
                            lodMeshes[2] = lodMeshes[1] != null ? lodMeshes[1] : lodMeshes[0];
                            lodMaterials[2] = lodMaterials[1] != null ? lodMaterials[1] : lodMaterials[0];
                            Debug.LogWarning($"[TreeSpawner] Tree '{lodSet.treeTypeName}' LOD2 missing, using LOD{(lodMeshes[1] != null ? "1" : "0")} as fallback", authoring);
                            
                            // Create a prefab entity reference (use best available LOD)
                            GameObject fallbackPrefab = lodPrefabs[1] != null ? lodPrefabs[1] : lodPrefabs[0];
                            if (fallbackPrefab != null)
                            {
                                Entity prefabEntity = GetEntity(fallbackPrefab, TransformUsageFlags.Dynamic);
                                treePrefabBuffer.Add(new TreePrefabElement { prefabEntity = prefabEntity });
                            }
                        }
                    }
                }
                
                // Store meshes/materials in flattened array
                int baseIndex = validTreeTypes * 3;
                treeMeshes[baseIndex + 0] = lodMeshes[0];
                treeMeshes[baseIndex + 1] = lodMeshes[1];
                treeMeshes[baseIndex + 2] = lodMeshes[2];
                treeMaterials[baseIndex + 0] = lodMaterials[0];
                treeMaterials[baseIndex + 1] = lodMaterials[1];
                treeMaterials[baseIndex + 2] = lodMaterials[2];
                
                Debug.Log($"[TreeSpawner] Baked tree type '{lodSet.treeTypeName}' with {(lodPrefabs[1] != null ? "3" : lodPrefabs[2] != null ? "2" : "1")} LOD levels");
                validTreeTypes++;
            }
            
            // Add managed component with mesh/material data (legacy - still used by spawning system)
            if (validTreeTypes > 0)
            {
                int validMeshCount = validTreeTypes * 3;
                var finalMeshes = new Mesh[validMeshCount];
                var finalMaterials = new Material[validMeshCount];
                System.Array.Copy(treeMeshes, finalMeshes, validMeshCount);
                System.Array.Copy(treeMaterials, finalMaterials, validMeshCount);
                
                AddComponentObject(entity, new TreePrefabMeshMaterialData
                {
                    meshes = finalMeshes,
                    materials = finalMaterials
                });
                
                // Add new GlobalTreeRenderingData singleton for optimized rendering system
                AddComponentObject(entity, new GlobalTreeRenderingData
                {
                    meshes = finalMeshes,
                    materials = finalMaterials
                });
            }
            
            if (treePrefabBuffer.Length == 0)
            {
                Debug.LogError("[TreeSpawner] No valid tree prefabs were converted to entities!", authoring);
            }
            else
            {
                Debug.Log($"[TreeSpawner] Baked {validTreeTypes} tree types with {treePrefabBuffer.Length} total LOD prefabs");
            }
        }
    }

    private void OnValidate()
    {
        // Ensure valid values
        minTreesPerTile = Mathf.Max(0, minTreesPerTile);
        maxTreesPerTile = Mathf.Max(minTreesPerTile, maxTreesPerTile);
        maxSlopeDegrees = Mathf.Clamp(maxSlopeDegrees, 0f, 90f);
        maxTreesSpawnedPerFrame = Mathf.Max(1, maxTreesSpawnedPerFrame);
        
        // Validate LOD distances are in increasing order
        lod0Distance = Mathf.Max(1f, lod0Distance);
        lod1Distance = Mathf.Max(lod0Distance + 1f, lod1Distance);
        lod2Distance = Mathf.Max(lod1Distance + 1f, lod2Distance);
        lodHysteresis = Mathf.Max(0f, lodHysteresis);
        
        // Validate distance culling settings
        maxTreeRenderDistance = Mathf.Clamp(maxTreeRenderDistance, 100f, 1000f);
    }
}
