using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for the EnemySpawner system. It allows designers to reference a GameObject with a 
/// SplineComponentAuthoring that defines the path along which enemies will be spawned.
/// The Baker class converts the authoring data into runtime components.
/// </summary>
public class EnemySpawnerAuthoring : MonoBehaviour
{
    /// <summary> 
    /// The GameObject with a SplineComponentAuthoring that defines the path along which enemies will be spawned. 
    /// This should be assigned in the Unity Editor. The referenced GameObject must have a SplineComponentAuthoring component.
    /// </summary>
    [SerializeField] private GameObject loopSpline;
    
    [Header("Formation Settings")]
    [Tooltip("Number of enemies to spawn in the formation (default 10 for bowling pins)")]
    [SerializeField] private int formationCount = 10;
    
    [Tooltip("Distance between enemies in the formation (units)")]
    [SerializeField] private float formationSpacing = 2f;
    
    [Tooltip("Movement speed for enemies during approach and exit phases")]
    [SerializeField] private float formationSpeed = 5f;
    
    [Header("Spawn Behavior")]
    [Tooltip("Distance ahead of spline start to spawn formation (perpendicular to path, outside player view)")]
    [SerializeField] private float spawnDistance = 75f;
    
    
    public class Baker : Baker<EnemySpawnerAuthoring>
    {
        public override void Bake(EnemySpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // Get the entity for the referenced spline GameObject
            Entity splineEntity = GetEntity(authoring.loopSpline, TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new EnemySpawner
            {
                doSpawn = false,
                splineEntity = splineEntity,
                formationCount = authoring.formationCount,
                formationSpacing = authoring.formationSpacing,
                spawnDistance = authoring.spawnDistance,
                formationSpeed = authoring.formationSpeed,
            });
        }
    }
}

/// <summary>
/// Component that holds data for spawning enemies along a spline. It contains a flag to trigger spawning 
/// and a reference to the spline entity that has the SplineDataComponent defining the path for enemies to follow.
/// </summary>
public struct EnemySpawner : IComponentData
{
    public bool doSpawn;
    public Entity splineEntity;
    public int formationCount;
    public float formationSpacing;
    public float spawnDistance;
    public float formationSpeed;
}
