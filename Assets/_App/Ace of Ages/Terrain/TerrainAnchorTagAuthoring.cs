using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for terrain anchors.
/// Attach this to any GameObject in a SubScene that should move with the terrain scroll.
/// The GameObject's initial position will be stored as the base position.
/// </summary>
public class TerrainAnchorTagAuthoring : MonoBehaviour
{
    [Tooltip("The base position for this anchor. If not set, uses the GameObject's position at bake time.")]
    public bool useCustomBasePosition = false;
    
    [Tooltip("Custom base position in world space (only used if useCustomBasePosition is true)")]
    public Vector3 customBasePosition = Vector3.zero;

    /// <summary>Bakes the anchor's base position (custom or from GameObject's world transform) into a <see cref="TerrainAnchorTag"/> component.</summary>
    private class Baker : Baker<TerrainAnchorTagAuthoring>
    {
        /// <inheritdoc/>
        public override void Bake(TerrainAnchorTagAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            
            // Store the base position - either custom or from GameObject transform
            float3 basePos = authoring.useCustomBasePosition 
                ? (float3)authoring.customBasePosition 
                : (float3)authoring.transform.position;
            
            AddComponent(entity, new TerrainAnchorTag
            {
                basePosition = basePos
            });
        }
    }
    
    /// <summary>Draws a cyan wire sphere and world-axis lines at the anchor's base position in the Scene view when this component is selected.</summary>
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 pos = useCustomBasePosition ? customBasePosition : transform.position;
        
        // Draw a sphere at base position
        Gizmos.DrawWireSphere(pos, 0.5f);
        
        // Draw coordinate axes
        Gizmos.color = Color.red;
        Gizmos.DrawLine(pos, pos + Vector3.right * 1f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(pos, pos + Vector3.up * 1f);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(pos, pos + Vector3.forward * 1f);
    }
}

