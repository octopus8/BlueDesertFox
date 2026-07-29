# Transform Follower System

Bridge between DOTS entities in subscenes and GameObject Transforms in the main scene.

**Navigation:**

| Goal | Document |
|------|---------|
| 5-minute setup | [QUICKSTART.md](QUICKSTART.md) |
| Visual architecture diagrams | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Full technical reference | [TransformFollowerREADME.md](TransformFollowerREADME.md) |
| Performance and implementation details | [IMPLEMENTATION_SUMMARY.md](IMPLEMENTATION_SUMMARY.md) |
| Testing checklist | [TESTING_GUIDE.md](TESTING_GUIDE.md) |

---

## The Key Concept

**Problem:** DOTS entities cannot directly reference GameObjects — a fundamental Unity limitation.

**Solution:** Use a managed component as a bridge:
- `TransformReference` (managed `IComponentData` class) — holds the GameObject Transform reference
- `TransformFollowerSettings` (unmanaged struct) — holds offset, rotation, and smoothing settings
- `TransformFollowerSystemOptimized` — reads Transform data on the main thread, then updates entity positions via parallel Burst jobs

**Trade-off:** The Transform read must stay on the main thread (Burst cannot access managed references), but entity position updates are Burst-compiled and run in parallel. This is unavoidable when bridging the GameObject ↔ Entity boundary.

---

## Quick Start (3 Steps)

### Step 1: Target (outside subscene)
Any moving GameObject you want entities to follow — player rig, camera, etc.

### Step 2: Entity (inside subscene)
1. Select an entity GameObject in the subscene
2. Add Component → **TransformFollowerAuthoring**
3. Drag the target GameObject into the **Target Transform** field
4. Set **Offset** if needed (e.g., `0, 2, 0` to float above)

**Note:** The reference is stored and applied at runtime in `Start()`, not during baking. This allows cross-subscene references that Unity's baking system normally disallows.

### Step 3: Test
Press Play — the entity should follow the target.

---

## Inspector Settings

| Setting | Description |
|---------|-------------|
| **Target Transform** | The GameObject to follow |
| **Offset** | Local position offset from target |
| **Follow Rotation** | Match target's rotation? |
| **Smooth Time** | `0` = instant snap, higher = smooth interpolation |

**Preset buttons (custom inspector):**
- Offset presets: Above, Behind, Front, Reset
- Smooth presets: Instant, Fast, Smooth, Slow

**Scene view gizmos:**
- Cyan line: Entity → target
- Yellow sphere: Target position
- Green sphere: Target + offset position

---

## System Variants

| | `TransformFollowerSystemOptimized` | `TransformFollowerSystem` |
|-|-------------------------------------|---------------------------|
| **Status** | **Active** (default) | Disabled (`[DisableAutoCreation]`) |
| **Best for** | All use cases | — |
| **Burst** | Partial (entity updates only) | No |
| **Jobs** | Yes (parallel entity update) | No |

**To swap back to the simple system:**
1. Remove `[DisableAutoCreation]` from `TransformFollowerSystem.cs`
2. Add `[DisableAutoCreation]` to `TransformFollowerSystemOptimized.cs`

---

## Performance

| Follower Count | Approx Frame Cost |
|----------------|-------------------|
| 10 | ~0.01ms |
| 100 | ~0.05ms |
| 1000 | ~0.3ms |

---

## Common Use Cases

**Following the player:**
```
Target: Player GameObject
Offset: (0, 2, 0)
Follow Rotation: false
Smooth Time: 0.2
```
Use for: UI elements, cameras, companion NPCs

**Homing projectile:**
```
Target: Enemy GameObject
Offset: (0, 0, 0)
Follow Rotation: true
Smooth Time: 0.05
```
Use for: Missiles, tracking projectiles

**Formation member:**
```
Target: Leader GameObject
Offset: (-2, 0, -2)
Follow Rotation: false
Smooth Time: 0.3
```
Use for: Squad formations, convoy vehicles

---

## Runtime Control

**Change target:**
```csharp
entityManager.SetComponentData(entity, new TransformReference { target = newTarget });
```

**Update settings:**
```csharp
entityManager.SetComponentData(entity, new TransformFollowerSettings
{
    offset = new float3(0, 5, 0),
    followRotation = true,
    smoothTime = 0.1f
});
```

See `TransformFollowerExample.cs` for more patterns.

---

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Entity doesn't follow | Check target is assigned; verify subscene is loaded; ensure entity is in subscene, target is outside |
| Jittery movement | Increase smooth time (try 0.2); ensure target moves smoothly |
| Performance issues | Optimized system is already active — check profiler for hot spots |

See [TESTING_GUIDE.md](TESTING_GUIDE.md) for detailed debugging steps.

---

## Tips

**Use for:** Following player/camera/UI elements; bridging GameObject → Entity worlds; hybrid ECS/GameObject workflow.

**Don't use for:** Entity → Entity following (use pure ECS queries instead — fully Burst-compatible); networked gameplay (managed components don't serialize); cases where both objects can be entities.

---

## File Locations

```
Assets/_App/Escape Mountain/TransformFollower/
├── TransformFollowerAuthoring.cs           Add to entities in subscenes
├── TransformFollowerInitSystem.cs          Runtime target search initialization
├── TransformFollowerSystemOptimized.cs     Optimized system (active)
├── TransformFollowerSystem.cs              Simple system [DisableAutoCreation]
├── TransformFollowerExample.cs             Code examples
├── Editor/
│   └── TransformFollowerAuthoringEditor.cs Custom inspector with presets
└── Documentation/
    ├── README_START_HERE.md                This file — navigation hub
    ├── QUICKSTART.md                       5-minute setup
    ├── ARCHITECTURE.md                     Visual diagrams and data flow
    ├── TransformFollowerREADME.md          Full technical reference
    ├── IMPLEMENTATION_SUMMARY.md           Implementation details and alternatives
    └── TESTING_GUIDE.md                    Test scenarios and debugging checklist
```
