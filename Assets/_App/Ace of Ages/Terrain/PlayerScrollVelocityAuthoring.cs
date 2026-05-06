using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for player-based scroll velocity with world origin tracking rotation.
/// Scrolls terrain in the direction the player is facing, with rotation based on world origin orientation.
/// Supports vertical movement based on player pitch angle.
/// Only one velocity provider (PlayerScrollVelocityAuthoring or ConstantScrollVelocityAuthoring) should be in the scene.
/// </summary>
public class PlayerScrollVelocityAuthoring : MonoBehaviour
{
    public enum WorldOriginSearchMode
    {
        FindByName,
        FindByTag,
        FindMainCamera
    }
    
    [Header("Scroll Settings")]
    [Tooltip("Scroll speed in units per second (scrolls in player's forward direction)")]
    public float speed = 50f;
    
    [Tooltip("Rotation speed multiplier for world origin tracking (higher = faster rotation toward world origin direction)")]
    public float rotationSpeed = 2.0f;
    
    [Header("Vertical Movement")]
    [Tooltip("Vertical movement speed in units per second at maximum pitch (90°). Speed scales proportionally with pitch angle. Looking up moves world origin upward, looking down moves it downward.")]
    public float verticalSpeed = 10f;
    
    [Tooltip("Minimum Y position for the world origin (prevents moving too far down)")]
    public float minVerticalPosition = -100f;
    
    [Tooltip("Maximum Y position for the world origin (prevents moving too far up)")]
    public float maxVerticalPosition = 100f;
    
    [Header("World Origin Tracking")]
    [Tooltip("How to find the world origin GameObject at runtime")]
    public WorldOriginSearchMode worldOriginSearchMode = WorldOriginSearchMode.FindMainCamera;
    
    [Tooltip("GameObject name to search for (only used if mode is FindByName)")]
    public string worldOriginName = "Main Camera";
    
    [Tooltip("GameObject tag to search for (only used if mode is FindByTag)")]
    public string worldOriginTag = "MainCamera";

    public class Baker : Baker<PlayerScrollVelocityAuthoring>
    {
        public override void Bake(PlayerScrollVelocityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new PlayerTerrainScrollVelocityConfig
            {
                speed = authoring.speed,
                rotationSpeed = authoring.rotationSpeed,
                verticalSpeed = authoring.verticalSpeed,
                minVerticalPosition = authoring.minVerticalPosition,
                maxVerticalPosition = authoring.maxVerticalPosition
            });
            
            // Determine world origin search mode and parameters
            WorldOriginTrackingSearch.Mode searchMode;
            string searchString = "";
            
            switch (authoring.worldOriginSearchMode)
            {
                case WorldOriginSearchMode.FindByName:
                    searchMode = WorldOriginTrackingSearch.Mode.FindByName;
                    searchString = authoring.worldOriginName;
                    break;
                case WorldOriginSearchMode.FindByTag:
                    searchMode = WorldOriginTrackingSearch.Mode.FindByTag;
                    searchString = authoring.worldOriginTag;
                    break;
                case WorldOriginSearchMode.FindMainCamera:
                default:
                    searchMode = WorldOriginTrackingSearch.Mode.FindMainCamera;
                    break;
            }
            
            // Add world origin search component - will be used by WorldOriginTrackingInitSystem at runtime
            AddComponent(entity, new WorldOriginTrackingSearch
            {
                mode = searchMode,
                searchString = searchString,
                initialized = false
            });
            
            // Add empty WorldOriginTransformReference - will be populated at runtime
            AddComponentObject(entity, new WorldOriginTransformReference
            {
                worldOriginTransform = null
            });
        }
    }
}

