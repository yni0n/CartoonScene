// ============================================
// Toon_Surface.hlsl
// 用途: 数据与贴图采样
// 依赖: Toon_Common.hlsl
// 作者: Yang
// ============================================

#ifndef TOON_SURFACE_INCLUDED
#define TOON_SURFACE_INCLUDED

//纹理和采样器
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);


struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float3 smoothNormalOS : TEXCOORD1;
    float2 uv : TEXCOORD0;
};

struct Varyings
{
    float4 positionHCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float3 positionWS : TEXCOORD2;
};

half4 SampleBaseMap(float2 uv)
{
    return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
}

#endif
