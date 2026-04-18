using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for tree spawning configuration.
/// Place this on the same GameObject as TerrainConfigAuthoring to enable tree spawning on terrain tiles.
/// </summary>
public class TreeSpawnerConfigAuthoring : MonoBehaviour
{
    [Header("Tree Prefabs")]
    [Tooltip("Array of tree prefabs to randomly spawn on terrain tiles")]
    public GameObject[] treePrefabs;
    
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

    public class Baker : Baker<TreeSpawnerConfigAuthoring>
    {
        public override void Bake(TreeSpawnerConfigAuthoring authoring)
        {
            // Validate that we have tree prefabs
            if (authoring.treePrefabs == null || authoring.treePrefabs.Length == 0)
            {
                Debug.LogWarning("[TreeSpawner] No tree prefabs assigned to TreeSpawnerConfigAuthoring!", authoring);
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
            
            // Add buffer for tree prefab entities
            var treePrefabBuffer = AddBuffer<TreePrefabElement>(entity);
            
            // Create arrays for mesh/material references
            var treeMeshes = new Mesh[authoring.treePrefabs.Length];
            var treeMaterials = new Material[authoring.treePrefabs.Length];
            int validCount = 0;
            
            // Convert GameObject prefabs to entity prefabs and extract mesh/material
            for (int i = 0; i < authoring.treePrefabs.Length; i++)
            {
                var treePrefab = authoring.treePrefabs[i];
                
                if (treePrefab != null)
                {
                    Entity prefabEntity = GetEntity(treePrefab, TransformUsageFlags.Dynamic);
                    treePrefabBuffer.Add(new TreePrefabElement
                    {
                        prefabEntity = prefabEntity
                    });
                    
                    // Extract mesh and material from GameObject prefab
                    Mesh mesh = null;
                    Material material = null;
                    
                    // Try to get MeshFilter from prefab or its children
                    var meshFilter = treePrefab.GetComponentInChildren<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        mesh = meshFilter.sharedMesh;
                        Debug.Log($"[TreeSpawner] Found mesh '{mesh.name}' on prefab '{treePrefab.name}'");
                    }
                    else
                    {
                        Debug.LogWarning($"[TreeSpawner] No MeshFilter with sharedMesh found on prefab '{treePrefab.name}'", authoring);
                    }
                    
                    // Try to get MeshRenderer from prefab or its children
                    var meshRenderer = treePrefab.GetComponentInChildren<MeshRenderer>();
                    if (meshRenderer != null && meshRenderer.sharedMaterial != null)
                    {
                        material = meshRenderer.sharedMaterial;
                        Debug.Log($"[TreeSpawner] Found material '{material.name}' on prefab '{treePrefab.name}'");
                    }
                    else
                    {
                        Debug.LogWarning($"[TreeSpawner] No MeshRenderer with sharedMaterial found on prefab '{treePrefab.name}'", authoring);
                    }
                    
                    // Store mesh/material references
                    treeMeshes[validCount] = mesh;
                    treeMaterials[validCount] = material;
                    validCount++;
                    
                    if (mesh == null || material == null)
                    {
                        Debug.LogWarning($"[TreeSpawner] Tree prefab '{treePrefab.name}' missing mesh or material! Mesh: {mesh}, Material: {material}", authoring);
                    }
                }
                else
                {
                    Debug.LogWarning("[TreeSpawner] Null tree prefab in array!", authoring);
                }
            }
            
            // Add managed component with mesh/material data
            if (validCount > 0)
            {
                AddComponentObject(entity, new TreePrefabMeshMaterialData
                {
                    meshes = treeMeshes,
                    materials = treeMaterials
                });
            }
            
            if (treePrefabBuffer.Length == 0)
            {
                Debug.LogError("[TreeSpawner] No valid tree prefabs were converted to entities!", authoring);
            }
            else
            {
                Debug.Log($"[TreeSpawner] Baked {treePrefabBuffer.Length} tree prefabs");
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
    }
}
