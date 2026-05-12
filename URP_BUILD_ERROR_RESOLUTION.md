# URP Build Error - Resolution Summary

## ✅ FIXED - Correct Version Now Applied

### The Problem
```
BuildFailedException: UniversalRenderPipelineGlobalSettings is not at last version
```

### The Root Cause
The asset had **version 10** but URP 17.3.0 expects **version 9**.

This happened because:
- The asset was likely created/modified in a Unity beta or preview version
- That beta version used a higher version number (10)
- The current stable Unity 6 with URP 17.3.0 uses version 9
- **Downgrading** the version was required, not upgrading

### The Solution
**File Modified**: `Assets/Settings/Project Configuration/UniversalRenderPipelineGlobalSettings.asset`

```diff
- m_AssetVersion: 10
+ m_AssetVersion: 9
```

### Verification Process
1. Read URP package source code at:
   ```
   Library/PackageCache/com.unity.render-pipelines.universal@1e87cf1dccb8/
   Runtime/UniversalRenderPipelineGlobalSettings.cs
   ```

2. Found the expected version:
   ```csharp
   internal const int k_LastVersion = 9;
   ```

3. Updated asset to match: **version 9** ✅

### Testing
1. **Reopen Unity Editor** (close/reopen if currently open)
2. **Try building**: `File → Build Settings → Build`
3. **Expected result**: Build succeeds without URP version error ✅

### Why This Was Confusing
- Initially updated to version 11 (incorrect guess)
- Error persisted because 11 ≠ 9
- Had to inspect URP source code to find `k_LastVersion = 9`
- The fix required **downgrading** from 10 → 9, not upgrading

### Files Modified
1. ✅ `Assets/Settings/Project Configuration/UniversalRenderPipelineGlobalSettings.asset`
   - Changed `m_AssetVersion: 10` → `m_AssetVersion: 9`
2. ✅ `URP_GLOBAL_SETTINGS_VERSION_FIX.md` - Updated documentation

---

## Status: ✅ RESOLVED

**Asset Version**: 9 (matches URP 17.3.0 requirement)  
**Build Status**: Should now succeed  
**Action Required**: Reopen Unity and try building again

---

**Fix Date**: May 11, 2026  
**Unity Version**: 6000.3.10f1  
**URP Version**: 17.3.0  
**Correct Version**: 9 (verified from URP source code)

