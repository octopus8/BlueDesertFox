# Quick Reference: Terrain System GameObject Tracking

## Setup (3 Steps)

### 1. Assign Player
```
TerrainConfigAuthoring → Player To Track → Drag your player GameObject
```

### 2. Add Shifter
```
Add Component → FloatingOriginGameObjectShifter
Transforms To Shift → Drag player's root Transform
```

### 3. Test
```
Press Play → Move player → Terrain follows!
```

---

## Components

| Component | Type | Purpose |
|-----------|------|---------|
| `PlayerTransformReference` | Managed IComponentData | Holds GameObject Transform reference |
| `FloatingOriginGameObjectShifter` | MonoBehaviour | Shifts GameObjects during origin reset |
| `TerrainConfigAuthoring` | MonoBehaviour | Configuration + player assignment |

---

## Systems

| System | Reads | Does |
|--------|-------|------|
| `TileSpawningSystem` | Player position | Spawns/despawns tiles |
| `FloatingOriginSystem` | Player distance | Triggers origin shifts |

---

## Inspector Fields

### TerrainConfigAuthoring
```
Player Tracking
├─ Player To Track: Transform (auto-detects AutoHandPlayer/Camera)

Tile Settings
├─ Tile Size: 100m (chunk size)
├─ View Distance: 500m (render radius)
└─ Vertices Per Side: 32 (mesh detail)

Floating Origin
├─ Enabled: true
└─ Shift Threshold: 2000m
```

### FloatingOriginGameObjectShifter
```
GameObject References
└─ Transforms To Shift: Transform[] (player rig root)

Options
├─ Update Device Tracking Immediate: true (VR sync)
└─ Debug Log: false (show shift events)
```

---

## Code Snippets

### Get Player Position
```csharp
var world = World.DefaultGameObjectInjectionWorld;
var em = world.EntityManager;
var query = em.CreateEntityQuery(typeof(PlayerTransformReference));
var entity = query.GetSingletonEntity();
var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
Vector3 position = playerRef.playerTransform.position;
query.Dispose();
```

### Change Tracked Player
```csharp
var playerRef = em.GetComponentObject<PlayerTransformReference>(entity);
playerRef.playerTransform = newPlayerTransform;
```

---

## Gizmos (Scene View)

Select `TerrainConfigAuthoring` to see:
- 🟣 **Magenta sphere** = Player position
- 🟢 **Green sphere** = View distance (tile spawn radius)
- 🟡 **Yellow sphere** = Shift threshold (origin reset distance)
- 🔵 **Cyan cube** = Sample tile at player position

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "Player transform reference is null" | Assign player in TerrainConfigAuthoring |
| No terrain tiles | Check player is assigned and active |
| Player jumps after shift | Add FloatingOriginGameObjectShifter |
| Tiles don't follow | Check player is actually moving |

---

## Migration from PlayerTag

```diff
- PlayerTagAuthoring on ECS entity
+ TerrainConfigAuthoring.playerToTrack = GameObject

- RequireForUpdate<PlayerTag>()
+ RequireForUpdate<PlayerTransformReference>()

- GetSingletonEntity<PlayerTag>()
+ ManagedAPI.GetSingleton<PlayerTransformReference>()

- GetComponent<LocalTransform>(entity).Position
+ playerRef.playerTransform.position
```

---

## Key Differences: Old vs New

### Old (PlayerTag)
- ❌ Required entity in subscene
- ❌ Complex setup with baking
- ❌ Hard to integrate with MonoBehaviour
- ✅ Pure ECS approach

### New (GameObject Tracking)
- ✅ Works with any GameObject
- ✅ Simple Inspector assignment
- ✅ Easy MonoBehaviour integration
- ⚠️ Uses managed components (minimal overhead)

---

## Performance

- **Managed component overhead:** ~0.01ms per frame
- **Player position read:** Direct Transform.position access
- **Burst compilation:** Still used for tile generation, mesh creation
- **Main thread:** Only player tracking (unavoidable for GameObjects)

**Conclusion:** Performance impact is negligible compared to terrain generation.

---

## Auto-Detection

`TerrainConfigAuthoring.OnValidate()` automatically finds:
1. ✅ `AutoHandPlayer` component (VR)
2. ✅ `Camera.main` (fallback)

If both fail, manual assignment required.

---

## Editor Tools

### Terrain Status Inspector
```
Window → Terrain System Status
```
Shows:
- Active tile count
- Rendering status
- Player tracking status
- System warnings

---

## Testing Origin Shift

```csharp
// In TerrainConfigAuthoring, temporarily set:
shiftThreshold = 50f; // Instead of 2000f

// Move player 50 units → Should see:
// Console: "FloatingOriginSystem: Origin shifted by..."
// Player position resets near origin
// Terrain remains consistent
```

---

## Best Practices

1. ✅ **Assign player to root Transform** (not child camera)
2. ✅ **Use FloatingOriginGameObjectShifter** for origin shifts
3. ✅ **Enable debug logging** during development
4. ✅ **Test with low shift threshold** first (e.g., 50m)
5. ✅ **Check Gizmos** in Scene view for visualization

---

## See Full Docs

- 📖 [GAMEOBJECT_TRACKING_GUIDE.md](./GAMEOBJECT_TRACKING_GUIDE.md) - Complete guide
- 📖 [IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md) - Technical details
- 📖 [README.md](./README.md) - Main documentation

---

**Last Updated:** 2026-03-15

