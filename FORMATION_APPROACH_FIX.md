# Formation Approach Fix - Keep Formation During Approach

## Problem
Enemies were spawned at individual formation positions but each moved toward its own entry point using different directions, causing them to converge/diverge and break formation during the approach phase.

## Solution
All enemies now move in the **same direction** (spline tangent) at the **same speed** during approach, maintaining their formation positions as they move toward the spline together.

## Changes Made

### 1. FormationMovementState Component
**File**: `Assets/_App/Ace of Ages/EnemySpawner/FormationMovementState.cs`
- Added `approachDirection` field (float3) to store the shared movement direction
- This is set to the spline tangent at spawn time, ensuring all formation members move parallel

### 2. EnemySpawnerSystem
**File**: `Assets/_App/Ace of Ages/EnemySpawner/EnemySpawnerSystem.cs`
- Set `approachDirection = startSample.tangent` when initializing FormationMovementState
- All enemies in the same spawn batch share this direction
- Already using `formationSpeed` for both FormationMovementState and SplineFollower.moveSpeed

### 3. FormationMovementSystem
**File**: `Assets/_App/Ace of Ages/EnemySpawner/FormationMovementSystem.cs`
- **Changed direction calculation**: Use `movementState.approachDirection` instead of `math.normalize(toEntry)`
- **Changed distance check**: Use `math.dot(toEntry, direction)` to measure distance **along the approach axis** instead of direct 3D distance
- **Simplified transition**: Single threshold check (`distanceAlongApproach <= 1f`) replaces complex multi-condition logic
- All enemies now move in parallel, maintaining formation spacing

## Behavior Changes

### Before:
- Enemy 0 (center): Moves straight toward center entry point
- Enemy 1 (left): Moves diagonally inward toward left entry point
- Enemy 2 (right): Moves diagonally inward toward right entry point
- **Result**: Formation converges during approach, breaks cohesion

### After:
- All enemies: Move in the same direction (spline tangent)
- All enemies: Same speed (formationSpeed)
- Distance check: Based on progress along shared axis
- **Result**: Formation maintained during approach, moves as cohesive unit

## Technical Details

**Spawn Geometry:**
```csharp
splineEntryPoint = startSample.position + 
                   startSample.tangent * formationData.forwardOffset + 
                   rightVector * formationData.lateralOffset.x;
spawnPosition = splineEntryPoint - startSample.tangent * spawnDistance;
```
Each enemy spawns at their formation-offset position behind the spline.

**Formation Movement:**
```csharp
float distanceAlongApproach = math.dot(toEntry, direction);
```
This projects the vector to the entry point onto the approach direction, measuring how far along the approach axis the enemy needs to travel. Since all enemies use the same direction and check distance the same way, they transition together.

**Formation Preservation:**
Since all enemies:
1. Start at formation-offset spawn positions
2. Move in the same direction (spline tangent) at the same speed
3. Check arrival based on progress along that shared direction

They naturally maintain their relative positions throughout the approach.

## Testing
With `formationSpeed = 200`:
- Approach phase: 200 m/s in formation, moving as cohesive unit ✓
- Following phase: 200 m/s along spline with formation offsets ✓
- Exit phase: 200 m/s in exit direction ✓

