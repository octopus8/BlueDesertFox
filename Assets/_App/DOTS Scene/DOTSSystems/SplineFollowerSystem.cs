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
            SplineFollowerJob splineFollowerJob = new SplineFollowerJob
            {
                deltaTime = Time.deltaTime,
            };
            splineFollowerJob.ScheduleParallel();
        }
        
        // Not using jobs for better debugging experience, as the system is not performance critical and we want to be able to easily inspect the values of the components in the editor.
        else
        {
            foreach (var (
                         localTransform,
                         unitMover,
                         physicsVelocity,
                         splineData
                         )
                     in SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRW<UnitMover>,
                         RefRW<PhysicsVelocity>,
                         RefRO<SplineDataComponent>
                     >())
            {
                // Check if spline data is valid
                if (!splineData.ValueRO.splineData.IsCreated)
                {
                    continue;
                }
                
                ref var spline = ref splineData.ValueRO.splineData.Value;
                
                // Calculate the new distance ratio based on speed and time
                unitMover.ValueRW.distanceRatio += (unitMover.ValueRO.moveSpeed * Time.deltaTime) / spline.totalLength;
                
                // Wrap around the spline if it's a closed loop
                if (spline.isClosed)
                {
                    unitMover.ValueRW.distanceRatio = unitMover.ValueRW.distanceRatio - math.floor(unitMover.ValueRW.distanceRatio);
                }
                else
                {
                    unitMover.ValueRW.distanceRatio = math.clamp(unitMover.ValueRW.distanceRatio, 0f, 1f);
                }
                
                // Evaluate the spline at the current distance ratio
                SplineSample sample = spline.Evaluate(unitMover.ValueRO.distanceRatio);
                
                // Smoothly interpolate position to the spline position
                float positionLerpSpeed = 10f; // Higher values = faster position interpolation
                localTransform.ValueRW.Position = math.lerp(localTransform.ValueRO.Position, sample.position, Time.deltaTime * positionLerpSpeed);
                
                // Calculate target rotation from the tangent direction
                quaternion targetRotation = quaternion.LookRotation(sample.tangent, sample.upVector);
                
                // Smoothly interpolate rotation using slerp with a rotation speed factor
                float rotationSpeed = 5f; // Higher values = faster rotation
                localTransform.ValueRW.Rotation = math.slerp(localTransform.ValueRO.Rotation, targetRotation, Time.deltaTime * rotationSpeed);
                
                // Keep velocities at zero since we're directly controlling position
                physicsVelocity.ValueRW.Linear = float3.zero;
                physicsVelocity.ValueRW.Angular = float3.zero;
            }
        }
        
        
    }
}


[BurstCompile]
public partial struct SplineFollowerJob : IJobEntity
{
    public float deltaTime;
    
    public void Execute(
        ref LocalTransform localTransform, 
        ref UnitMover unitMover, 
        ref PhysicsVelocity physicsVelocity,
        in SplineDataComponent splineData)
    {
        // Check if spline data is valid
        if (!splineData.splineData.IsCreated)
        {
            return;
        }
        
        ref var spline = ref splineData.splineData.Value;
        
        // Calculate the new distance ratio based on speed and time
        unitMover.distanceRatio += (unitMover.moveSpeed * deltaTime) / spline.totalLength;
        
        // Wrap around the spline if it's a closed loop
        if (spline.isClosed)
        {
            unitMover.distanceRatio = unitMover.distanceRatio - math.floor(unitMover.distanceRatio);
        }
        else
        {
            unitMover.distanceRatio = math.clamp(unitMover.distanceRatio, 0f, 1f);
        }
        
        // Evaluate the spline at the current distance ratio
        SplineSample sample = spline.Evaluate(unitMover.distanceRatio);
        
        // Smoothly interpolate position to the spline position
        float positionLerpSpeed = 10f; // Higher values = faster position interpolation
        localTransform.Position = math.lerp(localTransform.Position, sample.position, deltaTime * positionLerpSpeed);
        
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

