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
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #include "../Common/Toon_Common.hlsl"

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                half3 baseColor = SampleBaseMap(IN.uv).rgb * _BaseColor.rgb;
                half3 color = ToonDiffuse(baseColor, IN.normalWS, mainLight,
                                          _ShadowThreshold, _ShadowSmoothness, _ShadowColor);
                color += ToonSpecular(IN.normalWS, viewDirWS, mainLight,
                                      _SpecularPower, _SpecularThreshold);
                color += ToonRim(IN.normalWS, viewDirWS, _RimColor.rgb, _RimThreshold);
                return half4(color, 1);
            }

            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag

            #include "../Common/Toon_Common.hlsl"

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float4 posCS = TransformObjectToHClip(IN.positionOS.xyz);
                float3 viewNormal = normalize(TransformWorldToViewDir(TransformObjectToWorldNormal(IN.normalOS)));
                float aspect = _ScreenParams.x / _ScreenParams.y;
                posCS.x += viewNormal.x * _OutlineWidth * posCS.w * 0.2;
                posCS.y += viewNormal.y * _OutlineWidth * posCS.w * 0.2 * aspect * _ProjectionParams.x;
                OUT.positionHCS = posCS;
                OUT.uv = viewNormal.xy * 0.5 + 0.5;
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

    }
}
