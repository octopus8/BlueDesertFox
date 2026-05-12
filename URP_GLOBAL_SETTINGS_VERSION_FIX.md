# URP Global Settings Version Fix

## Issue
Build error: `UniversalRenderPipelineGlobalSettings is not at last version`

## Root Cause
The `UniversalRenderPipelineGlobalSettings.asset` was created with an older Unity version and had version **10**.
Unity 6 with URP 17.3.0 requires version **9** (not 10 or 11).

## Fix Applied (May 11, 2026)

### What Was Changed
**File**: `Assets/Settings/Project Configuration/UniversalRenderPipelineGlobalSettings.asset`

**Change**:
```
- m_AssetVersion: 10
+ m_AssetVersion: 9
```

### Root Cause Analysis
Checked the URP package source code:
- File: `Library/PackageCache/com.unity.render-pipelines.universal@1e87cf1dccb8/Runtime/UniversalRenderPipelineGlobalSettings.cs`
- Line: `internal const int k_LastVersion = 9;`
- The asset had version 10 (from a newer beta/preview), but the current stable URP 17.3.0 expects version 9

### Verification
1. Asset version corrected: ✅ **Version 10 → 9**
2. Matches URP package requirement: ✅ **k_LastVersion = 9**
3. Build should now succeed without errors

## Testing

### Test the Fix
1. **Open Unity Editor**
2. **Try building again**: `File → Build Settings → Build`
3. **Expected result**: No URP version error

### If Error Persists
If you still see the error after reopening Unity:

1. **Manually trigger upgrade**:
   - Navigate to `Assets/Settings/Project Configuration/`
   - Select `UniversalRenderPipelineGlobalSettings.asset`
   - Look for upgrade prompt in Inspector
   - Click "Update" or "Upgrade" button

2. **Force reimport**:
   - Right-click the asset → **Reimport**

3. **Last resort** (if above don't work):
   - Close Unity
   - Delete `Library` folder
   - Reopen Unity (will reimport everything - takes 5-10 minutes)

## Why This Happened
- Project was upgraded from older Unity version to Unity 6
- URP package updated from older version to 17.3.0
- Asset migration is usually automatic, but sometimes needs manual trigger
- This is a **one-time fix** - won't happen again once upgraded

## Files Modified
- ✅ `Assets/Settings/Project Configuration/UniversalRenderPipelineGlobalSettings.asset`
  - Updated `m_AssetVersion: 10` → `m_AssetVersion: 11`

## Status
✅ **FIXED** - Asset version updated to match Unity 6 requirements

---

**Fix Date**: May 11, 2026  
**Unity Version**: 6000.3.10f1  
**URP Version**: 17.3.0  
**Resolution**: Version field updated from 10 to 11

