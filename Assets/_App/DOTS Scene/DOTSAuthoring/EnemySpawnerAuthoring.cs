using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Authoring component for the EnemySpawner system. It allows designers to specify a SplineContainer and sample count
/// in the editor, which are then used to create a SplineDataComponent that defines the path for spawning enemies.
/// The Baker class converts the authoring data into runtime components and blob assets for efficient access during gameplay.
/// </summary>
public class EnemySpawnerAuthoring : MonoBehaviour
{
    /// <summary> The SplineContainer that defines the path along which enemies will be spawned. This should be assigned in the Unity Editor./// </summary>
    [SerializeField] private SplineContainer loopSpline;
    
    /// <summary> The number of sample points to generate along the spline. Higher values will result in smoother movement but may increase memory usage./// </summary>
    [SerializeField] private int sampleCount = 100;
    
    public class Baker : Baker<EnemySpawnerAuthoring>
    {
        public override void Bake(EnemySpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnemySpawner
            {
                doSpawn = false,
                splineData = CreateSplineDataComponent(authoring.loopSpline, authoring.sampleCount),
            });
        }

        /// <summary>
        /// Creates a SplineDataComponent from the given SplineContainer. It samples the spline at a specified number of points and stores the data in a blob asset for efficient access at runtime.
        /// </summary>
        /// <param name="splineContainer"></param>
        /// <param name="sampleCount"></param>
        /// <returns></returns>
        private SplineDataComponent CreateSplineDataComponent(SplineContainer splineContainer, int sampleCount)
        {
            if (splineContainer is null)
            {
                Debug.Log(
                    $"From {nameof(EnemySpawnerAuthoring.Baker)}.CreateSplineDataComponent(). spline container is null");
                return default;
            }

            var spline = splineContainer.Spline;
            float4x4 transformationMatrix = splineContainer.transform.localToWorldMatrix;

            var splineDataBlobAssetRef = SplineDataBlob.CreateSplineDataBlobAssetRef(
                spline,
                transformationMatrix,
                sampleCount);

            AddBlobAsset(ref splineDataBlobAssetRef, out _);

            return new SplineDataComponent
            {
                splineData = splineDataBlobAssetRef,
            };
        }
    }
}


/// <summary>
/// Component that holds data for spawning enemies along a spline. It contains a flag to trigger spawning and a reference to the spline data that defines the path for the enemies to follow.
/// </summary>
public struct EnemySpawner : IComponentData
{
    public bool doSpawn;
    public SplineDataComponent splineData;
}
