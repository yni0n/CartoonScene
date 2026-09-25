Shader "CartoonScene/Character/Toon_Opaque"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _ShadowThreshold("Shadow Threshold", Range(0, 1)) = 0.5
        _ShadowColor("Shadow Color", Color) = (0.7, 0.75, 0.9, 1)
        _ShadowSmoothness("Shadow Smoothness", Range(0.001, 0.5)) = 0.05
        _SpecularPower("Specular Power", Range(2, 128)) = 32
        _SpecularThreshold("Specular Threshold", Range(0, 1)) = 0.5
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimThreshold("Rim Threshold", Range(0, 1)) = 0.6
        _OutlineWidth("Outline Width", Range(0, 0.1)) = 0.02
        _OutlineColor("Outline Color", Color) = (0.2, 0.15, 0.2, 1)
        [Toggle(_OUTLINE_ON)] _OutlineEnabled("Outline Enabled", Float) = 1
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        // Stencil 层号：物体用主 Pass 盖"自己层"的章，描边 Pass 只避开同层像素。
        // 分层后互不误伤：角色(1)描边可画在地面(2)接触处；帐篷壳被自己层(3)拦下
        _StencilRef("Stencil Ref", Range(0, 255)) = 1
        // 主 Pass 是否向模板缓冲写标记（Replace=写，Keep=不写）
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilWriteOp("Stencil Write Op", Float) = 2
        // 描边 Pass 的模板测试模式：NotEqual(6)=避开同层（开放几何防糊脸），
        // Always(8)=不拦截（封闭物体，保留内部结构褶皱线）
        [Enum(UnityEngine.Rendering.CompareFunction)] _OutlineStencilMode("Outline Stencil Mode", Float) = 6
        // 树木摆动：只给树木变体材质开（SWAY_ON），其他物体保持 0
        [Toggle(SWAY_ON)] _SwayEnabled("Sway Enabled", Float) = 0
        _SwayStrength("Sway Strength", Range(0, 0.2)) = 0.05
        _SwaySpeed("Sway Speed", Range(0, 4)) = 1.2
        _SwayHeightScale("Sway Height Scale", Range(0.05, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            // 主 Pass 盖"自己层"的章（层号来自材质属性 _StencilRef）
            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass [_StencilWriteOp]
            }
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "../Common/Toon_Common.hlsl"
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma shader_feature_local SWAY_ON

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz)
                                  + ToonSwayOffsetWS(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                half4 baseMap = SampleBaseMap(IN.uv);
                clip(baseMap.a - _Cutoff);          // alpha 小于阈值 → 丢弃该像素
                half3 baseColor = baseMap.rgb * _BaseColor.rgb;
                half3 color = ToonDiffuse(baseColor, IN.normalWS, mainLight,
                                          _ShadowThreshold, _ShadowSmoothness, _ShadowColor);
                color += ToonSpecular(IN.normalWS, viewDirWS, mainLight,
                                      _SpecularPower, _SpecularThreshold);
                color += ToonRim(IN.normalWS, viewDirWS, _RimColor.rgb, _RimThreshold);
                color += ToonAdditionalLights(baseColor, IN.normalWS, IN.positionWS);
                return half4(color, 1);
            }

            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            // 自定义 LightMode：不再随不透明队列混排，由 ToonOutlineFeature
            // 在"所有不透明物体画完之后"统一绘制（保证盖章先于描边）
            Tags { "LightMode" = "ToonOutline" }
            Cull Front
            // 描边只画在"非自己层"的像素上：
            // 开放几何（帐篷布/地贴平面）的背面壳会被同层的章拦下，不再糊在表面上
            Stencil
            {
                Ref [_StencilRef]
                Comp [_OutlineStencilMode]
            }

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local _OUTLINE_ON
            #pragma shader_feature_local SWAY_ON

            #include "../Common/Toon_Common.hlsl"

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                // 基础位置先做与主 Pass 相同的摆动偏移，描边壳才不会与本体分离
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz)
                                  + ToonSwayOffsetWS(IN.positionOS.xyz);
                float4 posCS = TransformWorldToHClip(positionWS);

            #if defined(_OUTLINE_ON)
                // UV1 没有平滑法线数据时，退回原始法线
                float3 nOS = dot(IN.smoothNormalOS, IN.smoothNormalOS) > 0.0001
                           ? IN.smoothNormalOS
                           : IN.normalOS;
                float3 viewNormal = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(nOS)));
                float aspect = _ScreenParams.x / _ScreenParams.y;
                posCS.x += viewNormal.x * _OutlineWidth * posCS.w * 0.2;
                posCS.y += viewNormal.y * _OutlineWidth * posCS.w * 0.2 * aspect * _ProjectionParams.x;
                OUT.positionHCS = posCS;
            #else
                // keyword 关闭：把顶点塌缩到退化图元，GPU 不会产生任何像素
                OUT.positionHCS = float4(0, 0, 0, 0);
            #endif

                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0   // 只写深度，不写任何颜色

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma shader_feature_local SWAY_ON

            #include "../Common/Toon_Common.hlsl"

            // 引擎渲染 shadow map 时会设置这个全局值
            //（与 URP 官方 ShadowCasterPass.hlsl 第 13 行同款声明）
            float3 _LightDirection;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(Attributes IN)
            {
                ShadowVaryings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz)
                                  + ToonSwayOffsetWS(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // 沿法线+光方向偏移一个微小量，防止表面把自己挡住（shadow acne 条纹）
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

    }
}
