# TransformFollower — Architecture Diagrams

> **Scope:** This document covers the TransformFollower subsystem only.  
> For the Ace of Ages scene-wide architecture, see **[SCENE_OVERVIEW.md](SCENE_OVERVIEW.md)**.



## System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        UNITY SCENE                              │
│                                                                 │
│  ┌──────────────────┐              ┌────────────────────────┐  │
│  │  GameObject      │              │   DOTS SubScene        │  │
│  │  (Outside)       │              │                        │  │
│  │                  │              │  ┌──────────────────┐  │  │
│  │  ┌────────────┐  │              │  │  Entity          │  │  │
│  │  │ Transform  │◄─┼──────────────┼──┤  Components:     │  │  │
│  │  │ (Target)   │  │   Follows    │  │                  │  │  │
│  │  └────────────┘  │              │  │  • LocalTransform│  │  │
│  │                  │              │  │  • TransformRef  │  │  │
│  │  - Player        │              │  │  • Settings      │  │  │
│  │  - Camera        │              │  └──────────────────┘  │  │
│  │  - UI Element    │              │                        │  │
│  └──────────────────┘              └────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                            ▲
                            │ Updates every frame
                            │
                ┌───────────┴───────────┐
                │ TransformFollower     │
                │ System                │
                │                       │
                │ 1. Read Transform     │
                │ 2. Update Entity      │
                └───────────────────────┘
```

## Component Relationships

```
┌─────────────────────────────────────────────────────────────┐
│                       AUTHORING                             │
│  (Edit Mode - GameObject in SubScene)                       │
│                                                             │
│  TransformFollowerAuthoring (MonoBehaviour)                 │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  • targetTransform : Transform                      │   │
│  │  • offset : Vector3                                 │   │
│  │  • followRotation : bool                            │   │
│  │  • smoothTime : float                               │   │
│  └─────────────────────────────────────────────────────┘   │
│                          │                                  │
│                          │ Baker converts ▼                 │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                      RUNTIME ECS                            │
│  (Play Mode - Entity)                                       │
│                                                             │
│  Entity with Components:                                    │
│  ┌────────────────────────────────────────┐                │
│  │ TransformReference (Managed)           │                │
│  │  • target : Transform                  │ ◄─── LIMITATION│
│  └────────────────────────────────────────┘      (Managed) │
│                                                             │
│  ┌────────────────────────────────────────┐                │
│  │ TransformFollowerSettings (Unmanaged)  │                │
│  │  • offset : float3                     │                │
│  │  • followRotation : bool               │                │
│  │  • smoothTime : float                  │                │
│  └────────────────────────────────────────┘                │
│                                                             │
│  ┌────────────────────────────────────────┐                │
│  │ LocalTransform (Unmanaged)             │                │
│  │  • Position : float3    ◄──── Updated  │                │
│  │  • Rotation : quaternion◄──── by System│                │
│  │  • Scale : float                       │                │
│  └────────────────────────────────────────┘                │
└─────────────────────────────────────────────────────────────┘
```

## Update Flow (Simple System)

```
FRAME START
    ├── TransformFollowerSystem.OnUpdate()
    │   │
    │   ├── For each entity with:
    │   │   • LocalTransform (ref)
    │   │   • TransformFollowerSettings (in)
    │   │   • TransformReference (in)
    │   │
    │   ├── Read Transform.position ◄── MAIN THREAD ONLY
    │   │                                (Managed reference)
    │   ├── Read Transform.rotation ◄── MAIN THREAD ONLY
    │   │
    │   ├── Calculate target position
    │   │   targetPos = Transform.position + offset
    │   │
    │   ├── Apply smoothing
    │   │   position = lerp(current, target, smoothFactor)
    │   │
    │   ├── Update LocalTransform.Position
    │   └── Update LocalTransform.Rotation (if enabled)
    │
    └── Continue to other systems...
FRAME END
```

## Update Flow (Optimized System)

```
FRAME START
    ├── TransformFollowerSystemOptimized.OnUpdate()
    │   │
    │   ├── [Phase 1: Main Thread] ◄────────┐
    │   │   For each TransformReference:    │ Cannot parallelize
    │   │   • Read Transform.position       │ (Managed access)
    │   │   • Read Transform.rotation       │
    │   │   • Store in NativeArray          │
    │   │                                    │
    │   ├── [Phase 2: Parallel Jobs] ◄──────┐
    │   │   [BurstCompile]                  │ CAN parallelize
    │   │   For each entity:                │ (Only unmanaged)
    │   │   • Read cached Transform data    │
    │   │   • Calculate target position     │
    │   │   • Apply smoothing               │
    │   │   • Update LocalTransform         │
    │   │                                    │
    │   └── Complete jobs
    │
    └── Continue to other systems...
