// ============================================
// Toon_Lighting.hlsl
// 用途: 光照
// 依赖: Toon_Common.hlsl
// 作者: Yang
// ============================================

#ifndef TOON_LIGHTING_INCLUDED
#define TOON_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
//漫反射
half3 ToonDiffuse(half3 baseColor, half3 normalWS, Light mainLight,
                  half threshold, half smoothness, half4 shadowColor)
{
    half NdotL = dot(normalWS, mainLight.direction);
    half lambert = NdotL * 0.5 + 0.5;
    lambert *= mainLight.shadowAttenuation;   // 影子也过同一个 smoothstep 阈值
    half lightAtten = smoothstep(threshold - smoothness, threshold + smoothness, lambert);

    half3 litColor = baseColor * mainLight.color;
    half3 ambient = SampleSH(normalWS);
    half3 darkColor = baseColor * shadowColor.rgb * ambient * 2;
    return lerp(darkColor, litColor, lightAtten);
}
//高光，和边缘光一样在世界空间计算
half3 ToonSpecular(half3 normalWS, half3 viewDirWS, Light mainLight,
                   half power, half threshold)
{
    half3 halfDir = normalize(mainLight.direction + viewDirWS);
    half spec = pow(saturate(dot(normalWS, halfDir)), power);
    return mainLight.color * smoothstep(threshold - 0.05, threshold + 0.05, spec);
}
//边缘光
half3 ToonRim(half3 normalWS, half3 viewDirWS, half3 rimColor, half threshold)
{
    half rim = 1.0 - saturate(dot(normalWS, viewDirWS));
    return rimColor * smoothstep(threshold, threshold + 0.2, rim);
}
//附加光（点光源等）：篝火氛围光。柔和 lambert、不带阈值量化、不算阴影
half3 ToonAdditionalLights(half3 baseColor, half3 normalWS, half3 positionWS)
{
    half3 add = 0;
    uint count = GetAdditionalLightsCount();
    for (uint i = 0; i < count; i++)
    {
        Light l = GetAdditionalLight(i, positionWS);
        half NdotL = saturate(dot(normalWS, l.direction)) * 0.5 + 0.5;
        add += baseColor * l.color * l.distanceAttenuation * NdotL;
    }
    return add;
}
#endif