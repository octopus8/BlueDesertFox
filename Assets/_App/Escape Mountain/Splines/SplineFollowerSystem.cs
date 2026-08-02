using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;


/// <summary>
/// Moves entities with a <see cref="SplineFollower"/> component along their associated spline each frame.
/// Only processes entities in the <see cref="MovementPhase.FollowingSpline"/> phase (or entities
/// without a <see cref="FormationMovementState"/> for backwards compatibility).
/// Compensates for terrain scroll velocity so that spline-following enemies maintain correct
/// world-relative speeds. Supports bowling-pin formation offsets via <see cref="FormationPosition"/>.
/// <para>Disabled by default (<c>[DisableAutoCreation]</c>); enable for spline-following gameplay.</para>
/// </summary>
partial struct SplineFollowerSystem : ISystem
{
    private const bool useJobs = true;
    
    /// <summary>
    /// Schedules the <see cref="SplineFollowerJob"/> to advance all eligible spline-following
    /// entities in parallel, applying formation offsets and terrain scroll compensation.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var paused) && paused.Value)
            return;

        if (useJobs)
        {
            // Get scroll velocity from terrain scrolling system
            float3 scrollVelocity = float3.zero;
            if (SystemAPI.TryGetSingleton<TerrainScrollVelocity>(out var scrollVel))
            {
                scrollVelocity = scrollVel.WorldVelocity;
            }
            
            SplineFollowerJob splineFollowerJob = new SplineFollowerJob
            {
                deltaTime = Time.deltaTime,
                scrollVelocity = scrollVelocity,
                formationPositionLookup = SystemAPI.GetComponentLookup<FormationPosition>(true),
                movementStateLookup = SystemAPI.GetComponentLookup<FormationMovementState>(true),
            };
            splineFollowerJob.ScheduleParallel();
        }
        
        /* Not using jobs for better debugging experience, as the system is not performance critical and we want to be able to easily inspect the values of the components in the editor.
        // NOTE: This code is currently disabled because useJobs = true
        // To use this code for debugging, set useJobs = false at the top of the file
        else
        {
            // ...existing code...
        }
        */
        
        
    }
}


/// <summary>
/// Burst-compiled parallel job that advances each spline-following entity along its pre-sampled
/// spline, applies bowling-pin formation offsets, compensates for terrain scroll velocity, and
/// smoothly interpolates the entity's position and rotation toward the computed spline target.
/// </summary>
[BurstCompile]
public partial struct SplineFollowerJob : IJobEntity
{
    /// <summary>Elapsed time in seconds since the last frame.</summary>
    public float deltaTime;
    /// <summary>
    /// Current terrain scroll velocity vector. Projected onto the spline tangent to offset the
    /// entity's effective speed so that world-relative closing speed remains constant.
    /// </summary>
    public float3 scrollVelocity;
    /// <summary>Read-only lookup for <see cref="FormationPosition"/> to apply bowling-pin offsets.</summary>
    [ReadOnly] public ComponentLookup<FormationPosition> formationPositionLookup;
    /// <summary>Read-only lookup for <see cref="FormationMovementState"/> to filter by movement phase.</summary>
    [ReadOnly] public ComponentLookup<FormationMovementState> movementStateLookup;
    
