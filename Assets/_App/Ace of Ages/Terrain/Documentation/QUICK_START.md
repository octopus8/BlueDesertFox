# Quick Start Guide - Infinite Terrain System
**Version:** 3.0  
**Last Updated:** May 4, 2026

Get the terrain system running in your scene in under 10 minutes!

## Prerequisites

✅ Unity 6 (6000.3.10f1 or later)  
✅ Unity DOTS packages installed (Entities, Physics, Burst, Mathematics)  
✅ A scene with a player GameObject (VR rig, camera, or player controller)

## Step 1: Add Terrain Configuration (2 minutes)

### Create the Config GameObject

1. In your scene hierarchy, create a new empty GameObject
2. Name it `TerrainConfig`
3. Add the `TerrainConfigAuthoring` component

### Configure Basic Settings

In the Inspector:

**Player Tracking:**
- Player Search Mode: `AutoDetect`

**Tile Settings:**
- Tile Size: `100`
- View Distance: `500`
- Vertices Per Side: `32`

**Procedural Noise:**
- Noise Frequency: `0.01`
- Noise Amplitude: `20`
- Noise Octaves: `4`

**Auto-Scrolling:**
- Scroll Enabled: `false`

**Physics:**
- Max Colliders Per Frame: `3`

## Step 2: Convert to SubScene (3 minutes)

1. Right-click `TerrainConfig` in Hierarchy
2. Select `New SubScene From Selection`
3. Name it `TerrainSubScene`
4. Save in `Assets/_App/Ace of Ages/Terrain/`

⚠️ **Important**: TerrainConfig MUST be in a SubScene!

## Step 3: Verify Player GameObject (1 minute)

Ensure you have one of:
- ✅ AutoHandPlayer component (VR)
- ✅ Camera tagged as "MainCamera"
- ✅ GameObject named correctly
- ✅ GameObject with "Player" tag

## Step 4: Play the Scene (1 minute)

Press Play and watch terrain generate!

### Expected Output

Console should show:
```
[PlayerTrackingInitSystem] ✅ Found player: XR Origin Hands (XR Rig)
```

Terrain should appear around player.

## Step 5: (Optional) Add Debug Visualization (2 minutes)

1. Create GameObject named `TerrainDebug`
2. Add `TerrainTileGizmoVisualizer` component
3. Enable "Draw Tile Bounds" and "Draw Grid Coordinates"
4. Add `TerrainTrackingDebugger` component
5. Right-click → "Check Tracking Status"

## Step 6: (Optional) Enable Auto-Scrolling (1 minute)

1. Open TerrainConfigAuthoring
2. Enable `Scroll Enabled`
3. Set `Scroll Speed`: `5.0`
4. Play - terrain scrolls forward!

## Troubleshooting

### No Tiles Spawning

- Check console for player tracking errors
- Try Player Search Mode: `FindMainCamera`
- Verify TerrainConfig is in SubScene

**See**: [Troubleshooting Guide](TROUBLESHOOTING.md)

### Terrain Not Visible

- Add TerrainTileGizmoVisualizer
- Check camera far clip plane > 500m
- Verify material exists in Resources

### Player Tracking Fails

- Use TerrainTrackingDebugger → Check Tracking Status
- **See**: [Player Tracking Setup](PLAYER_TRACKING.md)

## Next Steps

- **[Configuration Reference](CONFIGURATION.md)** - Tune appearance
- **[System Overview](SYSTEM_OVERVIEW.md)** - Learn architecture
- **[Auto-Scrolling Guide](AUTO_SCROLLING.md)** - Set up endless runner

---

**Back to**: [Documentation Hub](README.md)

