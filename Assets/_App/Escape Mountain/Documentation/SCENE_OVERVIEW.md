# Ace of Ages — Scene Overview

Top-level architecture guide for the Ace of Ages scene.

**Complete document listing:** [Table of Contents](TABLE_OF_CONTENTS.md)

---

## Architecture Summary

Ace of Ages is a Unity 6 VR application using a hybrid ECS + MonoBehaviour architecture:

- **ECS (DOTS):** All performance-critical runtime systems live in `Entities Subscene.unity` and run via Unity.Entities
- **MonoBehaviour:** VR input (`PlayerShootingInput`), scene entry point (`AceOfAges.cs`)
- **Bridge:** `TransformFollowerSystem` and `PlayerTrackingInitSystem` connect the MonoBehaviour player rig to ECS entities

```mermaid
flowchart TD
    subgraph MAIN["Main Scene — MonoBehaviour Layer"]
        AOA["AceOfAges.cs\nEntry point — triggers enemy spawns"]
        PSI["PlayerShootingInput.cs\nInput System → ECS doShoot flag"]
    end

    subgraph ECS["Entities Subscene — ECS World"]
        TS["Terrain Systems\nInfinite procedural terrain"]
        SOS["Static Object Systems\nTrees, turrets, decorations"]
        ES["Enemy Spawner\nBowling-pin formations on splines"]
        SS["Shooting Systems\nBullet pool, collision, VFX"]
        EFF["Effects\nDirt explosion pool"]
        TF["TransformFollower\nECS entities → GameObject bridge"]
        SPL["Splines\nBlobAsset spline data"]
    end

    MAIN -->|"bridge"| ECS
```

---

## System Execution Order

```mermaid
flowchart TD
    subgraph INIT["InitializationSystemGroup"]
        I1["PlayerTrackingInitSystem"]
        I2["WorldOriginTrackingInitSystem"]
        I3["TransformFollowerInitSystem"]
        I4["BulletPoolSystem"]
        I5["DirtExplosionPoolSystem"]
        I6["StaticObjectLODMeshInfoInitSystem"]
    end

    subgraph SIM["SimulationSystemGroup"]
        S1["ResetEventsSystem\n(resets doSpawn, doShoot)"]
        S2["EnemySpawnerSystem\n(before ResetEvents)"]
        S3["FormationMovementSystem\n(before SplineFollower)"]
        S4["PlayerScrollVelocitySystem\nor ConstantScrollVelocitySystem"]
        S5["ScrollTerrainSystem"]
        S6["TileSpawningSystem"]
        S7["TileScrollPositionSystem"]
        S8["TerrainAnchorSystem"]
        S9["TerrainMeshGenerationSystem"]
        S10["TerrainDistanceTrackingSystem"]
        S11["CameraDataUpdateSystem"]
        S12["TerrainColliderPreparationSystem"]
        S13["TerrainPhysicsSystem"]
        S14["TerrainStaticObjectSpawningSystemOptimized"]
        S15["StaticObjectSpatialChunkingSystem"]
        S16["TreeLODUpdateSystem"]
        S17["BulletShooterSystem"]
        S18["BulletLifecycleSystem"]
        S19["FormationCleanupSystem\n(LateSimulationSystemGroup)"]
        S20["StaticObjectLinkedRendererStripSystem\n(after EndSimulation ECB)"]
    end

    subgraph TSG["TransformSystemGroup"]
        T1["PlayerTargetVelocityEstimateSystem"]
        T2["TransformFollowerSystemOptimized"]
        T3["StaticObjectPositionUpdateSystem"]
        T4["TurretAimingSystem"]
        T5["TurretBarrelSystem"]
        T6["TurretShooterSystem"]
    end

    subgraph FIXED["FixedStepSimulationSystemGroup"]
        F1["BulletTerrainScrollVelocitySystem"]
        F2["BulletCollisionSystem"]
    end

    subgraph PRES["PresentationSystemGroup"]
        P1["TerrainRenderingSystem"]
        P2["DirtExplosionPlaySystem"]
    end

    INIT --> SIM --> TSG
    SIM --> FIXED
    TSG --> PRES
```

---

## Key Singletons (ECS)

| Singleton | Description | Entity source |
| ----------------------------------- | ----------------------------------------------- | ------------------------------------------ |
| `TerrainTileConfig` | All terrain config parameters | `TerrainConfigAuthoring` |
| `ScrollOffset` | Accumulated terrain scroll offset | `TerrainConfigAuthoring` |
| `TerrainScrollVelocity` | Current scroll direction + speed | `TerrainConfigAuthoring` |
| `PlayerTransformReference` | Managed: player GO Transform | `TerrainConfigAuthoring` |
| `PlayerTargetVelocity` | Smoothed player horizontal velocity | `TerrainConfigAuthoring` |
| `CameraDataSingleton` | Camera position + forward for collider priority | `CameraDataUpdateSystem` (runtime) |
| `StaticObjectSpawnerConfig` | Static object spawn density/filters | `StaticObjectSpawnerConfigAuthoring` |
| `StaticObjectLODConfig` | LOD distances and VR frame-skip config | `StaticObjectSpawnerConfigAuthoring` |
| `PlayerTerrainScrollVelocityConfig` | Player scroll speed/rotation settings | `PlayerScrollVelocityAuthoring` |
| `WorldOriginTransformReference` | Managed: world origin GO Transform | `PlayerScrollVelocityAuthoring` |
| `PrefabEntitiesReferences` | Enemy, bullet, dirt explosion prefab entities | `PrefabEntitiesReferencesAuthoring` |
| `BulletPoolConfig` | Pool size config | `BulletPoolConfigAuthoring` |
| `DirtExplosionConfig` | Pool size + lifetime config | `DirtExplosionPoolConfigAuthoring` |

---

## Entry Point: `AceOfAges.cs`

The `AceOfAges` MonoBehaviour on the main scene camera triggers a test enemy spawn after 3 seconds:

```csharp
IEnumerator TestFunc()
{
    yield return new WaitForSeconds(3f);
    DoTestSpawn(); // Sets EnemySpawner.doSpawn = true via EntityQuery
}
```

For production use, replace this with gameplay-driven spawn triggers.

---

**See also:**

- [Table of Contents](TABLE_OF_CONTENTS.md) — complete document index for all subsystems
- `AGENTS.md` (workspace root) — complete project conventions and architecture guide
- `Terrain/Documentation/README.md` — terrain system documentation hub
