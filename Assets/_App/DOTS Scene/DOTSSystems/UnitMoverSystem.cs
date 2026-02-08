using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

partial struct UnitMoverSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (true)
        {
            UnitMoverJob unitMoverJob = new UnitMoverJob
            {
                deltaTime = Time.deltaTime,
            };
            unitMoverJob.ScheduleParallel();
        }
        else
        {
            foreach (var (
                         localTransform,
                         moveSpeed,
                         physicsVelocity
                         )
                     in SystemAPI.Query<
                         RefRW<LocalTransform>,
                         RefRO<UnitMover>,
                         RefRW<PhysicsVelocity>
                     >())
            {
                float3 targetPosition = localTransform.ValueRO.Position + new float3(0, 0, -10);
                float3 moveDirection = targetPosition - localTransform.ValueRO.Position;
                moveDirection = math.normalize(moveDirection);
                localTransform.ValueRW.Rotation = quaternion.LookRotation(moveDirection, math.up());
                physicsVelocity.ValueRW.Linear = moveDirection * moveSpeed.ValueRO.moveSpeed * Time.deltaTime;
                physicsVelocity.ValueRW.Angular = float3.zero;
            }
        }
        
        
    }
}


[BurstCompile]
public partial struct UnitMoverJob : IJobEntity
{
    public float deltaTime;
    
    public void Execute(ref LocalTransform localTransform, in UnitMover unitMover, ref PhysicsVelocity physicsVelocity)
    {
        float3 targetPosition = localTransform.Position + new float3(0, 0, -10);
        float3 moveDirection = targetPosition - localTransform.Position;
        moveDirection = math.normalize(moveDirection);
        localTransform.Rotation = quaternion.LookRotation(moveDirection, math.up());
        physicsVelocity.Linear = moveDirection * unitMover.moveSpeed *  deltaTime;
        physicsVelocity.Angular = float3.zero;
    }
}