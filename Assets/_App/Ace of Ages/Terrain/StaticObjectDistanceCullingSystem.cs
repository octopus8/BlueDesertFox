using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Burst-compiled system that adds and removes <see cref="DisableRendering"/> on static object entities
/// based on their distance to the camera, implementing the <see cref="StaticObjectLODConfig.enableDistanceCulling"/>
/// and <see cref="StaticObjectLODConfig.maxObjectRenderDistance"/> settings that are baked but were previously unused.
///
/// Entities with <see cref="DisableRendering"/> are skipped entirely by Entities.Graphics (BRG):
/// no frustum culling, no GPU instance data upload, no batch processing. This keeps the BRG
/// entity count proportional to the visible set rather than the total spawned set, which is the
/// primary reason XRUpdate time scales with <see cref="StaticObjectSpawnerConfig.maxObjectsPerTile"/>.
///
/// World position is computed as <c>tilePosition + localOffset</c> rather than reading the object
/// entity's own <see cref="LocalTransform"/> directly, so the result is always accurate even during
/// the frame the object transitions from culled to visible (before <see cref="objectPositionUpdateSystem"/>
/// has run).
///
/// Hysteresis (from <see cref="StaticObjectLODConfig.hysteresisDelta"/>) prevents per-frame
/// add/remove oscillation at the render distance boundary.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
[RequireMatchingQueriesForUpdate]
public partial struct StaticObjectDistanceCullingSystem : ISystem
{
    private ComponentLookup<LocalTransform> _tileTransformLookup;

