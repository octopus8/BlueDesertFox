using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// System that manages dirt explosion lifecycle - returns explosions to pool after their lifetime expires.
/// Uses time-based cleanup (configured lifetime from DirtExplosionConfig).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(TransformSystemGroup))]
public partial struct DirtExplosionLifecycleSystem : ISystem
{
    /// <summary>Registers <see cref="DirtExplosion"/> and <see cref="DirtExplosionConfig"/> requirements.</summary>
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DirtExplosion>();
        state.RequireForUpdate<DirtExplosionConfig>();
    }

    /// <summary>
    /// Iterates all active dirt explosion entities and, for any whose elapsed time since
    /// <see cref="DirtExplosionData.spawnTime"/> exceeds <see cref="DirtExplosionConfig.lifetime"/>,
    /// resets their state, moves them below the map, and returns them to the <see cref="DirtExplosionPoolSystem"/>.
    /// </summary>
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.TryGetSingleton<GamePaused>(out var gamePaused) && gamePaused.Value)
            return;

        var poolSystemHandle = state.World.GetExistingSystem<DirtExplosionPoolSystem>();
        if (poolSystemHandle == SystemHandle.Null)
            return;

        ref var poolSystem = ref state.WorldUnmanaged.GetUnsafeSystemRef<DirtExplosionPoolSystem>(poolSystemHandle);

        var config = SystemAPI.GetSingleton<DirtExplosionConfig>();
        double currentTime = SystemAPI.Time.ElapsedTime;
        if (SystemAPI.TryGetSingleton(out gamePaused))
            currentTime = GamePausedUtility.GetGameplayElapsedTime(currentTime, gamePaused);

        foreach (var (explosionData, transform, anchor, entity) in
            SystemAPI.Query<RefRW<DirtExplosionData>, RefRW<LocalTransform>, RefRW<TerrainAnchorTag>>()
                .WithAll<DirtExplosion>()
                .WithEntityAccess())
        {
            if (!explosionData.ValueRO.active)
                continue;

            if (currentTime - explosionData.ValueRO.spawnTime <= config.lifetime)
                continue;

            explosionData.ValueRW = new DirtExplosionData
            {
                spawnTime = 0,
                active = false,
                triggered = false
            };

            anchor.ValueRW = new TerrainAnchorTag
            {
                basePosition = new float3(0, -10000, 0)
            };

            var lt = transform.ValueRO;
            lt.Position = new float3(0, -10000, 0);
            transform.ValueRW = lt;

            poolSystem.ReturnToPool(entity);
        }
    }
}
