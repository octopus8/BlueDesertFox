using Unity.Entities;
using UnityEngine;

/// <summary>
/// Authoring component for player-based scroll velocity with head-tracking rotation.
/// Scrolls terrain in the direction the player is facing, with rotation based on headset orientation.
/// Only one velocity provider (PlayerScrollVelocityAuthoring or ConstantScrollVelocityAuthoring) should be in the scene.
/// </summary>
public class PlayerScrollVelocityAuthoring : MonoBehaviour
{
    public enum HeadsetSearchMode
    {
        FindByName,
        FindByTag,
        FindMainCamera
    }
    
    [Header("Scroll Settings")]
    [Tooltip("Scroll speed in units per second (scrolls in player's forward direction)")]
    public float speed = 50f;
    
    [Tooltip("Rotation speed multiplier for head-tracking (higher = faster rotation toward headset direction)")]
    public float rotationSpeed = 2.0f;
    
    [Header("Headset Tracking")]
    [Tooltip("How to find the headset GameObject at runtime")]
    public HeadsetSearchMode headsetSearchMode = HeadsetSearchMode.FindMainCamera;
    
    [Tooltip("GameObject name to search for (only used if mode is FindByName)")]
    public string headsetName = "Main Camera";
    
    [Tooltip("GameObject tag to search for (only used if mode is FindByTag)")]
    public string headsetTag = "MainCamera";

    public class Baker : Baker<PlayerScrollVelocityAuthoring>
    {
        public override void Bake(PlayerScrollVelocityAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new PlayerTerrainScrollVelocityConfig
            {
                speed = authoring.speed,
                rotationSpeed = authoring.rotationSpeed
            });
            
            // Determine headset search mode and parameters
            HeadsetTrackingSearch.Mode searchMode;
            string searchString = "";
            
            switch (authoring.headsetSearchMode)
            {
                case HeadsetSearchMode.FindByName:
                    searchMode = HeadsetTrackingSearch.Mode.FindByName;
                    searchString = authoring.headsetName;
                    break;
                case HeadsetSearchMode.FindByTag:
                    searchMode = HeadsetTrackingSearch.Mode.FindByTag;
                    searchString = authoring.headsetTag;
                    break;
                case HeadsetSearchMode.FindMainCamera:
                default:
                    searchMode = HeadsetTrackingSearch.Mode.FindMainCamera;
                    break;
            }
            
            // Add headset search component - will be used by HeadsetTrackingInitSystem at runtime
            AddComponent(entity, new HeadsetTrackingSearch
            {
                mode = searchMode,
                searchString = searchString,
                initialized = false
            });
            
            // Add empty HeadsetTransformReference - will be populated at runtime
            AddComponentObject(entity, new HeadsetTransformReference
            {
                headsetTransform = null
            });
        }
    }
}


