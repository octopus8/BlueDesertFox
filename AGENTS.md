# AI Agent Guide for BlueDesertFox

## Project Overview
Unity VR application combining traditional MonoBehaviour components with Unity DOTS (ECS). Uses AutoHand for VR hand interactions, custom word prediction system, and hybrid scene management via Addressables and ECS SubScenes.

## Architecture

### Hybrid Unity Architecture
- **MonoBehaviour Layer**: VR interactions (AutoHand), UI, keyboard input, word prediction
- **ECS Layer**: Performance-critical systems loaded via SubScenes (see `Assets/_App/Ace of Ages/` for DOTS systems)
- **Scene Management**: `SceneStartup.cs` orchestrates initial setup, loading SubScenes via `SubSceneLoader` singleton and managing camera fade-ins, calls `DeviceTracking.Instance.UpdateImmediate()` after setting tracking origin
- **UI System**: State machine pattern via `UIManager` with stack-based state management (`IUIState`, `UIState`)
- **Input System**: Uses Unity Input System with `InputSystem.actions.FindAction("ActionName")` pattern for runtime action binding. Scenes using this pattern must include `InputSystemActionsInitializer` component to set the global `InputSystem.actions` reference, or configure Project-Wide Actions in Project Settings.

### Key Namespaces & Assembly Definitions
- `Autohand` (AutoHandAssembly.asmdef): VR hand/grabbable interactions
- `LiquidForce` (LiquidForce.asmdef): Camera fading, device tracking, object following utilities, scene loading
- `App.StartScene`: Scene selection UI and Addressables-based scene loading (legacy pattern, coexists with UIManager)

### Singleton Pattern Usage
Project relies on several Singleton patterns:
- `DeviceTracking.Instance` - VR tracking origin management (LiquidForce), includes `UpdateImmediate()` for instant head follower sync
- `CameraFader.Instance` - Screen fade transitions (LiquidForce)
- `SubSceneLoader.Instance` - ECS SubScene loading system, extends `SystemBase` and sets `Instance` in `OnCreate()`
- `AutoHandPlayer.Instance` - VR player controller (Autohand namespace), uses `_Instance` backing field with lazy initialization

## Critical Systems

### VR Hand Interaction (AutoHand)
- **Grabbable objects**: Extend `Grabbable` class, implement `CanGrab()` for custom logic
- **Hand component**: `Hand.cs` manages grab states, uses `GrabType` enum (InstantGrab, HandToGrabbable, GrabbableToHand)
- **Physics-dependent**: Requires specific physics settings (see `AutoHandSetupWizard.cs` for quality presets: 50-90Hz fixedDeltaTime, solver iterations 10-30)
- Example: `Assets/AutoHand/Scripts/Hand/Hand.cs`, `Assets/AutoHand/Scripts/Grabbable/Grabbable.cs`

### Word Prediction System
Located in `Assets/Scripts/Word Prediction/`:
- **N-gram based**: `NGramGenerator.cs` uses bi-gram dictionaries for next-word prediction
- **Levenshtein autocomplete**: `Levenshtein.cs` provides spell-correction suggestions
- **Initialization**: Dictionaries loaded from Resources at Awake(), uses corpus files for training
- **Integration**: `TextFieldBehaviour.cs` triggers predictions on spacebar press
- Button selection: `AutocompleteWordPicker.cs` handles word replacement in input field

### VR Keyboard
Located in `Assets/Scripts/Keyboard/`:
- **Key.cs**: Physical key simulation with Rigidbody-based press detection (DistanceToBePressed = 0.01f)
- Press feedback: Color changes, sound via `KeySoundController`, constrained physics movement
- Text input: `KeycodeAdder.cs` component handles character insertion

