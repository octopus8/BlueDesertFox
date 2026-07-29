using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

/// <summary>
/// Copies <see cref="TerrainAnchorTag"/> entity transforms into the live <see cref="PhysicsWorld"/>
/// after scroll moves them, then rebuilds the broadphase trees queries use.
/// Runs after <see cref="TerrainAnchorSystem"/> (and therefore after ground contact) so the next
/// frame's player casts — which intentionally use pre-scroll anchor poses — match CollisionWorld.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TerrainAnchorSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
public partial struct TerrainAnchorPhysicsSyncSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<TerrainAnchorTag>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency.Complete();

        ref PhysicsWorld world = ref SystemAPI.GetSingletonRW<PhysicsWorldSingleton>().ValueRW.PhysicsWorld;
        if (world.NumBodies == 0)
            return;

        NativeArray<RigidBody> bodies = world.Bodies;
        NativeArray<MotionData> motionDatas = world.MotionDatas;

        bool updateStatic = false;
        bool updateDynamic = false;
        int numStatic = world.NumStaticBodies;

        foreach (var (transform, entity) in SystemAPI
                     .Query<RefRO<LocalTransform>>()
                     .WithAll<TerrainAnchorTag, PhysicsCollider>()
                     .WithEntityAccess())
        {
            int bodyIndex = world.GetRigidBodyIndex(entity);
            if (bodyIndex < 0 || bodyIndex >= bodies.Length)
                continue;

            LocalTransform lt = transform.ValueRO;
            RigidTransform worldFromBody = new RigidTransform(lt.Rotation, lt.Position);

            RigidBody body = bodies[bodyIndex];
            body.WorldFromBody = worldFromBody;
            body.Scale = lt.Scale;
            bodies[bodyIndex] = body;

            if (bodyIndex < numStatic)
            {
                updateStatic = true;
            }
            else
            {
                int motionIndex = bodyIndex - numStatic;
                if (motionIndex >= 0 && motionIndex < motionDatas.Length)
                {
                    MotionData motion = motionDatas[motionIndex];
                    // WorldFromBody = WorldFromMotion * inverse(BodyFromMotion)
                    motion.WorldFromMotion = math.mul(worldFromBody, motion.BodyFromMotion);
                    motionDatas[motionIndex] = motion;
                }

                updateDynamic = true;
            }
        }

        if (updateStatic)
            world.CollisionWorld.UpdateStaticTree(ref world);

        if (updateDynamic)
        {
            float3 gravity = float3.zero;
            if (SystemAPI.HasSingleton<PhysicsStep>())
                gravity = SystemAPI.GetSingleton<PhysicsStep>().Gravity;

            world.CollisionWorld.UpdateDynamicTree(ref world, SystemAPI.Time.DeltaTime, gravity);
        }
    }
}
