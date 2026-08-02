# Ace of Ages

VR **flying shooter** scene (separate from Escape Mountain snowboarding).

## Scenes

| Scene | Path |
| ----- | ---- |
| Main | `Ace of Ages.unity` |
| Start | `Ace of Ages Start.unity` |
| Entities Subscene | `Ace of Ages Entities Subscene.unity` |

## Scripts in this folder

- `AceOfAges.cs` — scene entry / test enemy spawn trigger
- `PrefabEntitiesReferencesAuthoring.cs` — baked prefab entity singleton for enemies, bullets, VFX

## Shared DOTS systems

Terrain, shooting, enemy formations, transform follower, and effects live under **`Assets/_App/Escape Mountain/`** (shared global assembly). See:

- [Escape Mountain Scene Overview](../Escape%20Mountain/Documentation/SCENE_OVERVIEW.md)
- [Documentation Table of Contents](../Escape%20Mountain/Documentation/TABLE_OF_CONTENTS.md)
- Workspace root [`AGENTS.md`](../../../AGENTS.md)
