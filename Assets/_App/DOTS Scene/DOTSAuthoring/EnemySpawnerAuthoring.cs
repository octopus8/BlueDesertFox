using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

public class EnemySpawnerAuthoring : MonoBehaviour
{
    [SerializeField] private SplineContainer splineContainer;
    [SerializeField] private int sampleCount = 100;
    
    public class Baker : Baker<EnemySpawnerAuthoring>
    {
        public override void Bake(EnemySpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnemySpawner
            {
                doSpawn = false,
                splineData = CreateSplineDataComponent(authoring.splineContainer, authoring.sampleCount),
            });
        }

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

public struct EnemySpawner : IComponentData
{
    public bool doSpawn;
    public SplineDataComponent splineData;
}