FRAME END
```

## Data Flow

```
     GAMEOBJECT WORLD          BRIDGE           ECS WORLD
    ┌──────────────┐                         ┌──────────┐
    │              │                         │          │
    │  Transform   │                         │  Entity  │
    │  .position ──┼──┐                      │          │
    │  .rotation   │  │                      │          │
    └──────────────┘  │                      └──────────┘
                      │                           ▲
                      │  [Managed Component]      │
                      │  TransformReference       │
                      ├───────────────────────────┤
                      │  .target : Transform      │
                      └───────────────────────────┘
                                  │
                                  │ System reads
                                  │ every frame
                                  ▼
                            ┌───────────┐
                            │ Settings  │
                            │ + offset  │
                            │ + smooth  │
                            └───────────┘
                                  │
                                  ▼
                         Update LocalTransform
```

## The Fundamental Limitation

```
┌────────────────────────────────────────────────────────────┐
│  WHY WE NEED MANAGED COMPONENTS                            │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  CANNOT DO (Burst/Jobs):                                   │
│  ┌──────────────────────────────────────────────────┐     │
│  │ [BurstCompile]                                   │     │
│  │ void Update(Transform target)  ◄── ERROR!       │     │
│  │ {                                                │     │
│  │     float3 pos = target.position;  // ✗ Managed │     │
│  │ }                                                │     │
│  └──────────────────────────────────────────────────┘     │
│                                                            │
│  CAN DO (Main Thread):                                     │
│  ┌──────────────────────────────────────────────────┐     │
│  │ void Update(TransformReference transformRef)     │     │
│  │ {                                                │     │
│  │     if (transformRef.target != null)  // ✓      │     │
│  │     {                                            │     │
│  │         float3 pos = transformRef.target.position;│     │
│  │     }                                            │     │
│  │ }                                                │     │
│  └──────────────────────────────────────────────────┘     │
│                                                            │
│  WORKAROUND (Optimized):                                   │
│  ┌──────────────────────────────────────────────────┐     │
│  │ // Step 1: Main thread                           │     │
│  │ NativeArray<float3> positions;                   │     │
│  │ foreach (var t in transforms)                    │     │
│  │     positions.Add(t.position);  // ✓            │     │
│  │                                                  │     │
│  │ // Step 2: Burst job                            │     │
│  │ [BurstCompile]                                   │     │
│  │ void UpdateJob(NativeArray<float3> positions)    │     │
│  │ {                                                │     │
│  │     // Process cached data  ✓                   │     │
│  │ }                                                │     │
│  └──────────────────────────────────────────────────┘     │
└────────────────────────────────────────────────────────────┘
```

## Performance Comparison

```
Simple System:
    ┌────────────────┐
    │ Main Thread    │ ◄── All work here
    ├────────────────┤
    │ Read Transforms│
    │ Update Entities│
    └────────────────┘
    Time: O(n)

Optimized System:
    ┌────────────────┐
    │ Main Thread    │ ◄── Only Transform reads
    ├────────────────┤
    │ Read Transforms│
    └────────────────┘
           │
           ▼
    ┌────────────────┐
    │ Worker Threads │ ◄── Parallel processing
    ├────────────────┤
    │ Update Entities│ [Burst]
    └────────────────┘
    Time: O(n) + O(n/cores)
```

## Use Case Decision Tree

```
                    Start
                      │
                      ▼
        Need to follow GameObject?
                ┌─────┴─────┐
               Yes          No
                │            │
                ▼            ▼
    Can convert to Entity?  Use regular
        ┌───────┴────┐      ECS systems
       Yes           No
        │             │
        ▼             ▼
   Use full ECS   How many followers?
   (Best perf)      ┌──┴──┐
                 <100    >100
                   │       │
                   ▼       ▼
             Simple Sys  Optimized Sys
             (Default)    (Enable it)
```

---

This visual guide complements the documentation files.

