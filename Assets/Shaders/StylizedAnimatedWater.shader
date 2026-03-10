Shader "SCoL/StylizedAnimatedWater"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.10, 0.24, 0.44, 0.62)
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 1)) = 0.38
        _NormalTiling ("Normal Tiling", Float) = 0.42
        _ScrollA ("Scroll A", Vector) = (0.115, 0.035, 0, 0)
        _ScrollB ("Scroll B", Vector) = (-0.075, 0.085, 0, 0)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 5.5
        _HighlightStrength ("Highlight Strength", Range(0, 2)) = 0.06
        _FlowContrast ("Flow Contrast", Range(0, 1)) = 0.32
        _Opacity ("Opacity", Range(0, 1)) = 0.64
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _NormalStrength;
                float _NormalTiling;
                float4 _ScrollA;
                float4 _ScrollB;
                float _FresnelPower;
                float _HighlightStrength;
                float _FlowContrast;
                float _Opacity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.uv = IN.uv;
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(pos.positionWS);
                return OUT;
            }

            half3 SampleAnimatedNormal(float2 uv, float3 worldPos)
            {
                float2 flowUV = worldPos.xz * _NormalTiling + uv * 0.15;
                float2 uvA = flowUV + _Time.y * _ScrollA.xy;
                float2 uvB = flowUV + _Time.y * _ScrollB.xy;

                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA), _NormalStrength);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB), _NormalStrength);

                half3 n = normalize(half3(nA.xy + nB.xy, nA.z * nB.z));
                return n;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 normalWS = SampleAnimatedNormal(IN.uv, IN.positionWS);
                half3 viewDir = normalize(IN.viewDirWS);
                half fresnel = pow(saturate(1.0h - dot(viewDir, normalWS)), _FresnelPower);
                half ripple = saturate((normalWS.x + normalWS.y) * 0.5h + 0.5h);
                half flow = sin(IN.positionWS.x * 0.24h + _Time.y * 2.8h) * 0.5h + 0.5h;
                flow = lerp(flow, sin(IN.positionWS.z * 0.31h - _Time.y * 1.9h) * 0.5h + 0.5h, 0.5h);
                half band = sin((IN.positionWS.x + IN.positionWS.z) * 0.42h + _Time.y * 2.2h) * 0.5h + 0.5h;

                half3 color = _BaseColor.rgb;
                color *= lerp(0.84h, 1.06h, ripple);
                color *= lerp(1.0h - _FlowContrast, 1.0h + _FlowContrast, flow);
                color += band * 0.035h;
                color += fresnel * _HighlightStrength;

                half alpha = saturate(_Opacity * lerp(0.88h, 1.03h, ripple) * lerp(0.92h, 1.06h, band));
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
