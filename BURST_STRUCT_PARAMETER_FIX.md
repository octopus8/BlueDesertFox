# Burst Struct Parameter and Return Type Fix - TerrainMeshGenerationSystem

## Issue 1: Struct Parameter by Value
Burst compilation error occurred:
```
BC1064: Unsupported parameter `TileMeshJobData` `data` in function `GenerateTileMeshJob.SampleNoise(...)`: 
structs cannot be passed to or returned from external functions in burst. 
To fix this issue, use a reference or pointer.
```

## Issue 2: Struct Return Type
Burst compilation error occurred:
```
BC1064: Unsupported return type `Unity.Mathematics.float3` for function `GenerateTileMeshJob.CalculateNormalFromHeightfield(...)`: 
structs cannot be passed to or returned from external functions in burst. 
To fix this issue, use a reference or pointer.
```

## Root Cause
Burst doesn't allow:
1. Passing structs by value to functions marked with `[BurstCompile]`
2. Returning structs from functions marked with `[BurstCompile]`

When helper methods have `[BurstCompile]` attribute, Burst treats them as "external functions" with strict rules about struct handling.

## Solution
Two-part fix:

### Part 1: Pass structs by reference
Changed methods to pass `TileMeshJobData` by readonly reference using the `in` keyword.

### Part 2: Remove `[BurstCompile]` from helper methods
Removed `[BurstCompile]` attribute from static helper methods. They will still be Burst-compiled as part of the job, but won't be treated as separate external functions with struct restrictions.

## Changes Made

**File:** `Assets\_App\Ace of Ages\Terrain\TerrainMeshGenerationSystem.cs`

### 1. SampleNoise Method
**Before:**
```csharp
[BurstCompile]
private static float SampleNoise(double worldX, double worldZ, TileMeshJobData data)
```

**After:**
```csharp
private static float SampleNoise(double worldX, double worldZ, in TileMeshJobData data)
```

### 2. CalculateNormalFromHeightfield Method
**Before:**
```csharp
[BurstCompile]
private static float3 CalculateNormalFromHeightfield(double worldX, double worldZ, float stepSize, TileMeshJobData data)
```

**After:**
```csharp
private static float3 CalculateNormalFromHeightfield(double worldX, double worldZ, float stepSize, in TileMeshJobData data)
```

## Why This Works

### `in` Keyword
- **Burst Compatible**: Passes struct by reference, which Burst supports
- **Readonly**: Prevents accidental modification of the struct
- **Performance**: No struct copying overhead
- **Transparent**: Call sites don't need to change (compiler handles it automatically)

### Removing `[BurstCompile]` from Helper Methods
- **Still Burst-Compiled**: When called from a Burst-compiled job, helper methods are automatically Burst-compiled through inlining
- **No External Function Restrictions**: Burst doesn't treat them as separate external functions, so struct return types are allowed
- **Better Optimization**: Burst compiler can inline these methods for better performance
- **Simpler**: Follows Unity's recommended pattern for helper methods in Burst jobs

## Performance Notes
- Helper methods without `[BurstCompile]` are typically inlined by Burst when called from a Burst job
- This often results in better performance than treating them as separate external functions
- The `in` keyword prevents struct copying, maintaining optimal performance

## Verification
✅ Burst compilation successful - no errors  
✅ Camera-based prioritization still functional  
✅ Helper methods still Burst-compiled via inlining  
⚠️ Only minor naming convention warnings remain (non-blocking)

## Status
**RESOLVED** - The implementation is now fully Burst-compatible and ready for testing.

## Date
March 17, 2026