    /// <summary>Registers required singletons and caches the tile transform lookup.</summary>
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<StaticObjectLODConfig>();
        state.RequireForUpdate<StaticObjectLODMeshInfoReady>();
        state.RequireForUpdate<CameraDataSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        _tileTransformLookup = state.GetComponentLookup<LocalTransform>(isReadOnly: true);
    }

    /// <summary>
    /// Reads the LOD config and camera position, then schedules two parallel jobs:
    /// one to cull newly-distant entities (add <see cref="DisableRendering"/>),
    /// one to un-cull entities that have moved back into range (remove <see cref="DisableRendering"/>).
    /// Both jobs use a single <see cref="EndSimulationEntityCommandBufferSystem"/> ECB so structural
    /// changes play back once per frame, avoiding archetype fragmentation.
    /// No work is performed when <see cref="StaticObjectLODConfig.enableDistanceCulling"/> is false.
    /// </summary>
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var lodConfig = SystemAPI.GetSingleton<StaticObjectLODConfig>();
        if (!lodConfig.enableDistanceCulling)
            return;

        float3 cameraPosition = SystemAPI.GetSingleton<CameraDataSingleton>().position;
        float maxDist = lodConfig.maxObjectRenderDistance;
        float hysteresis = lodConfig.hysteresisDelta;

        _tileTransformLookup.Update(ref state);

        var configEntity = SystemAPI.GetSingletonEntity<StaticObjectLODConfig>();
        var lodInfoBuffer = state.EntityManager.GetBuffer<StaticObjectLODMaterialMeshInfoElement>(configEntity, isReadOnly: true);
        var lodMeshInfos = new NativeArray<MaterialMeshInfo>(lodInfoBuffer.Length, Allocator.TempJob);
        for (int i = 0; i < lodInfoBuffer.Length; i++)
            lodMeshInfos[i] = lodInfoBuffer[i].materialMeshInfo;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Job 1: Check visible entities — cull those that moved beyond render distance.
        var cullJob = new CullDistantObjectsJob
        {
            tileTransformLookup = _tileTransformLookup,
            cameraPositionXZ = new float2(cameraPosition.x, cameraPosition.z),
            cullDistance = maxDist + hysteresis,
            ecb = ecb.AsParallelWriter()
        };
        state.Dependency = cullJob.ScheduleParallel(state.Dependency);

        // Job 2: Check culled entities — sync pose/LOD then un-cull those back into range.
        var unCullJob = new UnCullNearObjectsJob
        {
            tileTransformLookup = _tileTransformLookup,
            cameraPositionXZ = new float2(cameraPosition.x, cameraPosition.z),
            unCullDistance = maxDist - hysteresis,
            lod0Distance = lodConfig.lod0Distance,
            lod1Distance = lodConfig.lod1Distance,
            lod2Distance = lodConfig.lod2Distance,
            hysteresis = hysteresis,
            lodsPerObjectType = lodConfig.lodsPerObjectType,
            lodMeshInfos = lodMeshInfos,
            ecb = ecb.AsParallelWriter()
        };
        state.Dependency = unCullJob.ScheduleParallel(state.Dependency);
        lodMeshInfos.Dispose(state.Dependency);
    }

    /// <summary>
    /// Burst-compiled parallel job that evaluates currently-visible static objects and adds
    /// <see cref="DisableRendering"/> to those whose computed world position is beyond
    /// <c>cullDistance</c> from the camera (XZ plane only).
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(GlobalStaticObjectInstance))]
    [WithNone(typeof(DisableRendering))]
    private partial struct CullDistantObjectsJob : IJobEntity
    {
        [ReadOnly]
        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> tileTransformLookup;

        [ReadOnly] public float2 cameraPositionXZ;
        [ReadOnly] public float cullDistance;

        public EntityCommandBuffer.ParallelWriter ecb;

        /// <summary>
        /// Computes the object's world XZ position from its tile transform plus local offset
        /// and adds <see cref="DisableRendering"/> if the XZ distance to the camera exceeds
        /// <c>cullDistance</c>.
        /// </summary>
        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int chunkIndex,
            in StaticObjectTileOwnership ownership)
        {
            if (!tileTransformLookup.HasComponent(ownership.tileEntity))
                return;

            float3 tilePos = tileTransformLookup[ownership.tileEntity].Position;
            float2 worldXZ = new float2(
                tilePos.x + ownership.localOffset.x,
                tilePos.z + ownership.localOffset.z);

            float dist = math.distance(worldXZ, cameraPositionXZ);
            if (dist > cullDistance)
                ecb.AddComponent<DisableRendering>(chunkIndex, entity);
        }
    }

    /// <summary>
    /// Burst-compiled parallel job that evaluates currently-culled static objects and removes
    /// <see cref="DisableRendering"/> from those whose computed world position has moved back
    /// within <c>unCullDistance</c> of the camera (XZ plane only).
    /// Before un-culling, syncs world position and LOD mesh so the first visible frame is correct.
    /// </summary>
    [BurstCompile]
    [WithAll(typeof(GlobalStaticObjectInstance), typeof(DisableRendering))]
    private partial struct UnCullNearObjectsJob : IJobEntity
    {
        [ReadOnly]
        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> tileTransformLookup;

        [ReadOnly] public float2 cameraPositionXZ;
        [ReadOnly] public float unCullDistance;
        [ReadOnly] public float lod0Distance;
        [ReadOnly] public float lod1Distance;
        [ReadOnly] public float lod2Distance;
        [ReadOnly] public float hysteresis;
        [ReadOnly] public int lodsPerObjectType;
        [ReadOnly] public NativeArray<MaterialMeshInfo> lodMeshInfos;

        public EntityCommandBuffer.ParallelWriter ecb;

        /// <summary>
        /// Computes world position from tile transform plus local offset. When within un-cull range,
        /// syncs <see cref="LocalTransform"/>, applies distance LOD, then removes <see cref="DisableRendering"/>.
        /// </summary>
        private void Execute(
            Entity entity,
            [ChunkIndexInQuery] int chunkIndex,
            in StaticObjectTileOwnership ownership,
            in LocalTransform transform,
            in GlobalStaticObjectInstanceData instanceData)
        {
            if (!tileTransformLookup.HasComponent(ownership.tileEntity))
                return;

            float3 tilePos = tileTransformLookup[ownership.tileEntity].Position;
            float3 worldPos = tilePos + ownership.localOffset;
            float2 worldXZ = new float2(worldPos.x, worldPos.z);

            float dist = math.distance(worldXZ, cameraPositionXZ);
            if (dist >= unCullDistance)
                return;

            ecb.SetComponent(chunkIndex, entity, new LocalTransform
            {
                Position = worldPos,
                Rotation = transform.Rotation,
                Scale = transform.Scale
            });

            byte newLOD = StaticObjectLODUtility.DetermineLODLevel(
                dist,
                instanceData.currentLODLevel,
                lod0Distance,
                lod1Distance,
                lod2Distance,
                hysteresis);

            if (newLOD != instanceData.currentLODLevel)
            {
                int newMeshIndex = (instanceData.objectTypeIndex * lodsPerObjectType) + newLOD;
                if (lodMeshInfos.Length > newMeshIndex)
                    ecb.SetComponent(chunkIndex, entity, lodMeshInfos[newMeshIndex]);
            }

            var updatedInstanceData = instanceData;
            updatedInstanceData.currentLODLevel = newLOD;
            updatedInstanceData.lastDistanceToPlayer = dist;
            ecb.SetComponent(chunkIndex, entity, updatedInstanceData);

            ecb.RemoveComponent<DisableRendering>(chunkIndex, entity);
        }
    }
}
