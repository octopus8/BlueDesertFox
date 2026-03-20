using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// System that manages the multi-phase movement lifecycle of enemy formations.
/// Handles: Approaching spline → Following spline → Leaving spline → Cleanup.
/// Runs before SplineFollowerSystem to update movement states first.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SplineFollowerSystem))]
partial struct FormationMovementSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        // Require player tracking for distance calculations
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<TerrainTileConfig>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get singletons (managed component requires main thread access)
        var playerRef = SystemAPI.ManagedAPI.GetSingleton<PlayerTransformReference>();
        if (playerRef == null || playerRef.playerTransform == null)
            return;
        
        float3 playerPosition = playerRef.playerTransform.position;
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        float viewDistance = config.viewDistance;
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        // Schedule Burst-compiled job for movement calculations
        var job = new FormationMovementJob
        {
            playerPosition = playerPosition,
            viewDistance = viewDistance,
            deltaTime = deltaTime
        };
        
        job.ScheduleParallel();
    }
}

[BurstCompile]
public partial struct FormationMovementJob : IJobEntity
{
    public float3 playerPosition;
    public float viewDistance;
    public float deltaTime;
    
    public void Execute(
        ref LocalTransform localTransform,
        ref FormationMovementState movementState,
        ref SplineFollower splineFollower,
        ref PhysicsVelocity physicsVelocity,
        in SplineDataComponent splineData)
    {
        switch (movementState.phase)
        {
            case MovementPhase.ApproachingSpline:
                HandleApproachPhase(ref localTransform, ref movementState, ref physicsVelocity);
                break;
                
            case MovementPhase.FollowingSpline:
                HandleFollowingPhase(ref movementState, ref splineFollower, in splineData, ref localTransform);
                break;
                
            case MovementPhase.LeavingSpline:
                HandleLeavingPhase(ref localTransform, ref movementState, ref physicsVelocity);
                break;
                
            case MovementPhase.OutOfBounds:
                // Already marked for cleanup, do nothing
                break;
        }
    }
    
    private void HandleApproachPhase(
        ref LocalTransform localTransform,
        ref FormationMovementState movementState,
        ref PhysicsVelocity physicsVelocity)
    {
        // Calculate direction to spline entry point
        float3 toEntry = movementState.splineEntryPoint - localTransform.Position;
        float distanceToEntry = math.length(toEntry);
        
        // Check if close enough to transition to following
        if (distanceToEntry <= movementState.approachThreshold)
        {
            movementState.phase = MovementPhase.FollowingSpline;
            physicsVelocity.Linear = float3.zero;
            physicsVelocity.Angular = float3.zero;
            return;
        }
        
        // Move toward entry point using physics velocity
        float3 direction = math.normalize(toEntry);
        float approachSpeed = 10f; // Configurable approach speed
        
        // Lerp velocity toward target direction for smooth movement
        float3 targetVelocity = direction * approachSpeed;
        physicsVelocity.Linear = math.lerp(physicsVelocity.Linear, targetVelocity, deltaTime * 5f);
        physicsVelocity.Angular = float3.zero;
        
        // Update rotation to face movement direction
        if (math.lengthsq(physicsVelocity.Linear) > 0.001f)
        {
            float3 forward = math.normalize(physicsVelocity.Linear);
            quaternion targetRotation = quaternion.LookRotationSafe(forward, new float3(0, 1, 0));
            localTransform.Rotation = math.slerp(localTransform.Rotation, targetRotation, deltaTime * 5f);
        }
    }
    
    private void HandleFollowingPhase(
        ref FormationMovementState movementState,
        ref SplineFollower splineFollower,
        in SplineDataComponent splineData,
        ref LocalTransform localTransform)
    {
        // SplineFollowerSystem will handle the actual movement
        // Check if we've reached the end of the spline
        if (!splineData.splineData.IsCreated)
            return;
        
        ref var spline = ref splineData.splineData.Value;
        
        // For non-closed splines, check if we've reached the end
        if (!spline.isClosed && splineFollower.distanceRatio >= 0.99f)
        {
            // Transition to leaving phase
            movementState.phase = MovementPhase.LeavingSpline;
            
            // Capture current tangent direction as exit direction
            SplineSample exitSample = spline.Evaluate(1.0f);
            movementState.exitDirection = math.normalize(exitSample.tangent);
        }
    }
    
    private void HandleLeavingPhase(
        ref LocalTransform localTransform,
        ref FormationMovementState movementState,
        ref PhysicsVelocity physicsVelocity)
    {
        // Continue moving in the exit direction at constant speed
        float exitSpeed = 10f; // Speed when leaving spline
        physicsVelocity.Linear = movementState.exitDirection * exitSpeed;
        physicsVelocity.Angular = float3.zero;
        
        // Check distance from player
        float distanceFromPlayer = math.distance(localTransform.Position, playerPosition);
        
        // Mark as out of bounds if beyond view distance * 1.2 (20% buffer)
        if (distanceFromPlayer > viewDistance * 1.2f)
        {
            movementState.phase = MovementPhase.OutOfBounds;
            physicsVelocity.Linear = float3.zero;
            physicsVelocity.Angular = float3.zero;
        }
    }
}


