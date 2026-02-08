using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;


[RequireComponent(typeof(SplineContainer))]
public class SplineComponentAuthoring : MonoBehaviour
{
    public class SplineComponentBaker: Baker<SplineComponentAuthoring>
    {
        public override void Bake(SplineComponentAuthoring authoring)
        {
            var splineContainer = GetComponent<SplineContainer>();

            if (splineContainer is null)
            {
                Debug.Log($"From {nameof(SplineComponentBaker)}.Bake(). spline container is null");
                return;
            }

            var spline = splineContainer.Spline;
            float4x4 transformationMatrix = splineContainer.transform.localToWorldMatrix;
            using var nativeSpline = new NativeSpline(spline, Allocator.Temp);

            var nativeSplineBlobAssetRef = NativeSplineBlob.CreateNativeSplineBlobAssetRef(
                nativeSpline,
                spline.Closed,
                transformationMatrix);
       
            var entity = GetEntity(TransformUsageFlags.Dynamic);
       
            AddBlobAsset(ref nativeSplineBlobAssetRef, out _);
       
            AddComponent(entity, new SplineBlobAssetComponent
            {
                reference = nativeSplineBlobAssetRef,
            });
        }
    }
}




public struct SplineBlobAssetComponent : IComponentData
{
    public BlobAssetReference<NativeSplineBlob> reference;
}
 

public struct NativeSplineBlob
{
    public BlobArray<BezierKnot> knots;
    public bool closed;
    public float4x4 transformMatrix;

    /// <summary>
    /// NativeSpline must be disposed on caller site.
    /// </summary>
    public NativeSpline CreateNativeSpline(Allocator allocator)
    {
       
        using var nativeList = new NativeList<BezierKnot>(initialCapacity: knots.Length, Allocator.Temp);

        for (int i = 0; i < knots.Length; i++)
        {
            nativeList.Add(knots[i]);
        }
  
        var readonlyKnots = new KnotsReadonlyCollection(nativeList);
  
        return new NativeSpline(readonlyKnots, closed, transformMatrix, allocator);
    }

    public static BlobAssetReference<NativeSplineBlob> CreateNativeSplineBlobAssetRef
        (
        NativeSpline nativeSpline,
        bool isClosed,
        float4x4 transformMatrix
        )
    {
        // Riping values
        var knots = nativeSpline.Knots;

        // Constructing blob
        using var nativeSplineBuilder = new BlobBuilder(Allocator.Temp);
        ref var nativeSplineRoot = ref nativeSplineBuilder.ConstructRoot<NativeSplineBlob>();

        var knotsBuilder = nativeSplineBuilder.Allocate(ref nativeSplineRoot.knots, knots.Length);
        for(int i = 0; i < knots.Length; i++)
        {
            knotsBuilder[i] = knots[i];
        }
  
        nativeSplineRoot.closed = isClosed;
        nativeSplineRoot.transformMatrix = transformMatrix;

        return nativeSplineBuilder
                .CreateBlobAssetReference<NativeSplineBlob>(Allocator.Persistent);
    }
}


public readonly struct KnotsReadonlyCollection: IReadOnlyList<BezierKnot>
{
    private readonly NativeList<BezierKnot> _knots;
    
    public KnotsReadonlyCollection(NativeList<BezierKnot> knots)
    {
        _knots = knots;
    }
    
    public IEnumerator<BezierKnot> GetEnumerator()
    {
        for (int i = 0; i < _knots.Length; i++)
        {
            yield return _knots[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public BezierKnot this[int index] => _knots[index];
    public int Count => _knots.Length;
}


