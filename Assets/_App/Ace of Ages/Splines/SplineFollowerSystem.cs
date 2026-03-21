using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

partial struct SplineFollowerSystem : ISystem
{
    private const bool useJobs = true;
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (useJobs)
        {
            // Calculate scroll velocity from terrain scrolling system
            float3 scrollVelocity = float3.zero;
            if (SystemAPI.TryGetSingleton<ScrollConfig>(out var scrollConfig) && 
                SystemAPI.TryGetSingleton<ScrollOffset>(out var scrollOffset))
            {
                if (scrollConfig.enabled && scrollConfig.scrollSpeed > 0f)
                {
                    // Calculate scroll direction from accumulated offset
                    float3 scrollDirection = math.normalizesafe(scrollOffset.accumulatedOffset);
                    scrollVelocity = scrollDirection * scrollConfig.scrollSpeed;
                }
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


[BurstCompile]
public partial struct SplineFollowerJob : IJobEntity
{
    public float deltaTime;
    public float3 scrollVelocity;
    [ReadOnly] public ComponentLookup<FormationPosition> formationPositionLookup;
    [ReadOnly] public ComponentLookup<FormationMovementState> movementStateLookup;
    
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
        
        if (hasFormation)
        {
            FormationPosition formationPos = formationPositionLookup[entity];
            
            // Apply forward offset from formation position
            adjustedDistanceRatio = splineFollower.distanceRatio + (formationPos.forwardOffset / spline.totalLength);
            
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

