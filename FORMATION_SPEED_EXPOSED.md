# Formation Speed Exposure Summary

## Overview
Exposed the previously hardcoded `baseApproachSpeed` parameter as a configurable `formationSpeed` field that can be set per enemy spawner in the Unity Editor. This speed is now used consistently across all three movement phases: approaching, following, and exiting.

## Changes Made

### 1. FormationMovementState Component
**File**: `Assets/_App/Ace of Ages/EnemySpawner/FormationMovementState.cs`
- Added `formationSpeed` field to track per-entity movement speed
- Used during both ApproachingSpline and LeavingSpline phases

### 2. EnemySpawnerAuthoring Component
**File**: `Assets/_App/Ace of Ages/EnemySpawner/EnemySpawnerAuthoring.cs`
- Added `[SerializeField] private float formationSpeed = 5f;` with default value of 5 m/s
- Added tooltip: "Movement speed for enemies during approach and exit phases"
- Updated Baker to pass formationSpeed to runtime component

### 3. EnemySpawner Runtime Component
**File**: `Assets/_App/Ace of Ages/EnemySpawner/EnemySpawnerAuthoring.cs`
- Added `formationSpeed` field to IComponentData struct
- Passed from authoring to runtime in Baker

### 4. EnemySpawnerSystem
**File**: `Assets/_App/Ace of Ages/EnemySpawner/EnemySpawnerSystem.cs`
- Updated FormationMovementState initialization to set `formationSpeed` from spawner config:
  ```csharp
  formationSpeed = enemySpawner.ValueRO.formationSpeed
  ```
- **Updated SplineFollower.moveSpeed initialization to use formationSpeed** (was hardcoded to 5f):
  ```csharp
  moveSpeed = enemySpawner.ValueRO.formationSpeed
  ```

### 5. FormationMovementSystem
**File**: `Assets/_App/Ace of Ages/EnemySpawner/FormationMovementSystem.cs`
- Replaced hardcoded `baseApproachSpeed = 5f` with `movementState.formationSpeed` in HandleApproachPhase
- Replaced hardcoded `baseExitSpeed = 10f` with `movementState.formationSpeed` in HandleLeavingPhase
- Both phases now use the same configurable speed value

## Usage
In the Unity Editor:
1. Select any GameObject with EnemySpawnerAuthoring component
2. Adjust "Formation Speed" field under "Formation Settings" section
3. Default value: 5.0 m/s
4. This speed is used for **all three phases**:
   - Approaching the spline entry point
   - Following the spline path
   - Exiting after spline completion
5. Speed is scroll-velocity-compensated automatically in approach/exit phases
6. SplineFollowerSystem handles spline-following with the configured moveSpeed

## Benefits
- Single unified speed control for all movement phases
- Per-spawner speed configuration without code changes
- Maintains scroll velocity compensation for smooth terrain scrolling
- Designer-friendly with Inspector tooltips
- Consistent movement speed throughout enemy lifecycle

