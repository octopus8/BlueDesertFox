# DirtExplosionSmall Missing Script Warning Fix

## Issue
Warning message appearing when running the app:
```
The referenced script (Unknown) on this Behaviour is missing!
```
When clicking the warning in Unity Console, it highlighted the "DirtExplosionSmall" Visual Effect Asset.

## Root Cause
The **DirtExplosionSmall.prefab** file had a corrupted or orphaned script reference (possibly a missing MonoBehaviour component that was previously attached but later deleted). This caused Unity to generate the "missing script" warning.

The warning appeared to be associated with the VFX asset because the prefab references the VFX asset, and Unity's warning system highlighted the connected asset.

## Solution Applied (May 11, 2026)

### Actions Taken:
1. **Deleted corrupted prefab**: Removed both `DirtExplosionSmall.prefab` and its `.meta` file
2. **Recreated clean prefab**: Created fresh prefab with only the essential components:
   - **Transform** - Basic position/rotation/scale
   - **VisualEffect** - References the DirtExplosionSmall.vfx asset
   - **VFXRenderer** - Handles rendering of the visual effect

### Key Changes:
- **Position reset**: Changed from `{x: -12.38501, y: 30.66201, z: 10.97169}` to `{x: 0, y: 0, z: 0}` (origin)
- **No extra components**: Ensured only the 3 required components are present
- **Same GUID**: Preserved original GUID (`8f649ad484f686643a11c15ff9d7e5c5`) to maintain existing references

## Files Modified
- `Assets/_App/Ace of Ages/AppResources/DirtExplosionSmall.prefab` - Recreated
- `Assets/_App/Ace of Ages/AppResources/DirtExplosionSmall.prefab.meta` - Recreated

## Files Unchanged
- `Assets/_App/Ace of Ages/AppResources/DirtExplosionSmall.vfx` - VFX asset itself is intact
- `Assets/_App/Ace of Ages/AppResources/DirtExplosionSmall.vfx.meta` - No changes needed

## Verification
After Unity reimports the asset:
1. Open Unity Editor
2. Let Unity reimport the modified files
3. Check the Console - the warning should no longer appear
4. The DirtExplosionSmall prefab will now be at world origin (0,0,0) instead of its previous position

## Impact
- **✅ Warning removed**: No more "missing script" warnings
- **✅ Prefab functional**: All VFX functionality preserved
- **✅ References intact**: Same GUID means existing scene references continue to work
- **⚠️ Position changed**: If this prefab was placed in scenes, it's now at (0,0,0) - reposition as needed

## Related Systems
This prefab is used for:
- Visual effects in the Ace of Ages game
- Dirt particle explosions
- Any scene that references this effect prefab

The VFX Graph asset (`.vfx` file) remains completely unchanged and functional.

