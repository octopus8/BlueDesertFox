using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring component for terrain anchors.
/// Attach this to any GameObject in a SubScene that should move with the terrain scroll.
/// The GameObject's initial position will be stored as the base position.
/// Colliders that must stay solid while scrolling (e.g. Rideable quarterpipes) also need a
/// kinematic <see cref="Rigidbody"/> so Unity Physics tracks the moving transform. A MeshCollider
/// alone bakes as a static body and stays behind the scrolled mesh — probes then miss and riders tunnel.
/// Ground contact runs before <see cref="TerrainAnchorSystem"/> so casts use the pre-scroll pose.
/// MeshCollider meshes must have Read/Write enabled — Unity Physics cannot bake a non-readable mesh
/// into <see cref="Unity.Physics.PhysicsCollider"/>, so the render mesh scrolls but casts pass through.
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

            var meshCollider = GetComponent<UnityEngine.MeshCollider>();
            if (meshCollider != null)
            {
                DependsOn(meshCollider);
                var mesh = meshCollider.sharedMesh;
                if (mesh == null)
                {
                    Debug.LogError(
                        $"[TerrainAnchor] '{authoring.name}' has a MeshCollider with no mesh. " +
                        "Rideable casts will pass through.",
                        authoring);
                }
                else if (!mesh.isReadable)
                {
                    Debug.LogError(
                        $"[TerrainAnchor] '{authoring.name}' MeshCollider mesh '{mesh.name}' is not " +
                        "Read/Write enabled. Unity Physics cannot bake it — enable Read/Write on the " +
                        "model import settings and rebake this SubScene, or riders will tunnel through.",
                        authoring);
                }
            }
            
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

