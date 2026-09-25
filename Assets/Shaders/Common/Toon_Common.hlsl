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
    // 树木顶点摆动（SWAY_ON 材质开关控制，默认关闭）
    half _SwayStrength;      // 树梢最大摆幅（世界空间米）
    half _SwaySpeed;         // 摆动频率
    half _SwayHeightScale;   // 高度→权重的缩放（weight = saturate(y * scale)）
CBUFFER_END

// 顶点摆动：三个 Pass（主/描边/阴影）必须共用，否则壳和影子与本体分离
// 输入物体空间顶点位置，返回世界空间的偏移量
float3 ToonSwayOffsetWS(float3 positionOS)
{
#if defined(SWAY_ON)
    // 摆动权重 ∝ 高度：树根(原点)不动，越往上摆越多
    float weight = saturate(positionOS.y * _SwayHeightScale);
    // 相位由物体根的世界坐标决定 → 每棵树摆动节奏错开
    float3 rootWS = float3(unity_ObjectToWorld[0].w, unity_ObjectToWorld[1].w, unity_ObjectToWorld[2].w);
    float phase = dot(rootWS.xz, float2(0.6, 0.8)) * 1.5;
    // 双频正弦：慢频主体起伏 + 快频细碎颤动，避免机械感
    float t = _Time.y * _SwaySpeed;
    float sway = sin(t + phase) * 0.7 + sin(t * 1.63 + phase * 1.9) * 0.3;
    float3 windDir = normalize(float3(1.0, 0.0, 0.35));   // 全局风方向
    return windDir * (sway * _SwayStrength * weight);
#else
    return 0;
#endif
}

#include "Toon_Surface.hlsl"
#include "Toon_Lighting.hlsl"

#endif

