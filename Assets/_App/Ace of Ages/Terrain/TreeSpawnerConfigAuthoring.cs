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
    [Range(0, 50)]
    public int minTreesPerTile = 5;
    
    [Tooltip("Maximum number of trees per tile")]
    [Range(0, 50)]
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
            
            // Convert GameObject prefabs to entity prefabs
            foreach (var treePrefab in authoring.treePrefabs)
            {
                if (treePrefab != null)
                {
                    Entity prefabEntity = GetEntity(treePrefab, TransformUsageFlags.Dynamic);
                    treePrefabBuffer.Add(new TreePrefabElement
                    {
                        prefabEntity = prefabEntity
                    });
                }
                else
                {
                    Debug.LogWarning("[TreeSpawner] Null tree prefab in array!", authoring);
                }
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
