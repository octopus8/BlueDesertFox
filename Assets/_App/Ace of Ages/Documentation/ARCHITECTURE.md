# TransformFollower — Architecture Diagrams

> **Scope:** This document covers the TransformFollower subsystem only.  
> For the Ace of Ages scene-wide architecture, see **[SCENE_OVERVIEW.md](SCENE_OVERVIEW.md)**.



## System Overview

```mermaid
flowchart LR
    subgraph GOW["GameObject World (Outside SubScene)"]
        T["Transform (Target)\n- Player\n- Camera\n- UI Element"]
    end
    subgraph SS["DOTS SubScene"]
        E["Entity Components:\n• LocalTransform\n• TransformReference\n• Settings"]
    end
    TFS["TransformFollowerSystem\n① Read Transform\n② Update Entity"]

    T -->|"reads position/rotation"| TFS
    TFS -->|"updates LocalTransform"| E
    E -.->|"follows"| T
```

## Component Relationships

```mermaid
flowchart TD
    subgraph AUTH["AUTHORING (Edit Mode — GameObject in SubScene)"]
        MA["TransformFollowerAuthoring\n• targetTransform : Transform\n• offset : Vector3\n• followRotation : bool\n• smoothTime : float"]
    end

    subgraph RUNTIME["RUNTIME ECS (Play Mode — Entity)"]
        TR["TransformReference (Managed)\n• target : Transform\n⚠️ LIMITATION (Managed)"]
        TFS["TransformFollowerSettings (Unmanaged)\n• offset : float3\n• followRotation : bool\n• smoothTime : float"]
        LT["LocalTransform (Unmanaged)\n• Position : float3  ← Updated by System\n• Rotation : quaternion ← Updated by System\n• Scale : float"]
    end

    MA -->|"Baker converts"| TR
    MA -->|"Baker converts"| TFS
    MA -->|"Baker converts"| LT
```

## Update Flow (Simple System)

```mermaid
flowchart TD
    FS["Frame Start"]
    Q["TransformFollowerSystem.OnUpdate()\nFor each entity with:\n• LocalTransform (ref)\n• TransformFollowerSettings (in)\n• TransformReference (in)"]
    RT["Read Transform.position\n⚠️ MAIN THREAD ONLY (Managed reference)"]
    RR["Read Transform.rotation\n⚠️ MAIN THREAD ONLY"]
    CP["Calculate target position\ntargetPos = Transform.position + offset"]
    AS["Apply smoothing\nposition = lerp(current, target, smoothFactor)"]
    ULT["Update LocalTransform.Position"]
    ULR["Update LocalTransform.Rotation (if enabled)"]
    FE["Continue to other systems"]

    FS --> Q --> RT --> RR --> CP --> AS --> ULT --> ULR --> FE
```

## Update Flow (Optimized System)

```mermaid
flowchart TD
    FS["Frame Start"]
    P1["Phase 1: Main Thread\nFor each TransformReference:\n• Read Transform.position\n• Read Transform.rotation\n• Store in NativeArray\n⚠️ Cannot parallelize — Managed access"]
    P2["Phase 2: Parallel Jobs — BurstCompile\nFor each entity:\n• Read cached Transform data\n• Calculate target position\n• Apply smoothing\n• Update LocalTransform\n✅ CAN parallelize — Only unmanaged"]
    CJ["Complete jobs"]
    FE["Continue to other systems"]

    FS --> P1 --> P2 --> CJ --> FE
```

## Data Flow

```mermaid
flowchart LR
    subgraph GOW["GameObject World"]
        T["Transform\n.position\n.rotation"]
    end
    subgraph BRIDGE["Bridge (Managed Component)"]
        TR["TransformReference\n.target : Transform"]
    end
    subgraph ECS["ECS World"]
        S["Settings\n+ offset\n+ smooth"]
        LT["Update LocalTransform"]
    end

    T -->|"System reads every frame"| TR
    TR --> S --> LT
```

## The Fundamental Limitation

The `TransformReference` managed component is required because Burst/Jobs **cannot** access GameObjects:

```csharp
// ❌ CANNOT DO (Burst/Jobs):
[BurstCompile]
void Update(Transform target)
{
    float3 pos = target.position; // ERROR — Managed reference
}

// ✅ CAN DO (Main Thread):
void Update(TransformReference transformRef)
{
    if (transformRef.target != null)
        float3 pos = transformRef.target.position; // OK
}

// ✅ WORKAROUND (Optimized):
// Step 1: Main thread — cache managed reads
NativeArray<float3> positions;
foreach (var t in transforms)
    positions.Add(t.position);

// Step 2: Burst job — process unmanaged data only
[BurstCompile]
void UpdateJob(NativeArray<float3> positions) { /* ... */ }
```

## Performance Comparison

```mermaid
flowchart TD
    subgraph SIMPLE["Simple System — O(n)"]
        MT1["Main Thread\n① Read Transforms\n② Update Entities"]
    end
    subgraph OPT["Optimized System — O(n) + O(n÷cores)"]
        MT2["Main Thread\n① Read Transforms only"]
        WT["Worker Threads (Burst)\n② Update Entities — Parallel"]
        MT2 --> WT
    end
```

## Use Case Decision Tree

```mermaid
flowchart TD
    Start(["Start"])
    Q1{"Need to follow\na GameObject?"}
    Q2{"Can the target\nbe converted\nto an Entity?"}
    Q3{"How many\nfollowers?"}
    R1["Use regular\nECS systems"]
    R2["Use full ECS\n(Best performance)"]
    R3["Simple System\n(Default — good for < 100)"]
    R4["Optimized System\n(Enable it — 100+)"]

    Start --> Q1
    Q1 -->|Yes| Q2
    Q1 -->|No| R1
    Q2 -->|Yes| R2
    Q2 -->|No| Q3
    Q3 -->|"< 100"| R3
    Q3 -->|"> 100"| R4
```

---

This visual guide complements the documentation files.
