using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Splines;

partial struct UnitMoverSystem : ISystem
{
    private const bool useJobs = false;
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (useJobs)
        {
            UnitMoverJob unitMoverJob = new UnitMoverJob
            {
                deltaTime = Time.deltaTime,
            };
            unitMoverJob.ScheduleParallel();
        }
        
        // Not using jobs for better debugging experience, as the system is not performance critical and we want to be able to easily inspect the values of the components in the editor.
        else
        {
            foreach (var (
                         localTransform,
                         unitMover,
                         physicsVelocity
                         )
                     in SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRW<UnitMover>,
                         RefRW<PhysicsVelocity>
                     >())
            {
                // Check if spline is valid
                if (!unitMover.ValueRO.spline.reference.IsCreated)
                {
                    continue;
                }
                
                // Create a NativeSpline from the blob asset
                using var nativeSpline = unitMover.ValueRO.spline.reference.Value.CreateNativeSpline(Allocator.Temp);
                
                // Calculate the spline length
                float splineLength = nativeSpline.GetLength();
                
                // Calculate the new distance ratio based on speed and time
                unitMover.ValueRW.distanceRatio += (unitMover.ValueRO.moveSpeed * Time.deltaTime) / splineLength;
                
                // Wrap around the spline if it's a closed loop
                if (unitMover.ValueRW.distanceRatio > 1f)
                {
                    unitMover.ValueRW.distanceRatio -= 1f; // For looping paths
                }
                
                // Evaluate the spline at the current distance ratio
                float3 position = nativeSpline.EvaluatePosition(unitMover.ValueRO.distanceRatio);
                float3 tangent = nativeSpline.EvaluateTangent(unitMover.ValueRO.distanceRatio);
                float3 upVector = nativeSpline.EvaluateUpVector(unitMover.ValueRO.distanceRatio);
                
                // Calculate target direction
                float3 targetDirection = position - localTransform.ValueRO.Position;
                
                // Set the linear velocity towards the target position
                physicsVelocity.ValueRW.Linear = math.normalize(targetDirection) * unitMover.ValueRO.moveSpeed;
                
                // Normalize the tangent to ensure it's a proper direction vector
                float3 normalizedTangent = math.normalize(tangent);
                
                // Calculate target rotation from the tangent direction
                quaternion targetRotation = quaternion.LookRotation(normalizedTangent, upVector);
                
                // Smoothly interpolate rotation using slerp with a rotation speed factor
                // Adjust the rotation speed (0.1f) to control how fast the entity rotates
                float rotationSpeed = 5f; // Higher values = faster rotation
                localTransform.ValueRW.Rotation = math.slerp(localTransform.ValueRO.Rotation, targetRotation, Time.deltaTime * rotationSpeed);
                
                // Keep angular velocity at zero to prevent unwanted rotation
                physicsVelocity.ValueRW.Angular = float3.zero;
            }
        }
        
        
    }
}


[BurstCompile]
public partial struct UnitMoverJob : IJobEntity
{
    public float deltaTime;
    
    public void Execute(ref LocalTransform localTransform, ref UnitMover unitMover, ref PhysicsVelocity physicsVelocity)
    {
        // Check if spline is valid
        if (!unitMover.spline.reference.IsCreated)
        {
            return;
        }
        
        // Create a NativeSpline from the blob asset
        using var nativeSpline = unitMover.spline.reference.Value.CreateNativeSpline(Allocator.Temp);
        
        // Calculate the spline length
        float splineLength = nativeSpline.GetLength();
        
        // Calculate the new distance ratio based on speed and time
        unitMover.distanceRatio += (unitMover.moveSpeed * deltaTime) / splineLength;
        
        // Wrap around the spline if it's a closed loop
        if (unitMover.distanceRatio > 1f)
        {
            unitMover.distanceRatio -= 1f; // For looping paths
        }
        
        // Evaluate the spline at the current distance ratio
        float3 position = nativeSpline.EvaluatePosition(unitMover.distanceRatio);
        float3 tangent = nativeSpline.EvaluateTangent(unitMover.distanceRatio);
        float3 upVector = nativeSpline.EvaluateUpVector(unitMover.distanceRatio);
        
        // Calculate target direction
        float3 targetDirection = position - localTransform.Position;
        
        // Set the linear velocity towards the target position
        physicsVelocity.Linear = math.normalize(targetDirection) * unitMover.moveSpeed;
        
        // Normalize the tangent to ensure it's a proper direction vector
        float3 normalizedTangent = math.normalize(tangent);
        
        // Calculate target rotation from the tangent direction
        quaternion targetRotation = quaternion.LookRotation(normalizedTangent, upVector);
        
        // Smoothly interpolate rotation using slerp with a rotation speed factor
        float rotationSpeed = 5f; // Higher values = faster rotation
        localTransform.Rotation = math.slerp(localTransform.Rotation, targetRotation, deltaTime * rotationSpeed);
        
        // Keep angular velocity at zero to prevent unwanted rotation
        physicsVelocity.Angular = float3.zero;
    }
}