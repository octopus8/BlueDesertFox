# AI Agent Guide for BlueDesertFox

## Project Overview
Unity VR application combining traditional MonoBehaviour components with Unity DOTS (ECS). Uses AutoHand for VR hand interactions, custom word prediction system, and hybrid scene management via Addressables and ECS SubScenes.

## Architecture

### Hybrid Unity Architecture
- **MonoBehaviour Layer**: VR interactions (AutoHand), UI, keyboard input, word prediction
- **ECS Layer**: Performance-critical systems loaded via SubScenes (see `Assets/_App/Scripts/ECSManagedSystems/`)
- **Scene Management**: `SceneStartup.cs` orchestrates initial setup, loading SubScenes via `SubSceneLoader` singleton and managing camera fade-ins
- **UI System**: State machine pattern via `UIManager` with stack-based state management (`IUIState`, `UIState`)

### Key Namespaces & Assembly Definitions
- `Autohand` (AutoHandAssembly.asmdef): VR hand/grabbable interactions
- `LiquidForce` (LiquidForce.asmdef): Camera fading, device tracking, object following utilities
- `App.StartScene`: Scene selection UI and Addressables-based scene loading (legacy pattern, coexists with UIManager)

### Singleton Pattern Usage
Project relies on several Singleton patterns:
- `DeviceTracking.Instance` - VR tracking origin management (LiquidForce), includes `UpdateImmediate()` for instant head follower sync
- `CameraFader.Instance` - Screen fade transitions (LiquidForce)
- `SubSceneLoader.Instance` - ECS SubScene loading system
- `AutoHandPlayer.Instance` - VR player controller (Autohand namespace), uses `_Instance` backing field with lazy initialization
- `BLeeDev.instance` - Development testing utilities (lowercase 'i')

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

### Ace of Ages Game Systems
Located in `Assets/_App/Ace of Ages/`:
- **Terrain System** (`Terrain/`): DOTS-based infinite terrain with floating origin, procedural generation using Perlin noise, automatic mesh collider generation (see `Terrain/README.md`)
- **DOTS Systems** (`DOTSSystems/`): ECS performance-critical systems including:
  - `TransformFollowerSystem`: Makes DOTS entities follow GameObject Transforms outside subscenes using managed `TransformReference` component
  - `SplineFollowerSystem`: Moves entities along Unity.Splines with formation support, uses `SplineDataComponent` and `FormationPosition`
  - `EnemySpawnerSystem`: Spawns entities in bowling pin formations along splines via `EnemySpawner` component
  - `TransformFollowerInitSystem`: Initializes Transform references at runtime (runs in `InitializationSystemGroup`)
- **Authoring Components** (`DOTSAuthoring/`): `TransformFollowerAuthoring`, `SplineFollowerAuthoring`, `EnemySpawnerAuthoring`, `PlayerTagAuthoring`, `FormationPositionAuthoring`
- **Cross-Subscene References**: `TransformFollowerAuthoring` uses `TransformFollowerTargetSearch` component with `FindByName`, `FindByTag`, or `DirectReference` modes to locate targets at runtime
- Entry point: `AceOfAges.cs` test component triggers enemy spawns after delay

### Camera & Scene Transitions (LiquidForce namespace)
- **CameraFader**: Creates inverted sphere mesh around camera with custom shader, uses DOTween for async fade animations
- **DeviceTracking**: Manages VR tracking origin, provides head-following via `ObjectFollower` component, includes `UpdateImmediate()` for instant sync
- **ObjectFollower**: Smoothly lerps transform to follow targets with configurable speed/offsets, supports multiple update timings (OnUpdate, OnFixedUpdate, OnLateUpdate, OnPreRender, OnPreCull), includes `UpdateImmediate()` to snap targets to source instantly and force re-positioning
- **SceneLoader**: Handles both Addressable and standard scene loading with camera fade coordination (LiquidForce namespace)

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
Two parallel systems:
1. **Addressables** (traditional scenes): `UI.cs` in Start Scene uses `Addressables.LoadSceneAsync()`, waits for camera fade before activation
2. **ECS SubScenes**: `SubSceneLoader.Instance.LoadScene(subScene.SceneGUID)` via `Unity.Scenes.SceneSystem.LoadSceneAsync()`
3. Entry point: `SceneStartup.cs` sets tracking origin, fades camera, loads SubScenes from array, then destroys itself

## Development Workflows

### Building & Testing
- Project uses Unity 6 (2023.3+) with URP 17.3.0
- VR: OpenXR (1.16.1), XR Hands (1.7.3), XR Interaction Toolkit (3.3.1)
- Entry scene: `Assets/_App/Start Scene/Start Scene.unity`
- Test scenes: `Assets/_App/Test Scenes/` (KeyboardTest, PhysicsTest, UI Dev)

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

## Project-Specific Conventions

### Component Initialization Order
- ECS systems: `SubSceneLoader` extends `SystemBase` and sets `Instance` in `OnCreate()`
- AutoHand: Uses explicit `[DefaultExecutionOrder(10)]` for Hand, `[-100]` for Grabbable
- Singletons initialize in Awake(), register Instance, check for duplicates and Destroy if found
- `SceneStartup` destroys its own GameObject after fade-in completes

### ScriptableObject Configuration
- `SceneListSO`: Holds Addressable scene references (LiquidForce version uses `sceneDisplayName`, `isAddressable`, `scenePath`; root version uses `sceneName`), uses `[CreateAssetMenu]` for editor creation
- `AutoHandSettings`: Stores setup wizard config, loaded from Resources ("AutoHandSettings")
- Pattern: Create via Assets menu or right-click in project, assign in Inspector

### Layer Usage
- "UI" layer: Used for CameraFader sphere and UI elements
- Physics interactions expect default layer setup, AutoHand uses layer masks extensively

### Namespace Organization
- Global namespace: Utilities, keyboard, word prediction (legacy code), UIManager/UIState system
- `Autohand`: All VR hand interaction code
- `LiquidForce`: Custom utilities (camera, tracking, following)
- `App.StartScene`: Scene management UI

### Code Style Notes
- Singletons use `static Instance` property with private set (except `BLeeDev.instance` which is lowercase and public)
- Async methods return `UniTask` or `IEnumerator` for coroutines
- Heavy use of `?.` null-conditional operators
- SerializeField with [field: SerializeField] property syntax in newer code

## External Dependencies
- **UniTask**: Async/await replacement for Unity coroutines
- **DOTween**: Animation tweening library (fadeCameraIn/Out, UI show/hide)
- **AutoHand**: Third-party VR interaction framework (Assets/AutoHand/)
- **NaughtyAttributes**: Custom inspector attributes (within AutoHand)
- **TextMeshPro**: Text rendering (Unity package)

## Common Pitfalls
- Physics timestep critical for AutoHand: Don't modify `Time.fixedDeltaTime` without checking AutoHandSettings
- CameraFader requires "LiquidForce/CameraFader" shader in Resources
- SubScenes must be added to `SceneStartup.subScenes[]` array to load
- Addressable scenes must be marked in Addressables groups before `AssetReference` works
- Word prediction requires pre-generated dictionaries or will fail silently if corpus generation commented out
- `UIManager` requires `ObjectFollower` component - add via RequireComponent or prefab structure
- State machine navigation: Always use `PushState()`/`PopState()` - direct GameObject activation bypasses lifecycle callbacks
- `DeviceTracking.Instance.UpdateImmediate()` must be called after tracking origin changes to sync head followers immediately
- `ObjectFollower.UpdateImmediate()` forces instant snap without smoothing - call after Show()/position changes to prevent UI from being visible during transition