    /// <summary>
    /// Advances the entity along the spline, applies formation lateral/forward offsets, compensates
    /// for terrain scroll velocity, and smoothly interpolates the entity's transform each frame.
    /// Skips entities not in the <see cref="MovementPhase.FollowingSpline"/> phase.
    /// </summary>
    public void Execute(
        Entity entity,
        ref LocalTransform localTransform, 
        ref SplineFollower splineFollower, 
        ref PhysicsVelocity physicsVelocity,
        in SplineDataComponent splineData)
    {
        // Only process entities in FollowingSpline phase (or entities without movement state for backwards compatibility)
        if (movementStateLookup.HasComponent(entity))
        {
            var movementState = movementStateLookup[entity];
            if (movementState.phase != MovementPhase.FollowingSpline)
            {
                // Skip entities not currently following the spline
                return;
            }
        }
        
        // Check if spline data is valid
        if (!splineData.splineData.IsCreated)
        {
            return;
        }
        
        ref var spline = ref splineData.splineData.Value;
        
        // Get enemy's current movement direction from spline tangent
        SplineSample currentSample = spline.Evaluate(splineFollower.distanceRatio);
        float3 enemyDirection = math.normalize(currentSample.tangent);
        
        // Project scroll velocity onto enemy's movement direction to get speed offset
        // Scroll velocity represents world movement (opposite of player movement)
        // Negate to convert to player's relative velocity for correct closing speeds
        float scrollSpeedOffset = -math.dot(scrollVelocity, enemyDirection);
        
        // Apply scroll velocity offset to enemy movement speed (allow negative speeds)
        float effectiveSpeed = splineFollower.moveSpeed + scrollSpeedOffset;
        
        // Calculate the new distance ratio based on effective speed and time
        splineFollower.distanceRatio += (effectiveSpeed * deltaTime) / spline.totalLength;
        
        // Wrap around the spline if it's a closed loop
        if (spline.isClosed)
        {
            splineFollower.distanceRatio = splineFollower.distanceRatio - math.floor(splineFollower.distanceRatio);
        }
        else
        {
            splineFollower.distanceRatio = math.clamp(splineFollower.distanceRatio, 0f, 1f);
        }
        
        // Check if this entity has a formation position
        bool hasFormation = formationPositionLookup.HasComponent(entity);
        float adjustedDistanceRatio = splineFollower.distanceRatio;
        float3 lateralOffset = float3.zero;
        float forwardOffsetDistance = 0f; // Track offset distance for out-of-bounds calculation
        
        if (hasFormation)
        {
            FormationPosition formationPos = formationPositionLookup[entity];
            
            // Apply forward offset from formation position
            adjustedDistanceRatio = splineFollower.distanceRatio + (formationPos.forwardOffset / spline.totalLength);
            forwardOffsetDistance = formationPos.forwardOffset;
            
            // Wrap/clamp the adjusted ratio
            if (spline.isClosed)
            {
                adjustedDistanceRatio = adjustedDistanceRatio - math.floor(adjustedDistanceRatio);
            }
            else
            {
                adjustedDistanceRatio = math.clamp(adjustedDistanceRatio, 0f, 1f);
            }
            
            lateralOffset = formationPos.lateralOffset;
        }
        
        // Evaluate the spline at the (possibly adjusted) distance ratio
        SplineSample sample = spline.Evaluate(adjustedDistanceRatio);
        
        // Calculate target position
        float3 targetPosition = sample.position;
        
        // For non-closed splines, handle formation offsets that extend beyond spline bounds
        // by extending along the spline tangent instead of clamping to endpoints
        if (hasFormation && !spline.isClosed && forwardOffsetDistance != 0f)
        {
            float rawAdjustedRatio = splineFollower.distanceRatio + (forwardOffsetDistance / spline.totalLength);
            
            // Handle enemies behind the spline start (negative offset)
            if (rawAdjustedRatio < 0f)
            {
                // Enemy is behind the spline start - extend backward along start tangent
                float offsetDistance = -rawAdjustedRatio * spline.totalLength; // Distance behind start (positive value)
                SplineSample startSample = spline.Evaluate(0f);
                float3 backwardDirection = -math.normalize(startSample.tangent); // Reverse tangent for backward
                targetPosition = startSample.position + backwardDirection * offsetDistance;
                
                // Use start sample for lateral offset calculation
                sample = startSample;
            }
            // Handle enemies ahead of the spline end (positive offset beyond 1.0)
            else if (rawAdjustedRatio > 1f)
            {
                // Enemy is ahead of the spline end - extend forward along end tangent
                float offsetDistance = (rawAdjustedRatio - 1f) * spline.totalLength; // Distance ahead of end
                SplineSample endSample = spline.Evaluate(1f);
                float3 forwardDirection = math.normalize(endSample.tangent);
                targetPosition = endSample.position + forwardDirection * offsetDistance;
                
                // Use end sample for lateral offset calculation
                sample = endSample;
            }
        }
        
        // Apply lateral offset if in formation
        if (hasFormation)
        {
            // Calculate the right vector (perpendicular to movement direction)
            float3 rightVector = math.normalize(math.cross(sample.upVector, sample.tangent));
            targetPosition += rightVector * lateralOffset.x;
        }
        
        // Smoothly interpolate position to the target position
        float positionLerpSpeed = 10f; // Higher values = faster position interpolation
        localTransform.Position = math.lerp(localTransform.Position, targetPosition, deltaTime * positionLerpSpeed);
        
        // Calculate target rotation from the tangent direction
        quaternion targetRotation = quaternion.LookRotation(sample.tangent, sample.upVector);
        
        // Smoothly interpolate rotation using slerp with a rotation speed factor
        float rotationSpeed = 5f; // Higher values = faster rotation
        localTransform.Rotation = math.slerp(localTransform.Rotation, targetRotation, deltaTime * rotationSpeed);
        
        // Keep velocities at zero since we're directly controlling position
        physicsVelocity.Linear = float3.zero;
        physicsVelocity.Angular = float3.zero;
    }
}

