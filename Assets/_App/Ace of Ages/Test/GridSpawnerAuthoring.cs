using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for the GridSpawner system. It allows designers to reference a GameObject prefab
/// that will be spawned in a grid pattern in the XY plane.
/// The Baker class converts the authoring data into runtime components.
/// </summary>
public class GridSpawnerAuthoring : MonoBehaviour
{
    [Header("Prefab Settings")]
    [Tooltip("The GameObject prefab to spawn in a grid pattern")]
    [SerializeField] private GameObject prefab;
    
    [Header("Grid Settings")]
    [Tooltip("Number of objects in each dimension (gridSize x gridSize)")]
    [SerializeField] private int gridSize = 75;
    
    [Tooltip("Distance between each object in the grid (units)")]
    [SerializeField] private float spacing = 2f;
    
    [Tooltip("Z-position for the entire grid")]
    [SerializeField] private float zPosition = 100f;

    /// <summary>Bakes grid configuration (prefab entity, size, spacing, Z position) into a <see cref="GridSpawner"/> component.</summary>
    public class Baker : Baker<GridSpawnerAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(GridSpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // Get the entity for the referenced prefab GameObject
            Entity prefabEntity = GetEntity(authoring.prefab, TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new GridSpawner
            {
                prefabEntity = prefabEntity,
                gridSize = authoring.gridSize,
                spacing = authoring.spacing,
                zPosition = authoring.zPosition,
                hasSpawned = false
            });
        }
    }
}

/// <summary>
/// Component that holds data for spawning objects in a grid pattern.
/// It contains the prefab reference, grid configuration, and a flag to track whether spawning has occurred.
/// </summary>
public struct GridSpawner : IComponentData
{
    public Entity prefabEntity;
    public int gridSize;
    public float spacing;
    public float zPosition;
    public bool hasSpawned;
}

