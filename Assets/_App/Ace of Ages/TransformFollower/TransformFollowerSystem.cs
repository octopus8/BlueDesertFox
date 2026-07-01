using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// System that updates entities to follow external Transforms.
/// </summary>
/// <remarks>
/// This system must run on the main thread because it accesses managed Transform references.
/// This is the fundamental limitation - we cannot use Burst compilation or Jobs for this system
/// because managed references (GameObjects/Transforms) cannot be accessed from Jobs or Burst-compiled code.
/// 
/// For better performance with many followers, consider:
/// 1. Using a singleton managed component to batch updates
/// 2. Implementing a hybrid approach where you copy Transform data to a native array once per frame
/// 3. Limiting the number of entities that follow external Transforms
/// </remarks>
[DisableAutoCreation]
[RequireMatchingQueriesForUpdate]
public partial class TransformFollowerSystem : SystemBase
{
    /// <summary>
    /// On the main thread, iterates all entities with a <see cref="TransformReference"/> and updates
    /// their <see cref="LocalTransform"/> to match the referenced target <see cref="Transform"/>, applying
    /// the configured position offset and optional rotation following with smooth interpolation.
    /// Cannot use Burst or jobs due to managed <see cref="Transform"/> access.
    /// </summary>
    protected override void OnUpdate()
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        // This must run on the main thread because we're accessing managed Transform references
        Entities
            .WithAll<TransformReference>()
            .WithoutBurst() // Cannot use Burst because we're accessing managed references
            .ForEach((
                Entity entity,
                ref LocalTransform localTransform,
                in TransformFollowerSettings settings,
                in TransformReference transformRef) =>
            {
                // Check if the target Transform is valid
                if (transformRef.target == null)
                {
                    return;
                }
                
                // Calculate target position with offset
                float3 targetPosition = (float3)transformRef.target.position + settings.offset;
                
                // Apply smoothing if needed
                if (settings.smoothTime > 0)
                {
                    float smoothFactor = math.saturate(deltaTime / settings.smoothTime);
                    localTransform.Position = math.lerp(localTransform.Position, targetPosition, smoothFactor);
                }
                else
                {
                    localTransform.Position = targetPosition;
                }
                
                // Follow rotation if enabled
                if (settings.followRotation)
                {
                    quaternion targetRotation = transformRef.target.rotation;
                    
                    if (settings.smoothTime > 0)
                    {
                        float smoothFactor = math.saturate(deltaTime / settings.smoothTime);
                        localTransform.Rotation = math.slerp(localTransform.Rotation, targetRotation, smoothFactor);
                    }
                    else
                    {
                        localTransform.Rotation = targetRotation;
                    }
                }
            })
            .Run(); // Must use .Run() instead of .Schedule() because we're accessing managed references
    }
}

