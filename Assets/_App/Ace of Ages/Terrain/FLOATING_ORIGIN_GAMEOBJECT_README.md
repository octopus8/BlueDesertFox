# Floating Origin GameObject Integration

## Overview
This system extends the ECS-based floating origin system to synchronously shift GameObjects (like XR Origin) alongside ECS entities, preventing visual artifacts during world origin shifts.

## Components

### FloatingOriginEvents.cs
Static event manager that bridges ECS systems and MonoBehaviour components. Provides the `OnOriginShifted` event that fires when the world origin shifts.

### FloatingOriginSystem.cs (Modified)
- Changed from `.ScheduleParallel()` to `.Run()` for synchronous execution
- Removed `[BurstCompile]` from `OnUpdate()` to allow event invocation
- Invokes `FloatingOriginEvents.InvokeOriginShifted()` after shifting ECS entities

### FloatingOriginGameObjectShifter.cs
MonoBehaviour component that:
- Subscribes to `FloatingOriginEvents.OnOriginShifted` in `OnEnable()`
- Shifts configured GameObjects by subtracting the same offset applied to ECS entities
- Automatically uses `DeviceTracking.Instance.TrackingOrigin` if no transforms specified
- Calls `DeviceTracking.Instance.UpdateImmediate()` after shift to snap UI/camera followers

## Usage

### Setup in Scene
1. Add `FloatingOriginGameObjectShifter` component to a persistent GameObject in your scene (e.g., same GameObject with `SceneStartup`)
2. Configure options in Inspector:
   - **Transforms To Shift**: Leave empty to auto-use `DeviceTracking.Instance.TrackingOrigin`, or manually assign transforms
   - **Update Device Tracking Immediate**: Keep checked (default) to snap UI followers instantly
   - **Debug Log**: Enable to see shift events in console during testing

### Example Scene Setup
```
Scene GameObject Hierarchy:
├── Scene Manager (persistent GameObject)
│   ├── SceneStartup (destroys itself after startup)
│   └── FloatingOriginGameObjectShifter (stays active)
└── XR Origin Hands (XR Rig)
    └── ... (camera, hands, etc.)
```

## How It Works

1. **FloatingOriginSystem** monitors player distance from origin in `OnUpdate()`
2. When distance exceeds threshold, it runs `ShiftWorldOriginJob.Run()` synchronously to shift ECS entities
3. After job completes, it invokes `FloatingOriginEvents.InvokeOriginShifted(offset)`
4. **FloatingOriginGameObjectShifter** receives the event and subtracts the same offset from configured GameObjects
5. It calls `DeviceTracking.Instance.UpdateImmediate()` to snap followers (prevents lerp artifacts)
6. All operations happen synchronously in the same frame - no visual glitches!

## Performance Notes
- Changed from parallel job execution to `.Run()` for synchronous execution
- This ensures GameObjects and entities shift in the same frame
- Minimal performance impact since shifts only occur when player travels far from origin (e.g., every 2000+ meters)

## Testing
Enable **Debug Log** in the Inspector to see:
- When origin shifts occur
- Which GameObjects are being shifted
- Whether `DeviceTracking.UpdateImmediate()` is being called

Move the player far from origin (beyond shift threshold) to trigger a shift event.

