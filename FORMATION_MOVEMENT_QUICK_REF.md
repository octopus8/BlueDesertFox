# Formation Movement Quick Reference

## How It Works Now

### Spawning
- Enemies spawn in bowling pin formation
- Each has unique `splineEntryPoint` (formation-offset from spline start)
- All share the same `approachDirection` (spline tangent)

### Approach Phase (NEW)
- **Direction**: All enemies use `movementState.approachDirection` (shared spline tangent)
- **Speed**: All enemies use `movementState.formationSpeed` (configurable in Inspector)
- **Distance Check**: `math.dot(toEntry, direction)` measures progress along shared axis
- **Transition**: When `distanceAlongApproach <= 1f`, switch to FollowingSpline
- **Result**: Formation stays intact as a cohesive unit

### Following Phase
- Uses `SplineFollower.moveSpeed` (set to `formationSpeed` at spawn)
- Formation offsets applied by `SplineFollowerSystem` via `FormationPosition` component
- All enemies follow spline at same configured speed

### Exit Phase
- Uses `movementState.formationSpeed` (same as approach)
- All enemies continue in their exit direction at configured speed

## Configuration
**Inspector Settings** (EnemySpawnerAuthoring):
- `formationSpeed`: Controls speed for all three phases (default: 5 m/s)
- `formationSpacing`: Distance between enemies in formation (default: 2 m)
- `formationCount`: Number of enemies to spawn (default: 10)
- `spawnDistance`: How far behind spline start to spawn (default: 75 m)

## Key Code Locations
- Component: `FormationMovementState.cs` (approachDirection field)
- Spawning: `EnemySpawnerSystem.cs` (sets approachDirection to spline tangent)
- Movement: `FormationMovementSystem.cs` (HandleApproachPhase uses shared direction)

