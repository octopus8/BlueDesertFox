using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that rotates static object entities at LOD2 to face the camera (cylindrical billboard).
/// Only affects object types marked as billboard via <see cref="GlobalStaticObjectInstanceData.isBillboardType"/>.
/// Runs after <see cref="StaticObjectLODUpdateSystem"/> so LOD level is current before rotation is applied.
/// Uses Y-axis-only (cylindrical) rotation so the billboard stays upright regardless of camera pitch.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(StaticObjectLODUpdateSystem))]
[RequireMatchingQueriesForUpdate]
public partial struct StaticObjectBillboardSystem : ISystem
{
    /// <summary>Registers required singletons.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<CameraDataSingleton>();
    }

    /// <summary>
    /// Reads the camera position from <see cref="CameraDataSingleton"/> and schedules a parallel
    /// <see cref="BillboardRotationJob"/> to rotate all LOD2 billboard entities toward the camera.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var cameraData = SystemAPI.GetSingleton<CameraDataSingleton>();
        state.Dependency = new BillboardRotationJob
        {
            cameraPosition = cameraData.position
        }.ScheduleParallel(state.Dependency);
    }
}

/// <summary>
/// Burst-compiled parallel job that rotates each LOD2 billboard static object to face the camera
/// around the Y axis (cylindrical billboard — stays upright, only yaws toward the camera XZ position).
/// Skips entities that are not billboard types or are not currently at LOD2.
/// Skips entities with <see cref="Unity.Rendering.DisableRendering"/> — culled objects are not
/// rendered and do not need their rotation updated.
/// </summary>
[BurstCompile]
[WithAll(typeof(GlobalStaticObjectInstance))]
[WithNone(typeof(Unity.Rendering.DisableRendering))]
partial struct BillboardRotationJob : IJobEntity
{
    /// <summary>World-space camera position this frame, read from <see cref="CameraDataSingleton"/>.</summary>
    [Unity.Collections.ReadOnly] public float3 cameraPosition;

    void Execute(ref LocalTransform transform, in GlobalStaticObjectInstanceData instanceData)
    {
        if (!instanceData.isBillboardType || instanceData.currentLODLevel != 2)
            return;

        float3 toCamera = cameraPosition - transform.Position;
        toCamera.y = 0f;

        float len = math.length(toCamera);
        if (len < 0.001f)
            return;

        transform.Rotation = quaternion.LookRotationSafe(toCamera / len, math.up());
    }
}
