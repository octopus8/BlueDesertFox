# AI Agent Guide for BlueDesertFox

## Project Overview
Unity VR application combining traditional MonoBehaviour components with Unity DOTS (ECS). Uses AutoHand for VR hand interactions, custom word prediction system, and hybrid scene management via Addressables and ECS SubScenes.

## Architecture

### Hybrid Unity Architecture
- **MonoBehaviour Layer**: VR interactions (AutoHand), UI, keyboard input, word prediction
- **ECS Layer**: Performance-critical systems loaded via SubScenes (see `Assets/_App/Scripts/ECSManagedSystems/`)
- **Scene Management**: `SceneStartup.cs` orchestrates initial setup, loading SubScenes via `SubSceneLoader` singleton and managing camera fade-ins

### Key Namespaces & Assembly Definitions
- `Autohand` (AutoHandAssembly.asmdef): VR hand/grabbable interactions
- `LiquidForce` (LiquidForce.asmdef): Camera fading, device tracking, object following utilities
- `App.StartScene`: Scene selection UI and Addressables-based scene loading

### Singleton Pattern Usage
Project relies on several Singleton patterns:
- `DeviceTracking.Instance` - VR tracking origin management (LiquidForce)
- `CameraFader.Instance` - Screen fade transitions (LiquidForce)
- `SubSceneLoader.Instance` - ECS SubScene loading system
- `BLeeDev.instance` - Development testing utilities

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

### Camera & Scene Transitions (LiquidForce namespace)
- **CameraFader**: Creates inverted sphere mesh around camera with custom shader, uses DOTween for async fade animations
- **DeviceTracking**: Manages VR tracking origin, provides head-following via `ObjectFollower` component
- **ObjectFollower**: Smoothly lerps transform to follow targets with configurable speed/offsets, supports multiple update timings (OnUpdate, OnFixedUpdate, OnLateUpdate, OnPreRender)

### Scene Loading
Two parallel systems:
1. **Addressables** (traditional scenes): `UI.cs` in Start Scene uses `Addressables.LoadSceneAsync()`, waits for camera fade before activation
2. **ECS SubScenes**: `SubSceneLoader` system loads via `Unity.Scenes.SceneSystem.LoadSceneAsync()`
3. Entry point: `SceneStartup.cs` sets tracking origin, fades camera, loads SubScenes, then destroys itself

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

### Async Operations
- Uses **UniTask** (Cysharp.UniTask) for async/await in Unity
- DOTween integration: `.WithCancellation(token)` extension for cancellable animations
- Pattern: Store `CancellationTokenSource[]` arrays, cancel/dispose on state changes (see `UI.cs` for reference)

## Project-Specific Conventions

### Component Initialization Order
- ECS systems: `SubSceneLoader` uses `[DefaultExecutionOrder(-1)]` implicitly via SystemBase
- AutoHand: Uses explicit `[DefaultExecutionOrder(10)]` for Hand, `[-100]` for Grabbable
- Singletons initialize in Awake(), register Instance, check for duplicates and Destroy if found

### Layer Usage
- "UI" layer: Used for CameraFader sphere and UI elements
- Physics interactions expect default layer setup, AutoHand uses layer masks extensively

### Namespace Organization
- Global namespace: Utilities, keyboard, word prediction (legacy code)
- `Autohand`: All VR hand interaction code
- `LiquidForce`: Custom utilities (camera, tracking, following)
- `App.StartScene`: Scene management UI

### Code Style Notes
- Singletons use `static Instance` property with private set
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

