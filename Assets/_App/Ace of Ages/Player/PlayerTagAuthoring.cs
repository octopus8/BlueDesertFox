using Unity.Entities;
using UnityEngine;

/// <summary>
/// DEPRECATED: This component is no longer needed for the infinite terrain system.
/// The terrain now tracks GameObjects directly via TerrainConfigAuthoring.playerToTrack.
/// See GAMEOBJECT_TRACKING_GUIDE.md for details.
/// 
/// Legacy component that added a PlayerTag to ECS entities.
/// </summary>
[System.Obsolete("PlayerTag is no longer used by the terrain system. Use TerrainConfigAuthoring.playerToTrack instead.")]
public class PlayerTagAuthoring : MonoBehaviour
{
    public class Baker : Baker<PlayerTagAuthoring>
    {
        public override void Bake(PlayerTagAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent<PlayerTag>(entity);
        }
    }
}


public struct PlayerTag : IComponentData
{
}