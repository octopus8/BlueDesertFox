using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for the Player Follow Object entity. Bakes a tag used by
/// <see cref="PlayerFollowObjectSyncSystem"/> to publish pose data to the main scene.
/// </summary>
public class PlayerFollowObjectAuthoring : MonoBehaviour
{
    public class Baker : Baker<PlayerFollowObjectAuthoring>
    {
        public override void Bake(PlayerFollowObjectAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerFollowObjectTag>(entity);
        }
    }
}

/// <summary>Tag component that identifies the Player Follow Object entity in the ECS world.</summary>
public struct PlayerFollowObjectTag : IComponentData
{
}
