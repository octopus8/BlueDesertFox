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
    /// <summary>
    /// Registers required singletons (<see cref="PlayerTransformReference"/>,
    /// <see cref="CameraDataSingleton"/>, and <see cref="TerrainTileConfig"/>) so the system
    /// waits until the player is tracked.
    /// </summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlayerTransformReference>();
        state.RequireForUpdate<TerrainTileConfig>();
        state.RequireForUpdate<CameraDataSingleton>();
    }
    
    /// <summary>
    /// Reads the cached player position from <see cref="CameraDataSingleton"/> (written end of
    /// previous frame) and schedules the Burst-compiled <see cref="FormationMovementJob"/> in
    /// parallel to update all formation movement states for this frame.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float3 playerPosition = SystemAPI.GetSingleton<CameraDataSingleton>().position;
        var config = SystemAPI.GetSingleton<TerrainTileConfig>();
        float viewDistance = config.viewDistance;
        float deltaTime = SystemAPI.Time.DeltaTime;
        
        // Get scroll velocity from terrain scrolling system
        float3 scrollVelocity = float3.zero;
        if (SystemAPI.TryGetSingleton<TerrainScrollVelocity>(out var scrollVel))
        {
            scrollVelocity = scrollVel.direction * scrollVel.speed;
        }
        
        // Schedule Burst-compiled job for movement calculations
        var job = new FormationMovementJob
        {
            playerPosition = playerPosition,
            viewDistance = viewDistance,
            deltaTime = deltaTime,
            scrollVelocity = scrollVelocity
        };
        
        job.ScheduleParallel();
    }
}

/// <summary>
/// Burst-compiled parallel job that advances each formation enemy through its movement lifecycle
/// by dispatching to phase-specific handlers: approach, follow, leave, and out-of-bounds.
/// Terrain scroll velocity is factored into approach and exit speeds so world-relative movement
/// remains consistent regardless of scroll rate.
/// </summary>
[BurstCompile]
public partial struct FormationMovementJob : IJobEntity
{
    /// <summary>Current world-space position of the player, used for despawn distance checks.</summary>
    public float3 playerPosition;
    /// <summary>Camera view distance from <see cref="TerrainTileConfig"/>; entities beyond this are marked out-of-bounds.</summary>
    public float viewDistance;
    /// <summary>Elapsed time in seconds since the last frame.</summary>
    public float deltaTime;
    /// <summary>Current terrain scroll velocity vector used to offset movement speeds.</summary>
    public float3 scrollVelocity;
    
    /// <summary>
    /// Dispatches the entity to the correct phase handler based on <see cref="FormationMovementState.phase"/>.
    /// </summary>
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
    
    /// <summary>
    /// Advances the entity toward the spline entry point along <see cref="FormationMovementState.approachDirection"/>,
    /// compensating for terrain scroll velocity, and transitions to <see cref="MovementPhase.FollowingSpline"/>
    /// when the entity reaches within 1 unit of the entry point along the approach axis.
    /// </summary>
    private void HandleApproachPhase(
        ref LocalTransform localTransform,
        ref FormationMovementState movementState,
        ref PhysicsVelocity physicsVelocity)
    {
        // All enemies move in the same direction (spline tangent) to maintain formation
        float3 direction = movementState.approachDirection;
        
        // Calculate distance to entry point along the approach direction (not direct distance)
        // This ensures all formation members transition together based on forward progress
        float3 toEntry = movementState.splineEntryPoint - localTransform.Position;
        float distanceAlongApproach = math.dot(toEntry, direction);

        // Auto-detect transition: check if we've reached or passed the entry point along the approach axis
        // Use small threshold to handle formation members at different lateral positions
        if (distanceAlongApproach <= 1f)
        {
            movementState.phase = MovementPhase.FollowingSpline;
            physicsVelocity.Linear = float3.zero;
            physicsVelocity.Angular = float3.zero;
            return;
        }
        
        // All enemies move in the same direction at the same speed (maintains formation)
        float baseApproachSpeed = movementState.formationSpeed; // Use configured formation speed
        
        // Project scroll velocity onto movement direction for speed offset
        // Negate to convert world velocity to player's relative velocity
        float scrollSpeedOffset = -math.dot(scrollVelocity, direction);
        float effectiveApproachSpeed = baseApproachSpeed + scrollSpeedOffset;
        
        // Lerp velocity toward target direction for smooth movement
        float3 targetVelocity = direction * effectiveApproachSpeed;
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
    
    /// <summary>
    /// Delegates movement to <see cref="SplineFollowerSystem"/> and monitors the spline's
    /// <see cref="SplineFollower.distanceRatio"/>; when it reaches 0.99 on a non-closed spline,
    /// transitions to <see cref="MovementPhase.LeavingSpline"/> and captures the exit tangent.
    /// </summary>
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
    
    /// <summary>
    /// Drives the entity in the captured <see cref="FormationMovementState.exitDirection"/> at formation
    /// speed with terrain scroll compensation; transitions to <see cref="MovementPhase.OutOfBounds"/> once
    /// the entity has travelled far enough from the player.
    /// </summary>
    private void HandleLeavingPhase(
        ref LocalTransform localTransform,
        ref FormationMovementState movementState,
        ref PhysicsVelocity physicsVelocity)
    {
        // Continue moving in the exit direction at constant speed
        float baseExitSpeed = movementState.formationSpeed; // Use configured formation speed
        
        // Project scroll velocity onto exit direction for speed offset
        // Negate to convert world velocity to player's relative velocity
        float scrollSpeedOffset = -math.dot(scrollVelocity, movementState.exitDirection);
        float effectiveExitSpeed = baseExitSpeed + scrollSpeedOffset;
        
        physicsVelocity.Linear = movementState.exitDirection * effectiveExitSpeed;
        physicsVelocity.Angular = float3.zero;
        
        // Check distance from player
        float distanceFromPlayer = math.distance(localTransform.Position, playerPosition);
        
        // Mark as out of bounds if beyond despawn distance (same as spawn distance)
        if (distanceFromPlayer > movementState.despawnDistance)
        {
            movementState.phase = MovementPhase.OutOfBounds;
            physicsVelocity.Linear = float3.zero;
            physicsVelocity.Angular = float3.zero;
        }
    }
}


