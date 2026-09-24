// ============================================
// Toon_Common.hlsl
// 用途: 框架总入口，聚合核心库与共享常量
// 依赖: URP Core.hlsl
// 作者: Yang
// ============================================

#ifndef TOON_COMMON_INCLUDED
#define TOON_COMMON_INCLUDED
// 内容

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    float4 _BaseMap_ST;
    half _ShadowThreshold;
    half4 _ShadowColor;
    half _ShadowSmoothness;
    half _SpecularPower;
    half _SpecularThreshold;
    half4 _RimColor;
    half _RimThreshold;
    half _OutlineWidth;
    half4 _OutlineColor;
    half _OutlineEnabled;
    half _Cutoff;
CBUFFER_END

#include "Toon_Surface.hlsl"
#include "Toon_Lighting.hlsl"

#endif

