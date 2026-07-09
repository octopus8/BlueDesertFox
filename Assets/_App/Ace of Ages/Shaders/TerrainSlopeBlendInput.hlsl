#ifndef ACE_OF_AGES_TERRAIN_SLOPE_BLEND_INPUT_INCLUDED
#define ACE_OF_AGES_TERRAIN_SLOPE_BLEND_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _FlatMap_ST;
    float _FlatTiling;
    float _SteepTiling;
    float _SlopeStart;
    float _SlopeEnd;
    half _Smoothness;
    half _Metallic;
CBUFFER_END

TEXTURE2D(_FlatMap);
SAMPLER(sampler_FlatMap);
TEXTURE2D(_SteepMap);
SAMPLER(sampler_SteepMap);

#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float, _FlatTiling)
    UNITY_DOTS_INSTANCED_PROP(float, _SteepTiling)
    UNITY_DOTS_INSTANCED_PROP(float, _SlopeStart)
    UNITY_DOTS_INSTANCED_PROP(float, _SlopeEnd)
    UNITY_DOTS_INSTANCED_PROP(float, _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float, _Metallic)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

static float unity_DOTS_Sampled_FlatTiling;
static float unity_DOTS_Sampled_SteepTiling;
static float unity_DOTS_Sampled_SlopeStart;
static float unity_DOTS_Sampled_SlopeEnd;
static float unity_DOTS_Sampled_Smoothness;
static float unity_DOTS_Sampled_Metallic;

void SetupDOTSTerrainSlopeBlendMaterialPropertyCaches()
{
    unity_DOTS_Sampled_FlatTiling = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _FlatTiling);
    unity_DOTS_Sampled_SteepTiling = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SteepTiling);
    unity_DOTS_Sampled_SlopeStart = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SlopeStart);
    unity_DOTS_Sampled_SlopeEnd = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _SlopeEnd);
    unity_DOTS_Sampled_Smoothness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Smoothness);
    unity_DOTS_Sampled_Metallic = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _Metallic);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSTerrainSlopeBlendMaterialPropertyCaches()

#define _FlatTiling unity_DOTS_Sampled_FlatTiling
#define _SteepTiling unity_DOTS_Sampled_SteepTiling
#define _SlopeStart unity_DOTS_Sampled_SlopeStart
#define _SlopeEnd unity_DOTS_Sampled_SlopeEnd
#define _Smoothness unity_DOTS_Sampled_Smoothness
#define _Metallic unity_DOTS_Sampled_Metallic

#else

#ifndef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
    #define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES()
#endif

#endif

half3 SampleTriplanar(TEXTURE2D_PARAM(tex, samplerTex), float3 worldPos, float3 worldNormal, float tiling)
{
    float3 weights = abs(worldNormal);
    weights = weights / (weights.x + weights.y + weights.z + 1e-5);

    half3 xSample = SAMPLE_TEXTURE2D(tex, samplerTex, worldPos.yz * tiling).rgb;
    half3 ySample = SAMPLE_TEXTURE2D(tex, samplerTex, worldPos.xz * tiling).rgb;
    half3 zSample = SAMPLE_TEXTURE2D(tex, samplerTex, worldPos.xy * tiling).rgb;

    return xSample * weights.x + ySample * weights.y + zSample * weights.z;
}

#endif
