# Formation Spawn Positioning - Visual Reference

## Spawn Position Calculation

```
                     ↑ Spline Up Vector
                     │
                     │
        Spawn Position    Spline Entry Point
              ●  ←─────────  ●─────────────→  Spline Path Direction
              │   (spawnDistance = 75)     (tangent)
              │
              │ Approach Movement
              ↓ (PhysicsVelocity)
              
        [Perpendicular Axis]
        (Spline's local Z)
```

### Side View (Looking Along Spline Tangent)

```
                     Player View Direction
                            ↓↓↓
                            
        ┌───────────────────────────────────────┐
        │         [Player FOV]                  │
        │                                       │
        │                                       │
        │              ●  Spline Entry          │
        │              │                        │
        │              │ On Spline Path         │
        │              ●  Spline Start          │
        │              │                        │
        │              │                        │
        │              ●  (more spline)         │
        │                                       │
        └───────────────────────────────────────┘
                            
                            
●  ←─────(75 units)────→  ●  Spline Entry
│                         │
│  Spawn Position         │  Formation will
│  (Outside view)         │  approach from here
│                         │
│  Enemies spawn here     │  Then lock onto
│  in bowling pin         │  spline at this point
│  formation              │
```

### Top-Down View (XZ Plane)

```
                    Spline Path
                    ═══════════════════════════════►
                         (tangent direction)
                         
                         
                         
        Spawn Area          │          Player View Cone
        (10 enemies)        │              /\
                           │             /  \
         ● ● ● ●           │            /    \
          ● ● ●            │           /      \
           ● ●           ●─┼─●        /        \
            ●          Entry│        /          \
                      Point │       /   [FOV]    \
         │              │  │      /              \
         │              │  │     /                \
         └──────────────┘  │    ●  ← Player       \
         Approach Movement  │                       \
         (Physics Velocity) │                        \
                           │                         \
                           │                          \
                    (perpendicular)                    \
```

### Formation Layout at Spawn

```
Bowling Pin Formation (perpendicular to spline):

        ● ● ● ●   ← Row 3 (front)
         ● ● ●    ← Row 2
          ● ●     ← Row 1
           ●      ← Row 0 (back)
           │
           │ All enemies move toward →
           │
           ●      ← Spline Entry Point
        ═══════  ← Spline Path
```

## Coordinate System

### Spline Coordinate Frame
```
    Y (Up Vector)
    │
    │
    └─────► X (Right Vector = cross(Up, Tangent))
   ╱
  ╱
 Z (Tangent)
```

### Spawn Offset Calculation
```csharp
// Get spline's right vector (perpendicular to path)
float3 rightVector = cross(upVector, tangent);

// Offset along this perpendicular axis
float3 spawnPosition = entryPoint + rightVector * spawnDistance;
```

**Result**: Enemies spawn to the **side** of the spline path.

## Distance Relationships

### Spawn Distance (Default: 75 units)
```
Entry Point ─────(75)────→ Spawn Position
            perpendicular
```

### Approach Threshold (Default: 5 units)
```
Entry Point ───(5)─── [Transition Zone] ───(70)─── Spawn Start
              │                              │
         Starts Following              Starts Approaching
```

### View Distance (From TerrainTileConfig)
```
Player ──(500)── View Distance Edge ──(100 buffer)── Cleanup Point
         │                                   │
      Visible                            Destroyed
```

## Phase Transitions - Distance Based

```
Distance from Entry Point:

75  ●───────────────────────────────────● Spawn
    │                                   │
70  │  ApproachingSpline               │
    │  (moving toward entry)            │
    │                                   │
10  │                                   │
    │                                   │
 5  ●───────────────────────────────────● Threshold
    │                                   │
 0  ●  FollowingSpline                  │ Entry Point Reached
    │  (on spline, distance ratio      │
    │   increments each frame)          │
    │                                   │
    ●  distanceRatio = 0.99             │ Near Spline End
    │                                   │
    ●  LeavingSpline                    │ Spline Completed
    │  (constant velocity in exit dir)  │
    │                                   │
    │                                   │
    │  Distance from Player:            │
500 │  (viewDistance)                   │
    │                                   │
600 ●  OutOfBounds                      │ Despawn Distance
    │                                   │
    X  Destroyed                        X
```

## Formation Member Positions

### During Approach Phase
```
Each enemy targets its own entry point:

    ● ● ● ●    →    ●       Entry points spread along spline
     ● ● ●     →     ●      (based on forwardOffset)
      ● ●      →      ●
       ●       →       ●

(Formation spreads, then tightens during approach)
```

### During Following Phase
```
Formation maintains offsets on spline:

Spline: ═════════════════════════►

        ● ● ● ●   ← Front row
         ● ● ●    
          ● ●     
           ●      ← Back row (leader)
```

