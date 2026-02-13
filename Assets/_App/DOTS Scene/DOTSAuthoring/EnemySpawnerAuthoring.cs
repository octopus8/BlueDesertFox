using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

public class EnemySpawnerAuthoring : MonoBehaviour
{
    [SerializeField] private SplineContainer splineContainer;
    
    public class Baker : Baker<EnemySpawnerAuthoring>
    {
        public override void Bake(EnemySpawnerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EnemySpawner
            {
                doSpawn = false,
                spline = CreateSplineBlobAssetComponent(authoring.splineContainer),
            });
        }

        private SplineBlobAssetComponent CreateSplineBlobAssetComponent(SplineContainer splineContainer)
        {
            if (splineContainer is null)
            {
                Debug.Log(
                    $"From {nameof(EnemySpawnerAuthoring.Baker)}.CreateSplineBlobAssetComponent(). spline container is null");
                return default;
            }

            var spline = splineContainer.Spline;
            float4x4 transformationMatrix = splineContainer.transform.localToWorldMatrix;
            using var nativeSpline = new NativeSpline(spline, Allocator.Temp);

            var nativeSplineBlobAssetRef = NativeSplineBlob.CreateNativeSplineBlobAssetRef(
                nativeSpline,
                spline.Closed,
                transformationMatrix);

            AddBlobAsset(ref nativeSplineBlobAssetRef, out _);

            return new SplineBlobAssetComponent
            {
                reference = nativeSplineBlobAssetRef,
            };
        }
    }
}

public struct EnemySpawner : IComponentData
{
    public bool doSpawn;
    public SplineBlobAssetComponent spline;
}