### Texture Blending System
Located in `Assets/LiquidForce/TextureBlender/`:
- **TextureBlender**: Reusable MonoBehaviour component for GPU-accelerated texture blending, removes 8-texture hard limit via Texture2DArray
- **Performance**: Target <5ms for 4×2048² textures on RTX 3070, <2ms for cached repeat blends, <3ms for VR-optimized (1024×1024)
- **Rotation**: Per-texture rotation (0-360°) with zero-overhead optimization when unused (cached zero arrays, 98% faster), automatic UV tiling/wrapping for seamless rotated textures, ideal for terrain variation and normal map coherence
- **Blend Modes**: Additive (fastest, 30% faster than alpha), AlphaWeighted (respects texture alpha), Multiplicative (masking/darkening)
- **Normal Maps**: `BlendNormalsWithBaseAlpha()` blends normals with per-pixel alpha weighting, supports rotation for visual coherence with base textures
- **Resource Management**: Automatic pooling for RenderTextures and ComputeBuffers, Texture2DArray caching for repeat blends (35% speedup)
`- **API**: `BlendTextures()` (basic with optional rotation), `BlendTextures(..., rotations, offsets)` (full control with rotation and UV offset), `BlendTexturesAsync()` (non-blocking with UniTask), `BlendToExistingTexture()` (fastest - no allocation), `BlendNormalsWithBaseAlpha()` (normal map support with rotation), `BatchBlend()` (multiple operations)
- **Compute Shader**: `TextureBlenderComputeShader.compute` with kernels for each blend mode and normal blending, uses [numthreads(8,8,1)] for optimal GPU occupancy on RTX series, custom sampler (`sampler_linear_repeat`) with Wrap mode for seamless UV tiling during rotation
- **VR Compatible**: Writes to both RWTexture2D and RWStructuredBuffer for OpenGL ES 3.0 support (Quest/Pico)
- **Configuration**: Enable array caching and texture pooling in Inspector for maximum speed, FastMode to skip validation
- **Profiler Markers**: `TextureBlender.ConvertToArray`, `TextureBlender.Dispatch`, `TextureBlender.AllocateResources`, `TextureBlender.CacheCheck`
- **Examples**: `TextureBlenderExample.cs` shows usage patterns including rotation, `TextureBlenderBenchmark.cs` for performance testing
- **Documentation**: See `Assets/LiquidForce/TextureBlender/Documentation/` folder for complete guides (README, API_REFERENCE, ARCHITECTURE, QUICK_START, etc.) or `TEXTURE_BLENDING_SYSTEM.md` in project root
- **PDF Documentation**: `TextureBlender_Architecture.pdf` in Documentation folder - comprehensive architecture guide with diagrams (generate with Pandoc+XeLaTeX)
- Legacy: `ImageProcessorTest.cs` is deprecated (8-texture limit), marked with `[Obsolete]` attribute

### Ace of Ages Game Systems
Located in `Assets/_App/Ace of Ages/`:
- **Terrain System** (`Terrain/`): DOTS-based infinite terrain with procedural generation using multi-octave Perlin noise, parallel Burst-compiled mesh generation, LOD physics colliders with LRU caching, camera-aware prioritization, and optional directional auto-scrolling (see `Terrain/ARCHITECTURE.md`)
- **Terrain Core Systems**: 
  - `PlayerTrackingInitSystem`: Finds and assigns player Transform reference at runtime (runs in `InitializationSystemGroup`), searches via `PlayerTrackingSearch` component with modes: FindByName, FindByTag, FindAutoHandPlayer, FindMainCamera
  - `ScrollTerrainSystem`: Updates `ScrollOffset` each frame in player's forward direction (XZ plane projection) when auto-scroll enabled
  - `TileSpawningSystem`: Spawns/despawns tiles in ring around player using `NativeParallelHashMap<int2, Entity>` to track active tiles, applies scroll offset to tile positions
  - `TileScrollPositionSystem`: Updates all existing tile positions each frame based on `ScrollOffset` (ensures smooth scrolling)
  - `TerrainDistanceTrackingSystem`: Calculates distance to player and LOD level for each tile, runs before physics system
  - `TerrainMeshGenerationSystem`: Parallel Burst jobs with `IJobParallelFor` for vertex/normal generation, camera-aware priority sorting, frame budgeting via `NativeQueue<Entity>` (processes up to `maxCollidersCreatedPerFrame` tiles/frame)
  - `TerrainColliderPreparationSystem`: Burst-compiled job for LOD decimation (1x/2x/4x vertex stride), calculates camera-aware priority, schedules parallel jobs
  - `TerrainPhysicsSystem`: Main-thread `MeshCollider.Create()` with LRU cache (`NativeHashMap<ColliderCacheKey, ColliderCacheEntry>`), frame budgeting, cache eviction when memory threshold exceeded
  - `TerrainRenderingSystem`: Converts DynamicBuffers to Unity Mesh instances, sets up `RenderMesh` component, runs in `PresentationSystemGroup`, handles material loading from Resources ("TerrainMaterial")
- **DOTS Systems**: ECS performance-critical systems including:
  - `TransformFollowerSystem` (`TransformFollower/`): Makes DOTS entities follow GameObject Transforms outside subscenes using managed `TransformReference` component, runs on main thread via `.Run()` (cannot use Burst/Jobs due to managed references)
  - `SplineFollowerSystem` (`Splines/`): Moves entities along Unity.Splines with formation support via Burst-compiled job, uses `SplineDataComponent` (with pre-sampled `BlobAssetReference<SplineDataBlob>`) and `FormationPosition`
  - `EnemySpawnerSystem` (`EnemySpawner/`): Spawns entities in bowling pin formations along splines via `EnemySpawner` component, uses `CalculateBowlingPinPosition()` for 10-pin layout with hexagonal lateral spacing
  - `ResetEventsSystem`: Resets event flags (e.g., `doSpawn`) each frame, runs before `EnemySpawnerSystem` via `[UpdateBefore]` attribute
  - `TransformFollowerInitSystem` (`TransformFollower/`): Initializes Transform references at runtime (runs in `InitializationSystemGroup`), searches for targets via `TransformFollowerTargetSearch` component
- **Terrain Components**:
  - `TerrainTileConfig`: Singleton with tile size, view distance, vertices per side, noise parameters (frequency/amplitude/octaves/lacunarity/persistence), physics LOD thresholds, cache memory limit
  - `TerrainTile`: Grid coordinate, mesh generation flags (`meshGenerated`, `needsRegeneration`)
  - `ScrollOffset`: Singleton with `accumulatedOffset` (float3) for directional auto-scrolling (locked to XZ plane)
  - `ScrollConfig`: Singleton with `enabled` flag and `scrollSpeed` for terrain auto-scrolling
  - `PlayerTransformReference`: Managed singleton holding player Transform reference for terrain tracking
  - `PlayerTrackingSearch`: Runtime search configuration (FindByName/FindByTag/FindAutoHandPlayer/FindMainCamera modes)
  - `TerrainTileDistanceToPlayer`: Distance to player and current `TerrainPhysicsLODLevel` (FullResolution/HalfResolution/QuarterResolution/NoCollider)
  - `PhysicsColliderValid`: Tag indicating collider is up-to-date and cached
  - DynamicBuffers: `VertexElement`, `NormalElement`, `UVElement`, `IndexElement` for mesh data; `ColliderPreparedVertexElement`, `ColliderPreparedTriangleElement` for physics data
- **Authoring Components**: Co-located with systems in subdirectories - `TransformFollowerAuthoring`, `SplineFollowerAuthoring`, `EnemySpawnerAuthoring`, `PlayerTagAuthoring` (in `Player/`), `FormationPositionAuthoring`, `PrefabEntitiesReferencesAuthoring`
- **Cross-Subscene References**: `TransformFollowerAuthoring` uses `TransformFollowerTargetSearch` component with `FindByName`, `FindByTag`, or `DirectReference` modes to locate targets at runtime, initialized by `TransformFollowerInitSystem` since `MonoBehaviour.Start()` doesn't run in baked SubScenes
- **Managed Components**: `TransformReference` is a managed `IComponentData` class (not struct) bridging GameObject/Transform references to ECS
- Entry point: `AceOfAges.cs` test component triggers enemy spawns after 3-second delay using EntityQuery

### Camera & Scene Transitions (LiquidForce namespace)
- **CameraFader**: Creates inverted sphere mesh around camera with custom shader, uses DOTween for async fade animations
- **DeviceTracking**: Manages VR tracking origin, provides head-following via `ObjectFollower` component, includes `UpdateImmediate()` for instant sync
- **ObjectFollower**: Smoothly lerps transform to follow targets with configurable speed/offsets, supports multiple update timings (OnUpdate, OnFixedUpdate, OnLateUpdate, OnPreRender, OnPreCull), includes `UpdateImmediate()` to snap targets to source instantly and force re-positioning by calling internal `UpdateTargetTransforms()`
- **SceneLoader**: Handles both Addressable and standard scene loading with camera fade coordination, supports `isAddressable` flag in `SceneListSO.SceneListScene` for mixed scene types
- **UICamera**: Manages camera culling masks to separate UI layer rendering from main scene, toggles UI camera active state via `OnUIVisible(bool)`, used by `UIManager`

### UI State Machine System
Located in `Assets/_App/Scripts/UI/`:
- **UIManager**: Stack-based state machine with `PushState()`, `PopState()`, `PushModal()`, `PopModalPush()` methods for navigation
- **IUIState**: Interface defining lifecycle methods: `OnEnter()`, `OnExit()`, `OnPushed()`, `OnModalPushed()`, `OnPopped()`
- **UIState**: Base MonoBehaviour implementing IUIState with default GameObject activation/deactivation, includes `stateName` property for debugging
- **BreadcrumbUI**: Displays current state stack as text breadcrumb trail using `UIManager.GetStackNames()` for navigation debugging
- **State Management**: States tracked via `Stack<IUIState>`, use `uiManager.GetStackNames()` to inspect current state hierarchy
- **Integration**: `UIManager` requires `ObjectFollower` component (head following), `UICamera` for camera culling management, calls `objectFollower.UpdateImmediate()` on Show() to snap UI to head position
- Pattern: Create UIState subclasses in child GameObjects, set `startState` in Inspector to initialize UI on Start

### Scene Loading
Three parallel systems:
1. **Addressables** (legacy): `UI.cs` in `App.StartScene` namespace uses `Addressables.LoadSceneAsync()`, waits for camera fade before activation via coroutine
2. **Modern Scene Loading**: `LiquidForce.SceneLoader` handles both Addressable and standard scenes, `SceneSelectUIState` uses this for scene transitions
3. **ECS SubScenes**: `SubSceneLoader.Instance.LoadScene(subScene.SceneGUID)` via `Unity.Scenes.SceneSystem.LoadSceneAsync(World.Unmanaged, sceneGUID)`
4. Entry point: `SceneStartup.cs` sets tracking origin with `UpdateImmediate()` call, fades camera, loads SubScenes from array via `SubSceneLoader.Instance`, then destroys itself

## Development Workflows

### Building & Testing
- Project uses Unity 6 (6000.3.10f1) with URP 17.3.0
- VR: OpenXR (1.16.1), XR Hands (1.7.3), XR Interaction Toolkit (3.3.1)
- Entry scene: `Assets/_App/Start Scene/Start Scene.unity`
- Test scenes: `Assets/_App/Test Scenes/` (KeyboardTest.unity, UIManager Test/)
- Ace of Ages scene: `Assets/_App/Ace of Ages/Ace of Ages.unity` (DOTS terrain demo with subscenes)

### Adding New VR Interactable
1. Add `Grabbable` component to GameObject with Rigidbody
2. Set `HandGrabType` (Default/HandToGrabbable/GrabbableToHand)
3. Override `CanGrab(HandBase hand)` for conditional grabbing
4. Use events: `OnGrab(Hand hand)`, `OnRelease(Hand hand)` for custom behavior
5. Configure physics: joint break force, parent on grab, single hand only settings

### Extending Word Prediction
- Dictionaries stored in `Assets/Resources/WordPrediction/` (biGramDict.txt, levenshteinDict.txt)
- To regenerate: Uncomment dictionary generation code in `NGramGenerator.Awake()`, provide corpus in Resources as "Sample" TextAsset
- UI labels: Set `ButtonLabels` array in Inspector to TextMeshPro text components for predictions

### Building UI State Machines
1. Create new UIState subclass extending `UIState` MonoBehaviour
2. Override lifecycle methods: `OnEnter()` (show), `OnExit()` (hide), `OnPushed()` (paused by new state), `OnPopped()` (resumed)
3. Assign `uiManager` reference in Inspector
4. Call `uiManager.PushState(newState)` for navigation, `uiManager.PopState()` to go back
5. Use `uiManager.PushModal(modalState)` for overlays that don't hide previous state
6. Example: `Assets/_App/Scripts/UI/Scene Select/` contains scene selection state implementation

### Async Operations
- Uses **UniTask** (Cysharp.UniTask) for async/await in Unity
- DOTween integration: `.WithCancellation(token)` extension for cancellable animations
- Pattern: Store `CancellationTokenSource[]` arrays, cancel/dispose on state changes (see `UI.cs` or `UIManager.cs` for reference)

### Working with Terrain System
Located in `Assets/_App/Ace of Ages/Terrain/`:

**Configuration** (`TerrainConfigAuthoring`):
- Set player search mode: AutoDetect/FindByName/FindByTag/FindAutoHandPlayer/FindMainCamera
- Tile settings: `tileSize` (100m), `viewDistance` (500m), `verticesPerSide` (32)
- Auto-scroll: Enable via `scrollEnabled`, set `scrollSpeed` (5.0 m/s), scrolls in player's facing direction (XZ plane)
- Noise params: `noiseFrequency` (0.01), `noiseAmplitude` (20), `noiseOctaves` (4), `noiseLacunarity` (2.0), `noisePersistence` (0.5)
- Physics LOD: `maxCollidersCreatedPerFrame` (3), distance thresholds for LOD levels, `maxColliderCacheMemoryMB` (50)

**Debugging Tools**:
- `TerrainTrackingDebugger`: Attach to any GameObject, use context menu "Check Tracking Status" to verify player reference, shows GUI overlay in play mode
- Editor window: Window → Terrain → Status Inspector (checks material, URP config, entity counts)
- Profiler markers: `TerrainMesh.Generation`, `TerrainPhysics.ColliderCreation`, `TerrainMesh.PrioritySort` (monitor to ensure <5ms per frame)

**Performance Tuning**:
- High-end VR (RTX 4080+): Set `maxCollidersCreatedPerFrame` to 5-8
- Mid-range VR (RTX 3070): Keep at 3-4
- Low-end VR (Quest 2): Set to 1-2
- Increase `verticesPerSide` for more detail (32→64), reduces physics LOD decimation effectiveness
- Material must exist in Resources as "TerrainMaterial" (URP/Lit shader recommended)

**Zero-GC Pattern for ECS**:
- Use `SystemAPI.Query<>().WithEntityAccess()` for direct iteration (no `ToEntityArray()`)
- Collect entities in `NativeList<Entity>(Allocator.Temp)` when structural changes needed
- Phase 1: Collect entities during query iteration (no AddComponent/RemoveComponent calls)
- Phase 2: Process collected entities after iteration completes (structural changes allowed)
- Always dispose temp collections: `nativeList.Dispose()`

## Project-Specific Conventions

### Component Initialization Order
- ECS systems: `SubSceneLoader` extends `SystemBase` and sets `Instance` in `OnCreate()`
- AutoHand: Uses explicit `[DefaultExecutionOrder(10)]` for Hand, `[-100]` for Grabbable
- Singletons initialize in Awake(), register Instance, check for duplicates and Destroy if found
- `SceneStartup` destroys its own GameObject after fade-in completes

### ScriptableObject Configuration
- `SceneListSO`: Two versions exist:
  - `LiquidForce.SceneListSO`: Uses `sceneDisplayName`, `isAddressable` flag, `scenePath`, `AssetReference scene` - supports mixed Addressable/standard scenes
  - Root namespace version (legacy): Uses only `sceneName` and `AssetReference scene` for Addressables only
- `AutoHandSettings`: Stores setup wizard config, loaded from Resources ("AutoHandSettings")
- Pattern: Create via Assets menu (`[CreateAssetMenu]` attribute), assign in Inspector

### Layer Usage
- "UI" layer: Used for CameraFader sphere and UI elements, UICamera culls "UI" and "Hand" layers together
- "Hand" layer: AutoHand sets hand colliders recursively to left/right hand layers, UICamera includes in culling mask
- AutoHand layers: "Grabbable", "Grabbing", "HandPlayer" - see `AutoHandSetupWizard.cs` for layer collision matrix setup
- Physics interactions expect default layer setup, AutoHand uses layer masks extensively

### Namespace Organization
- Global namespace: Utilities, keyboard, word prediction (legacy code), UIManager/UIState system, DOTS components/systems
- `Autohand`: All VR hand interaction code
- `LiquidForce`: Custom utilities (camera, tracking, following, scene loading)
- `App.StartScene`: Legacy scene selection UI (coexists with modern `SceneSelectUIState`)

### Code Style Notes
- Singletons use `static Instance` property with private set, initialized in `Awake()` or `OnCreate()` for ECS systems
- Async methods return `UniTask` or `IEnumerator` for coroutines
- Heavy use of `?.` null-conditional operators for null safety
- SerializeField with `[field: SerializeField]` property syntax in newer code (auto-properties)
- DOTS components in global namespace (no assembly definitions for custom ECS code)

## External Dependencies
- **UniTask**: Async/await replacement for Unity coroutines
- **DOTween**: Animation tweening library (fadeCameraIn/Out, UI show/hide)
- **AutoHand**: Third-party VR interaction framework (Assets/AutoHand/)
- **NaughtyAttributes**: Custom inspector attributes (within AutoHand)
- **TextMeshPro**: Text rendering (Unity package)

## Common Pitfalls
- Physics timestep critical for AutoHand: Don't modify `Time.fixedDeltaTime` without checking AutoHandSettings
- CameraFader requires "LiquidForce/CameraFader" shader in Resources
- SubScenes must be added to `SceneStartup.subScenes[]` array to load at startup
- Addressable scenes must be marked in Addressables groups before `AssetReference` works
- Word prediction requires pre-generated dictionaries or will fail silently if corpus generation commented out
- `UIManager` requires `ObjectFollower` component - add via `[RequireComponent]` attribute (already present)
- State machine navigation: Always use `PushState()`/`PopState()` - direct GameObject activation bypasses lifecycle callbacks
- **InputSystem.actions**: Scenes using `InputSystem.actions.FindAction()` must include `InputSystemActionsInitializer` component or configure Project-Wide Actions. UIManager auto-detects missing actions and logs warnings but won't function without proper initialization.
- `DeviceTracking.Instance.UpdateImmediate()` must be called after tracking origin changes to sync head followers immediately
- `ObjectFollower.UpdateImmediate()` forces instant snap without smoothing - call after Show()/position changes to prevent UI from being visible during transition
- DOTS `TransformFollowerAuthoring` must be on entities inside SubScenes - runtime init won't work for non-baked entities
- `TransformFollowerSystem` uses `.Run()` instead of `.Schedule()` because it accesses managed Transform references (Burst incompatible)
- `SplineDataComponent` stores pre-sampled spline data as BlobAsset - configure `sampleCount` in `SplineComponentAuthoring` for accuracy vs memory tradeoff
- **Terrain System**: Floating origin system removed - player should stay within ~1000-2000m of world origin for best float precision
- **Zero-GC Pattern**: Never use `query.ToEntityArray()` - use direct iteration with `SystemAPI.Query<>().WithEntityAccess()` or collect in `NativeList<Entity>` to avoid managed allocations
- **Structural Changes**: Avoid `EntityManager.AddComponent()` during query iteration - collect entities in `NativeList` first, then process after iteration completes
- **Terrain Auto-Scroll**: `ScrollOffset` is directional (float3) based on player's forward direction projected to XZ plane - not just Z-axis
- **Camera Prioritization**: Mesh/physics generation prioritizes tiles in camera's forward direction - both systems calculate priority using dot product with camera forward
- **Material Loading**: Terrain requires "TerrainMaterial" in Resources folder or will generate fallback material at runtime (use Editor tool: Window → Terrain → Create Material)
- **Formation Movement**: `FormationMovementSystem` integrates with terrain scroll velocity - entities compensate for scrolling during approach/exit phases to maintain correct world positions
- **Movement Phase Transitions**: Threshold-based state machine - entities automatically progress through phases based on distance checks, no manual state management required
- **EnemySpawner Positioning**: Spawn point calculated perpendicular to spline start (Z-axis direction), using player position for off-camera placement