### During Exit Phase
```
Formation continues straight:

        ● ● ● ●   →
         ● ● ●    →
          ● ●     →
           ●      →
           
(All enemies move in captured exit direction)
```

## Spawn Distance Visualization

```
Camera FOV (Typical VR ~100°):
        
        ╱────────────────────╲
       ╱  [Visible Area]     ╲
      ╱                       ╲
     ●  Player                 ╲
      ╲                       ╱
       ╲                     ╱
        ╲───────────────────╱


Spawn Region (perpendicular, distance = 75):
        
                │
                │ ◄──── Enemies spawn here
                │       (outside FOV)
                ●
              Spline
              Entry
                │
                │
                │ Spline continues
                ▼
```

## Perpendicular Axis Explanation

The spline's "local Z axis" means perpendicular to the path:

```
If spline goes North-South (tangent = [0, 0, 1]):
└─ Perpendicular is East-West (right = [1, 0, 0])
   └─ Enemies spawn to the EAST or WEST

If spline goes East-West (tangent = [1, 0, 0]):
└─ Perpendicular is North-South (right = [0, 0, 1])
   └─ Enemies spawn to the NORTH or SOUTH
```

**Always perpendicular to spline direction at entry point!**

## Real-World Example

### Spline Configuration
- Spline starts at world position (0, 0, 0)
- Spline tangent at start: (1, 0, 0) [pointing East]
- Spline up vector: (0, 1, 0) [pointing up]

### Calculated Vectors
```csharp
tangent = normalize([1, 0, 0]) = [1, 0, 0]
upVector = [0, 1, 0]
rightVector = cross(upVector, tangent)
           = cross([0, 1, 0], [1, 0, 0])
           = [0, 0, -1]  // Points South
```

### Spawn Position
```
entryPoint = [0, 0, 0] (spline start)
spawnDistance = 75
spawnOffset = [0, 0, -1] * 75 = [0, 0, -75]
spawnPosition = [0, 0, 0] + [0, 0, -75] = [0, 0, -75]
```

**Result**: Enemies spawn 75 meters South of spline start!

### Approach Movement
```
Direction: from [0, 0, -75] toward [0, 0, 0]
Vector: [0, 0, 75]
Normalized: [0, 0, 1]  // Pointing North

PhysicsVelocity = [0, 0, 1] * approachSpeed(10) = [0, 0, 10]
Time to reach: 75 / 10 = 7.5 seconds
```

## Debugging Visualization

### Add Gizmos to Scene
Create `FormationDebugGizmos.cs`:

```csharp
public class FormationDebugGizmos : MonoBehaviour
{
    void OnDrawGizmos()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !Application.isPlaying) return;
        
        foreach (var (state, transform) in 
                 SystemAPI.Query<RefRO<FormationMovementState>, RefRO<LocalTransform>>())
        {
            Vector3 pos = transform.ValueRO.Position;
            
            // Draw phase-colored sphere
            switch (state.ValueRO.phase)
            {
                case MovementPhase.ApproachingSpline:
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(pos, 2f);
                    // Draw line to entry point
                    Gizmos.DrawLine(pos, state.ValueRO.splineEntryPoint);
                    break;
                    
                case MovementPhase.FollowingSpline:
                    Gizmos.color = Color.green;
                    Gizmos.DrawSphere(pos, 1f);
                    break;
                    
                case MovementPhase.LeavingSpline:
                    Gizmos.color = Color.blue;
                    Gizmos.DrawWireSphere(pos, 2f);
                    // Draw exit ray
                    Gizmos.DrawRay(pos, state.ValueRO.exitDirection * 20f);
                    break;
                    
                case MovementPhase.OutOfBounds:
                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(pos, 1.5f);
                    break;
            }
        }
    }
}
```

**Color Key**:
- 🟡 Yellow wireframe: Approaching
- 🟢 Green solid: Following
- 🔵 Blue wireframe + ray: Leaving
- 🔴 Red solid: Out of bounds

---

## Future Enhancements

### 1. Configurable Approach Speed
Add to `EnemySpawner`:
```csharp
public float approachSpeed;  // Default: 10
public float exitSpeed;      // Default: 10
```

### 2. Formation Cohesion During Approach
Add formation center calculation:
```csharp
float3 formationCenter = CalculateFormationCenter(allMembers);
// Each member approaches relative to center
```

### 3. Curved Approach Paths
Use secondary spline for approach trajectory:
```csharp
public Entity approachSpline;  // Optional approach path
```

### 4. Wave Spawning
Add timer system for repeated spawns:
```csharp
public struct WaveConfig : IComponentData
{
    public float spawnInterval;
    public int maxActiveWaves;
}
```

---

**Implementation Complete - Ready for Production Use**

