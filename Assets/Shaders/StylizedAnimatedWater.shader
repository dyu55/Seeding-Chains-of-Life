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
        _LightingStrength ("Lighting Strength", Range(0, 1)) = 1
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
            Cull Off // Reverted to Cull Off: The Voxel mesh generation culls top faces if set to Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _NormalStrength;
                float _NormalTiling;
                float4 _ScrollA;
                float4 _ScrollB;
                float _FresnelPower;
                float _HighlightStrength;
                float _FlowContrast;
                float _LightingStrength;
                float _Opacity;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                // --- Vertex Displacement (Physical Waves) ---
                // Convert to world space first so chunks seamlessly match each other
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                
                float time = _Time.y * 1.2f;
                // Two overlapping waves for organic, non-repeating vertical movement
                float waveA = sin(worldPos.x * 0.6f + time) * cos(worldPos.z * 0.45f + time * 0.8f);
                float waveB = sin(worldPos.x * 0.85f - time * 0.7f) * cos(worldPos.z * 0.75f + time * 1.1f);
                
                // Lift Y-axis by amplitude (approx 0.25 meters total variance, reduced by half)
                worldPos.y += (waveA + waveB) * 0.125f;
                
                // Finalize passing displaced positions to fragment shader
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.positionWS = worldPos;
                
                VertexNormalInputs norm = GetVertexNormalInputs(IN.normalOS);
                OUT.normalWS = norm.normalWS;
                OUT.viewDirWS = GetWorldSpaceNormalizeViewDir(worldPos);
                
                return OUT;
            }

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                float time = _Time.y;
                float2 wPos = IN.positionWS.xz * max(_NormalTiling, 0.05h);
                
                // Animate UVs for the flowing effect
                float2 uvA = wPos + time * _ScrollA.xy;
                float2 uvB = wPos + time * _ScrollB.xy;
                
                // 1. Correctly unpack the Unity Normal Map (Handles DXT5nm compression)
                // If it was just a flat blue texture, this returns (0,0,1).
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvA), _NormalStrength);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvB), _NormalStrength);
                
                // 2. Procedural Domain Warping (Math Waves)
                // This ensures the water is ALWAYS moving and organic, even if the normal map is completely missing!
                float2 mathDistort = float2(
                    sin(uvA.y * 3.0h) * cos(uvB.x * 2.0h),
                    cos(uvA.x * 2.5h) * sin(uvB.y * 3.5h)
                );
                
                // Combine texture normals and procedural mathematical normals
                half2 finalDistort = half2(nA.x + nB.x, nA.y + nB.y) + mathDistort * 0.15h * _NormalStrength;
                
                // Apply wobbly distortion to the actual world normal
                half3 normalWS = normalize(IN.normalWS + half3(finalDistort.x, 0.0h, finalDistort.y));
                half3 viewDir = normalize(IN.viewDirWS);
                
                Light mainLight = GetMainLight();
                half3 lightDir = normalize(mainLight.direction);
                
                // --- Lighting ---
                half ndotl = saturate(dot(normalWS, lightDir));
                half3 ambient = SampleSH(normalWS);
                ambient = max(ambient, half3(0.08h, 0.12h, 0.18h)); // Prevent pitch black in shadow
                
                half directDiffuse = smoothstep(0.4h, 0.45h, ndotl); // Sharp toon ramp
                half3 directLightColor = mainLight.color.rgb * directDiffuse;
                half3 totalLight = ambient + directLightColor;

                // --- Base Color ---
                half3 finalColor = _BaseColor.rgb * lerp(half3(1.0h, 1.0h, 1.0h), totalLight, _LightingStrength);

                // --- Stylized Organic Caustics/Foam Networks ---
                // We use "Domain Warping", stretching sine waves inside other sine waves to create water ripples instead of dots!
                float2 foamUV = wPos * 1.5h + finalDistort * 1.8h;
                
                float2 q = float2(
                    sin(foamUV.x + time * 1.2h),
                    cos(foamUV.y + time * 1.5h)
                );
                
                // The organic network formula (generates interconnected lines, not polka dots)
                half organicNoise = sin(foamUV.x + q.y * 2.5h) + cos(foamUV.y + q.x * 2.5h); // Range roughly [-2, 2]
                
                // Control thickness via FlowContrast
                half foamLines = smoothstep(0.8h - _FlowContrast * 0.5h, 1.8h, organicNoise);
                
                // Harmonize the foam color by making it a brightened version of the base color
                // This prevents stark contrast against a darker colored lake
                half3 foamColor = saturate(_BaseColor.rgb * 1.3h + half3(0.05h, 0.1h, 0.15h)) * totalLight;
                finalColor = lerp(finalColor, foamColor, foamLines * 0.5h);

                // --- Fresnel Edge ---
                half fresnel = pow(saturate(1.0h - dot(viewDir, normalWS)), _FresnelPower);
                finalColor += fresnel * ambient * _HighlightStrength * 1.5h;

                // --- Stylized Sun Specular ---
                half3 halfVector = normalize(lightDir + viewDir);
                half ndoth = saturate(dot(normalWS, halfVector));
                half specular = pow(ndoth, 150.0h);
                specular = smoothstep(0.5h, 0.55h, specular); // Sharp toon cutoff
                finalColor += specular * _HighlightStrength * 3.0h * mainLight.color.rgb;

                // --- Final Opacity ---
                // Water becomes opaquer where there are highlights, foam, or edges
                half alpha = saturate(_Opacity + foamLines * 0.6h + specular + fresnel * 0.4h);
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
