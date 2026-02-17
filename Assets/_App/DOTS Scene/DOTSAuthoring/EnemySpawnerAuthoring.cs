using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

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
    [SerializeField] private GameObject splineObject;
    
    public class Baker : Baker<EnemySpawnerAuthoring>
    {
        public override void Bake(EnemySpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // Get the entity for the referenced spline GameObject
            Entity splineEntity = GetEntity(authoring.splineObject, TransformUsageFlags.Dynamic);
            
            AddComponent(entity, new EnemySpawner
            {
                doSpawn = false,
                splineEntity = splineEntity,
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
}
