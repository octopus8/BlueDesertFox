using Unity.Entities;
using UnityEngine;

/// <summary>
/// Marks a baked collider as a rideable pipe face (quarterpipe / halfpipe inner surface).
/// Ground contact uses this tag so pipe leave/land does not depend on Unity Physics layer mapping.
/// </summary>
public struct PipeFaceTag : IComponentData
{
}

/// <summary>
/// Authoring for <see cref="PipeFaceTag"/>. Put this on the inner Pipe-layer mesh (PipeFace),
/// not the outer shell (PipeBody).
/// </summary>
public class PipeFaceTagAuthoring : MonoBehaviour
{
    private class Baker : Baker<PipeFaceTagAuthoring>
    {
        public override void Bake(PipeFaceTagAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PipeFaceTag>(entity);
        }
    }
}
